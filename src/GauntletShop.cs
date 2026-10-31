using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using R2API;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    // Native registered prefabs carry their markers to clients, including late
    // joiners. Currency, pickups and drone ownership use native server paths.
    internal sealed class GauntletShopTag:MonoBehaviour
    {
        internal int Slot=>GauntletShop.SlotAt(transform.position);
        internal UniquePickup Offer;
        private void Start()
        {
            if(Slot==GauntletShop.Ready)
                foreach(var renderer in GetComponentsInChildren<Renderer>(true))renderer.forceRenderingOff=true;
        }
    }

    internal sealed class GauntletShop:MonoBehaviour
    {
        private static GameObject terminalPrefab,scrapperPrefab;
        private static Hook scrapCostHook;
        private delegate void PayOriginal(CostTypeDef definition,CostTypeDef.PayCostContext context,CostTypeDef.PayCostResults result);
        private delegate void PayHook(PayOriginal original,CostTypeDef definition,CostTypeDef.PayCostContext context,CostTypeDef.PayCostResults result);
        private static readonly GameObject[] dronePrefabs=new GameObject[3];
        private static readonly string[] droneKeys={"Drone1Broken_prefab","Drone2Broken_prefab","MissileDroneBroken_prefab"};
        internal const int GreenTrade=GauntletShopPlan.GreenTrade,RedTrade=GauntletShopPlan.RedTrade,Scrapper=GauntletShopPlan.Scrapper,Ready=GauntletShopPlan.Ready;
        private readonly List<GameObject> stock=new List<GameObject>();
        private readonly List<int> prices=new List<int>();
        private readonly List<int> equipmentPrices=new List<int>(),dronePrices=new List<int>();
        private readonly GauntletShopPlan plan=new GauntletShopPlan();
        private readonly Dictionary<uint,float> nextVote=new Dictionary<uint,float>();
        private GauntletController owner;
        private bool opened,complete;
        private float retryAt,nextStartAttempt;
        private int smallChestPrice;
        private long generation;
        private void Awake(){owner=GetComponent<GauntletController>();}
        internal static Vector3 Position(int slot)
        {
            var p=GauntletShopPlan.Position(slot);
            return FortressBattlefield.Origin+new Vector3(p.X,p.Y,p.Z);
        }
        internal static int SlotAt(Vector3 position)
        {for(int i=0;i<=Ready;i++)if((Position(i)-position).sqrMagnitude<1)return i;return -1;}
        internal static void Register()
        {
            LanguageAPI.Add("GAUNTLET_SHOP_READY","Ready brazier");
            LanguageAPI.Add("GAUNTLET_SHOP_READY_CONTEXT","Toggle ready for the next round");
            LanguageAPI.Add("GAUNTLET_SHOP_GREEN","Green scrap exchange");
            LanguageAPI.Add("GAUNTLET_SHOP_RED","Red scrap exchange");
            LanguageAPI.Add("GAUNTLET_SHOP_GREEN_CONTEXT","Trade for this green item");
            LanguageAPI.Add("GAUNTLET_SHOP_RED_CONTEXT","Trade for this red item");
            terminalPrefab=Clone("LunarShopTerminal_prefab","ArenaGauntletMerchantTerminal");
            var card=FortressAssets.Load<InteractableSpawnCard>("iscScrapper_asset");
            if(!card || !card.prefab)throw new InvalidOperationException("Native scrapper prefab unavailable.");
            scrapperPrefab=PrefabAPI.InstantiateClone(card.prefab,"ArenaGauntletMerchantScrapper",true);
            scrapperPrefab.AddComponent<GauntletShopTag>();
            for(int i=0;i<dronePrefabs.Length;i++)dronePrefabs[i]=Clone(droneKeys[i],"ArenaGauntletMerchantDrone"+i);
        }
        private static GameObject Clone(string key,string name)
        {
            var source=FortressAssets.Load<GameObject>(key);
            if(!source)throw new InvalidOperationException("Native merchant prefab unavailable: "+key);
            var result=PrefabAPI.InstantiateClone(source,name,true);result.AddComponent<GauntletShopTag>();return result;
        }
        internal static void Install()
        {
            GauntletShopLoot.Install();
            GauntletShopPresentation.Install();
            On.RoR2.PurchaseInteraction.OnInteractionBegin+=Purchase;
            On.RoR2.PurchaseInteraction.GetInteractability+=Interactability;
            On.RoR2.PurchaseInteraction.CanBeAffordedByInteractor+=Affordable;
            On.RoR2.PurchaseInteraction.GetContextString+=Context;
            On.RoR2.ShopTerminalBehavior.SetHasBeenPurchased+=RefreshTerminal;
            On.RoR2.PickupPickerController.OnInteractionBegin+=OpenPicker;
            On.RoR2.PickupPickerController.HandlePickupSelected+=Choose;
            scrapCostHook=new Hook(typeof(CostTypeDef).GetMethod(nameof(CostTypeDef.PayCost),new[]{typeof(CostTypeDef.PayCostContext),typeof(CostTypeDef.PayCostResults)}),(PayHook)PayScrap);
        }
        private static void PayScrap(PayOriginal original,CostTypeDef definition,CostTypeDef.PayCostContext context,CostTypeDef.PayCostResults result)
        {
            var tag=context.purchasedObject?context.purchasedObject.GetComponent<GauntletShopTag>():null;
            if(!tag || (tag.Slot!=GreenTrade && tag.Slot!=RedTrade)){original(definition,context,result);return;}
            var exchange=new Inventory.ItemTransformation
            {
                originalItemIndex=ScrapFor(tag.Slot),newItemIndex=ItemIndex.None,
                minToTransform=1,maxToTransform=1,forbidTempItems=true,forbidRentItems=true,
                transformationType=ItemTransformationTypeIndex.None
            };
            if(exchange.TryTransform(context.activatorInventory,out var taken))result.AddTakenItemsFromTransformation(in taken);
        }
        private static ItemIndex ScrapFor(int slot)=>slot==GreenTrade?RoR2Content.Items.ScrapGreen.itemIndex:RoR2Content.Items.ScrapRed.itemIndex;
        private static void RefreshTerminal(On.RoR2.ShopTerminalBehavior.orig_SetHasBeenPurchased original,ShopTerminalBehavior terminal,bool purchased)
        {
            original(terminal,purchased);
            if(terminal.GetComponent<GauntletShopTag>())NativeAccess.Call(terminal,"UpdatePickupDisplayAndAnimations");
        }
        private static bool HasScrap(int slot,Interactor actor)
        {
            var inventory=actor?actor.GetComponent<CharacterBody>()?.inventory:null;
            var scrap=ScrapFor(slot);
            return inventory && !inventory.inventoryDisabled && !inventory.IsItemDisabled(scrap) && inventory.GetItemCountPermanent(scrap)>0;
        }
        internal void Prepare()
        {
            if(!NetworkServer.active || !owner || owner.ShuttingDown)return;
            if(stock.Count==0)Create(Ready);
        }
        internal void Open()
        {
            if(!NetworkServer.active)return;
            DestroyStock();plan.Reset();nextVote.Clear();prices.Clear();equipmentPrices.Clear();dronePrices.Clear();generation++;
            SyncPurchases();
            opened=true;complete=false;retryAt=0;Restock();
        }
        private void Restock()
        {
            if(complete || Time.unscaledTime<retryAt)return;
            retryAt=Time.unscaledTime+3;
            if(stock.Count==0)smallChestPrice=GauntletMerchantPrices.SmallChest(owner.Run);
            // Per slot: one empty pool (e.g. lunar items disabled) must not skip the later slots, including the ready brazier.
            for(int slot=0;slot<=Ready;slot++)
                if(!stock.Any(go=>go && go.GetComponent<GauntletShopTag>().Slot==slot))
                    try {Create(slot);}
                    catch(Exception error){GauntletPlugin.Log.LogWarning("Merchant setup will retry: "+error);}
            complete=Enumerable.Range(0,Ready+1).All(slot=>stock.Any(go=>go && go.GetComponent<GauntletShopTag>().Slot==slot));
        }
        private void Create(int slot)
        {
            bool drone=slot>=GauntletShopPlan.DroneStart && slot<GreenTrade;
            var prefab=slot==Scrapper?scrapperPrefab:drone?dronePrefabs[slot-GauntletShopPlan.DroneStart]:terminalPrefab;
            var go=Instantiate(prefab,Position(slot),Quaternion.identity);
            try
            {
                var tag=go.GetComponent<GauntletShopTag>();
                var terminal=go.GetComponent<ShopTerminalBehavior>();
                var purchase=go.GetComponent<PurchaseInteraction>();
                if(terminal)
                {
                    terminal.selfGeneratePickup=false;terminal.dropTable=null;
                    var offer=slot==Ready?UniquePickup.none:Roll(slot);
                    if(slot!=Ready && !offer.isValid)throw new InvalidOperationException("No eligible stock for merchant slot "+slot);
                    tag.Offer=offer;terminal.SetPickup(offer);terminal.dropAmount=1;
                }
                if(purchase)
                {
                    purchase.automaticallyScaleCostWithDifficulty=false;
                    if(!drone)
                    {
                        purchase.NetworkcostType=slot==Ready?CostTypeIndex.None:slot>=GauntletShopPlan.LunarStart && slot<GauntletShopPlan.EquipmentStart?CostTypeIndex.LunarCoin:
                            slot==GreenTrade?CostTypeIndex.GreenItem:slot==RedTrade?CostTypeIndex.RedItem:CostTypeIndex.Money;
                        purchase.Networkcost=slot==Ready?0:slot>=GauntletShopPlan.LunarStart && slot<GauntletShopPlan.EquipmentStart?2:slot==GreenTrade || slot==RedTrade?1:Price(tag.Offer);
                        purchase.saleStarCompatible=false;
                    }
                    else purchase.Networkcost=owner.Run.GetDifficultyScaledCost(prefab.GetComponent<PurchaseInteraction>().cost);
                    purchase.NetworkdisplayNameToken=slot==Ready?"GAUNTLET_SHOP_READY":slot==GreenTrade?"GAUNTLET_SHOP_GREEN":slot==RedTrade?"GAUNTLET_SHOP_RED":purchase.displayNameToken;
                    purchase.NetworkcontextToken=slot==Ready?"GAUNTLET_SHOP_READY_CONTEXT":slot==GreenTrade?"GAUNTLET_SHOP_GREEN_CONTEXT":slot==RedTrade?"GAUNTLET_SHOP_RED_CONTEXT":purchase.contextToken;
                    purchase.SetAvailable(opened);
                }
                var picker=go.GetComponent<PickupPickerController>();if(picker)picker.SetAvailable(opened);
                NetworkServer.Spawn(go);stock.Add(go);
                if(slot<GauntletShopPlan.ItemChoices)prices.Add(purchase.cost);
                else if(slot>=GauntletShopPlan.EquipmentStart && slot<GauntletShopPlan.DroneStart)equipmentPrices.Add(purchase.cost);
                else if(drone)dronePrices.Add(purchase.cost);
            }
            catch {Destroy(go);throw;}
        }
        private UniquePickup Roll(int slot)
        {
            var run=owner.Run;List<PickupIndex> list;
            if(slot<GauntletShopPlan.ItemChoices)
            {
                int tier=GauntletShopPricing.RollTier(owner.ArenaRng.nextNormalizedFloat,run.availableTier1DropList.Count,run.availableTier2DropList.Count,run.availableTier3DropList.Count);
                list=tier==0?run.availableTier1DropList:tier==1?run.availableTier2DropList:run.availableTier3DropList;
                // Like BiggerBazaar, each slot is an independent draw with
                // replacement. Do not reroll duplicates or shift rarity weights.
                return list.Count==0?UniquePickup.none:new UniquePickup(list[owner.ArenaRng.RangeInt(0,list.Count)]);
            }
            else if(slot<GauntletShopPlan.EquipmentStart)list=run.availableLunarItemDropList;
            else if(slot<GauntletShopPlan.DroneStart)list=run.availableEquipmentDropList;
            else list=slot==GreenTrade?run.availableTier2DropList:run.availableTier3DropList;
            var eligible=list.Where(index=>
            {
                var def=PickupCatalog.GetPickupDef(index);var item=def==null?null:ItemCatalog.GetItemDef(def.itemIndex);
                return def!=null && (!item || !item.ContainsTag(ItemTag.Scrap) && !item.ContainsTag(ItemTag.PriorityScrap));
            }).Distinct().ToList();
            var used=new HashSet<PickupIndex>(stock.Where(go=>go && go.GetComponent<ShopTerminalBehavior>()).Select(go=>go.GetComponent<GauntletShopTag>().Offer.pickupIndex));
            var unique=eligible.Where(index=>!used.Contains(index)).ToList();if(unique.Count>0)eligible=unique;
            return eligible.Count==0?UniquePickup.none:new UniquePickup(eligible[owner.ArenaRng.RangeInt(0,eligible.Count)]);
        }
        private int Price(UniquePickup pickup)
        {
            var def=PickupCatalog.GetPickupDef(pickup.pickupIndex);var item=def==null?null:ItemCatalog.GetItemDef(def.itemIndex);
            if(item)return GauntletShopPricing.Price(smallChestPrice,item.tier==ItemTier.Tier3?2:item.tier==ItemTier.Tier2?1:0);
            return Mathf.Max(1,owner.Run.GetDifficultyScaledCost(50));
        }
        internal uint ClearReward(int items)
        {
            if(opened && prices.Count==GauntletShopPlan.ItemChoices)return GauntletShopPricing.ClearReward(prices,equipmentPrices,dronePrices,items);
            uint reference=GauntletShopPricing.ExpectedReward(GauntletMerchantPrices.SmallChest(owner.Run),items);
            uint allowance=GauntletShopPricing.ClearReward(Array.Empty<int>(),new[]{owner.Run.GetDifficultyScaledCost(50)},
                new[]{owner.Run.GetDifficultyScaledCost(40),owner.Run.GetDifficultyScaledCost(40),owner.Run.GetDifficultyScaledCost(60)},items);
            return (uint)Math.Min(uint.MaxValue,(ulong)reference+allowance);
        }
        private int PurchaseLimit(int slot)
        {
            int tier=PurchaseTier(slot);
            return tier>=0?GauntletShopPlan.ItemLimit(tier):1;
        }
        private int PurchaseGroup(int slot)
        {
            int tier=PurchaseTier(slot);
            // Gold item displays share one player-wide allowance per rarity.
            // Other vendors retain their independent display allowance.
            return tier>=0?-1-tier:slot;
        }
        private int PurchaseTier(int slot)
        {
            if(slot<0 || slot>=GauntletShopPlan.ItemChoices)return -1;
            var go=stock.FirstOrDefault(value=>value && value.GetComponent<GauntletShopTag>().Slot==slot);
            var def=go?PickupCatalog.GetPickupDef(go.GetComponent<GauntletShopTag>().Offer.pickupIndex):null;
            var item=def==null?null:ItemCatalog.GetItemDef(def.itemIndex);
            return item?(item.tier==ItemTier.Tier1?0:item.tier==ItemTier.Tier2?1:2):-1;
        }
        internal void Close()
        {
            opened=false;StopAllCoroutines();
            foreach(var go in stock)
            {
                if(!go)continue;
                foreach(var purchase in go.GetComponentsInChildren<PurchaseInteraction>(true))purchase.SetAvailable(false);
                foreach(var picker in go.GetComponentsInChildren<PickupPickerController>(true))picker.SetAvailable(false);
            }
        }
        internal void Tick()
        {
            if(!opened || !NetworkServer.active || !owner.Fortress.CanManage)return;
            Restock();
            // Dead players cannot reach the brazier; like the arrival gate they must not block the round (BeginRound revives them).
            // A merchant slot that keeps failing must not block it either.
            var players=PlayerCharacterMasterController.instances.Where(p=>p && p.isConnected && p.master && p.master.GetBody() is CharacterBody body && body.healthComponent && body.healthComponent.alive).ToArray();
            bool ready=!owner.Run.NetworkinSceneTransition && NetworkUser.AllParticipatingNetworkUsersReady() && !ScrapperBusy();
            if(plan.Tick(Time.unscaledTime,players.Select(p=>p.master.netId.Value),ready) && Time.unscaledTime>=nextStartAttempt)
            {
                int completed=owner.Flow.Round;
                nextStartAttempt=Time.unscaledTime+3;
                try {NativeAccess.Call(owner.Run,"CleanUpCurrentWave");}
                catch(Exception error){GauntletPlugin.Log.LogWarning("Previous wave cleanup will retry before the next round. "+error);return;}
                if(!owner.Flow.NextInPlace())return;
                Close();
                if(GauntletBalance.AdvancesDifficultyStage(completed))owner.Run.NetworkstageClearCount=owner.Run.stageClearCount+1;
                owner.Encounter.Reset();owner.ActiveBudget=null;owner.RetryWaveAt=-1;
                owner.Fortress.WaitingForStart=true;owner.Fortress.BeginRound();GauntletNetwork.Broadcast(owner);return;
            }
            string status=$"READY {plan.ReadyCount}/{players.Length}";
            if(owner.ThemeName!=status){owner.ThemeName=status;GauntletNetwork.Broadcast(owner);}
        }
        private bool ScrapperBusy()=>stock.Any(go=>go && go.GetComponent<ScrapperController>() is ScrapperController scrapper &&
            (!scrapper.IsRewardPickupQueueEmpty() || scrapper.esm && !(scrapper.esm.state is EntityStates.Scrapper.Idle)));
        private static GauntletShop Current(GauntletShopTag tag)
        {
            var owner=GauntletController.Current;
            return tag && tag.Slot>=0 && owner && owner.Fortress.InCastle?owner.Shop:null;
        }
        private static bool CanShop(GauntletShop shop)=>shop && shop.opened && shop.owner.Fortress.CanManage && shop.owner.Flow.Phase==GauntletPhase.AwaitingExit;
        private void SyncPurchases()
        {
            var state=owner.Fortress.State;
            state.ShopPlayers=PlayerCharacterMasterController.instances.Where(p=>p && p.isConnected && p.master).Take(64).Select(p=>p.master.netId.Value).ToArray();
            var limits=Enumerable.Range(0,GauntletShopPlan.SlotCount).Select(PurchaseLimit).ToArray();
            var groups=Enumerable.Range(0,GauntletShopPlan.SlotCount).Select(PurchaseGroup).ToArray();
            state.ShopBought=state.ShopPlayers.Select(id=>plan.Mask(id,slot=>limits[slot],slot=>groups[slot])).ToArray();state.Revision++;
            GauntletNetwork.Broadcast(owner);
        }
        private static Interactability Interactability(On.RoR2.PurchaseInteraction.orig_GetInteractability original,PurchaseInteraction purchase,Interactor actor)
        {
            var tag=purchase.GetComponent<GauntletShopTag>();if(!tag)return original(purchase,actor);
            var owner=GauntletController.Current;
            var phase=NetworkServer.active?owner?.Flow.Phase:owner?.RemotePhase;
            if(!owner || phase!=GauntletPhase.AwaitingExit)return RoR2.Interactability.Disabled;
            var master=actor?actor.GetComponent<CharacterBody>()?.master:null;
            if(master && tag.Slot!=Ready && tag.Slot!=Scrapper)
            {
                var state=owner.Fortress.State;int player=Array.IndexOf(state.ShopPlayers,master.netId.Value);
                if(player>=0 && player<state.ShopBought.Length && (state.ShopBought[player]&(1<<tag.Slot))!=0 && !Patronage(owner,master,tag))return RoR2.Interactability.Disabled;
            }
            if((tag.Slot==GreenTrade || tag.Slot==RedTrade) && !HasScrap(tag.Slot,actor))return RoR2.Interactability.ConditionsNotMet;
            return original(purchase,actor);
        }
        private static bool Affordable(On.RoR2.PurchaseInteraction.orig_CanBeAffordedByInteractor original,PurchaseInteraction purchase,Interactor actor)
        {
            var tag=purchase.GetComponent<GauntletShopTag>();
            var body=actor?actor.GetComponent<CharacterBody>():null;
            if(tag && body && Patronage(GauntletController.Current,body.master,tag))return true;
            return tag && (tag.Slot==GreenTrade || tag.Slot==RedTrade)?HasScrap(tag.Slot,actor):original(purchase,actor);
        }
        private static int PatronageTier(GauntletShopTag tag)
        {
            if(!tag || tag.Slot<0 || tag.Slot>=GauntletShopPlan.ItemChoices)return -1;
            var def=PickupCatalog.GetPickupDef(tag.Offer.pickupIndex);var item=def==null?null:ItemCatalog.GetItemDef(def.itemIndex);
            return item?(item.tier==ItemTier.Tier1?0:item.tier==ItemTier.Tier2?1:-1):-1;
        }
        private static string Context(On.RoR2.PurchaseInteraction.orig_GetContextString original,PurchaseInteraction purchase,Interactor actor)
        {
            var tag=purchase.GetComponent<GauntletShopTag>();var body=actor?actor.GetComponent<CharacterBody>():null;
            return tag && body && Patronage(GauntletController.Current,body.master,tag)?"Claim free item (Patronage)":original(purchase,actor);
        }
        private static bool Patronage(GauntletController owner,CharacterMaster master,GauntletShopTag tag)
        {
            if(!owner || !master)return false;int tier=PatronageTier(tag);if(tier<0)return false;
            if(NetworkServer.active)return owner.Meta.Free(master,tier);
            return owner.Meta.Remote.TryGetValue(master.netId.Value,out var status) && status.Patronage>tier;
        }
        private static void Purchase(On.RoR2.PurchaseInteraction.orig_OnInteractionBegin original,PurchaseInteraction purchase,Interactor actor)
        {
            var tag=purchase.GetComponent<GauntletShopTag>();if(!tag){original(purchase,actor);return;}
            if(!NetworkServer.active)return;
            var shop=Current(tag);var body=actor?actor.GetComponent<CharacterBody>():null;var player=body && body.master?body.master.playerCharacterMasterController:null;
            if(!CanShop(shop) || !purchase.available || !player || !player.isConnected || !body.healthComponent || !body.healthComponent.alive ||
                (body.corePosition-purchase.transform.position).sqrMagnitude>144)return;
            uint id=body.master.netId.Value;int slot=tag.Slot;
            if(slot==Ready)
            {
                if(shop.nextVote.TryGetValue(id,out float next) && Time.unscaledTime<next)return;
                shop.nextVote[id]=Time.unscaledTime+.5f;
                bool ready=shop.plan.Toggle(id);GauntletPortals.Announce(Util.GetBestMasterName(body.master)+(ready?" is ready.":" is shopping."));return;
            }
            int freeTier=PatronageTier(tag);
            if(freeTier>=0 && shop.owner.Meta.Claim(body.master,freeTier))
            {
                var inventory=body.inventory;var definition=PickupCatalog.GetPickupDef(tag.Offer.pickupIndex);
                if(!inventory || inventory.inventoryDisabled || definition==null || inventory.IsItemDisabled(definition.itemIndex)){shop.owner.Meta.Refund(body.master);return;}
                long priorCount=GauntletStartingItems.Count(inventory);
                try {inventory.GiveItemPermanent(definition.itemIndex,1);}
                catch(Exception error)
                {
                    if(GauntletStartingItems.Count(inventory)<=priorCount)shop.owner.Meta.Refund(body.master);
                    GauntletPlugin.Log.LogWarning("Patronage grant callback failed: "+error);return;
                }
                // Direct grant preserves each player's free choice and never touches the paid allowance or global stock.
                shop.plan.Unready(id);shop.SyncPurchases();
                try
                {
                    GenericPickupController.SendPickupMessage(body.master,tag.Offer,1);
                    GauntletPortals.Announce("Patronage granted a free item. Your normal purchase allowance is unchanged.");
                }
                catch(Exception error){GauntletPlugin.Log.LogWarning("Patronage notification failed after a committed grant: "+error);}
                return;
            }
            int limit=shop.PurchaseLimit(slot);
            if(slot!=Scrapper && !shop.plan.CanBuy(id,slot,limit,shop.PurchaseGroup))return;
            if((slot==GreenTrade || slot==RedTrade) && !HasScrap(slot,actor))return;
            if(!purchase.CanBeAffordedByInteractor(actor))return;
            bool before=purchase.available;GauntletShopLoot.PurchaseDepth++;
            try {original(purchase,actor);}
            finally {GauntletShopLoot.PurchaseDepth--;}
            if(before && (!purchase || !purchase.available || tag.GetComponent<ShopTerminalBehavior>()?.NetworkhasBeenPurchased==true))
            {
                shop.plan.Unready(id);shop.plan.Buy(id,slot,limit,shop.PurchaseGroup);
                if(slot!=Scrapper)shop.owner.Meta.Bought(body.master);
                shop.SyncPurchases();
                if(tag && tag.GetComponent<ShopTerminalBehavior>())shop.StartCoroutine(shop.Rearm(tag,shop.generation));
            }
        }
        private IEnumerator Rearm(GauntletShopTag tag,long expected)
        {
            yield return new WaitForSeconds(.8f);
            if(!tag || !opened || expected!=generation)yield break;
            var terminal=tag.GetComponent<ShopTerminalBehavior>();var purchase=tag.GetComponent<PurchaseInteraction>();
            terminal.SetHasBeenPurchased(false);terminal.SetPickup(tag.Offer);purchase.SetAvailable(true);
        }
        private static void OpenPicker(On.RoR2.PickupPickerController.orig_OnInteractionBegin original,PickupPickerController picker,Interactor actor)
        {
            var tag=picker.GetComponent<GauntletShopTag>();if(!tag){original(picker,actor);return;}
            var owner=GauntletController.Current;var phase=NetworkServer.active?owner?.Flow.Phase:owner?.RemotePhase;
            if(phase!=GauntletPhase.AwaitingExit)return;
            if(NetworkServer.active)
            {
                var body=actor?actor.GetComponent<CharacterBody>():null;
                if(!CanShop(Current(tag)) || !body || !body.master || !body.healthComponent || !body.healthComponent.alive)return;
                owner.Shop.plan.Unready(body.master.netId.Value);
            }
            original(picker,actor);
        }
        private static void Choose(On.RoR2.PickupPickerController.orig_HandlePickupSelected original,PickupPickerController picker,int choice)
        {
            var tag=picker.GetComponent<GauntletShopTag>();
            if(tag && !CanShop(Current(tag)))return;
            if(!picker.GetComponent<GauntletPaidLoot>()){original(picker,choice);return;}
            GauntletShopLoot.PurchaseDepth++;
            try {original(picker,choice);}
            finally {GauntletShopLoot.PurchaseDepth--;}
        }
        private void DestroyStock()
        {foreach(var go in stock)if(go)NetworkServer.Destroy(go);stock.Clear();}
        private void OnDestroy(){if(NetworkServer.active)DestroyStock();}
    }
}
