using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal sealed class FortressServiceTag:MonoBehaviour
    { internal FortressController Fortress; internal int Module; internal int Stock=1; internal int Tier; internal bool Market; }
    internal static class FortressServices
    {
        private sealed class Service
        {
            internal string Key; internal int Module;internal GameObject Object;internal bool Suspended;
            internal readonly Dictionary<PurchaseInteraction,bool> Purchases=new Dictionary<PurchaseInteraction,bool>();
            internal readonly Dictionary<PickupPickerController,bool> Pickers=new Dictionary<PickupPickerController,bool>();
        }
        private sealed class Session
        { internal int Restock=-1;internal readonly List<Service> Services=new List<Service>();internal readonly Dictionary<CharacterMaster,int> Purchases=new Dictionary<CharacterMaster,int>(); }
        private static readonly Dictionary<FortressController,Session> sessions=new Dictionary<FortressController,Session>();
        internal static void Install()
        {
            On.RoR2.PurchaseInteraction.OnInteractionBegin+=Buy;
            On.RoR2.PurchaseInteraction.GetInteractability+=Interactability;
            On.RoR2.PickupPickerController.OnInteractionBegin+=OpenPicker;
            On.RoR2.PickupPickerController.HandlePickupSelected+=Choose;
        }
        private static T Setting<T>(string pluginName,string section,string key,T fallback)
        {
            var plugin=Chainloader.PluginInfos.Values.FirstOrDefault(info=>info.Metadata.Name==pluginName);
            if(plugin?.Instance is BepInEx.BaseUnityPlugin instance && instance.Config.TryGetEntry(new ConfigDefinition(section,key),out ConfigEntry<T> entry)) return entry.Value;
            return fallback;
        }
        internal static bool Supports(FortressUpgrade id,Run run)
        {
            if(id==FortressUpgrade.Hangar || id==FortressUpgrade.DroneUpgrade || id==FortressUpgrade.DroneCombiner || id==FortressUpgrade.DroneScrapper || id==FortressUpgrade.Kitchen)
                return DLC3Content.DLC3 && run.IsExpansionEnabled(DLC3Content.DLC3);
            return true;
        }
        internal static void Refresh(FortressController fortress)
        {
            if(!NetworkServer.active || !fortress.InCastle) return;
            if(!sessions.TryGetValue(fortress,out var session)) sessions[fortress]=session=new Session();
            if(session.Restock!=fortress.State.Restock)
            {
                foreach(var service in session.Services) if(service.Object) NetworkServer.Destroy(service.Object);
                session.Services.Clear();session.Purchases.Clear();session.Restock=fortress.State.Restock;
            }
            var state=fortress.State;
            int market=state.Rank(FortressUpgrade.Market);
            int count=market==0?0:market==1?3:market==2?6:8;
            for(int i=0;i<count;i++) Market(fortress,session,i,false);
            if(state.Rank(FortressUpgrade.Sanctum)>0) for(int i=0;i<3;i++) Market(fortress,session,i,true);
            Spawn(fortress,session,"scrapper",FortressUpgrade.Workshop,"iscScrapper_asset",new Vector3(-3,0,5));
            Spawn(fortress,session,"white-printer",FortressUpgrade.WhitePrinter,"iscDuplicator_asset",new Vector3(2,0,5));
            Spawn(fortress,session,"green-printer",FortressUpgrade.GreenPrinter,"iscDuplicatorLarge_asset",new Vector3(6,0,4));
            Spawn(fortress,session,"red-printer",FortressUpgrade.RedPrinter,"iscDuplicatorMilitary_asset",new Vector3(-6,0,4));
            Spawn(fortress,session,"cauldron-green",FortressUpgrade.Forge,"LunarCauldron__WhiteToGreen_prefab",new Vector3(-3,0,5));
            Spawn(fortress,session,"cauldron-red",FortressUpgrade.Forge,"LunarCauldron__GreenToRed_Variant_prefab",new Vector3(3,0,5));
            var reforge=Spawn(fortress,session,"reforge",FortressUpgrade.Reforge,"LunarCauldron__RedToWhite_Variant_prefab",new Vector3(0,0,9));
            if(reforge && reforge.GetComponent<PurchaseInteraction>())
            {
                var purchase=reforge.GetComponent<PurchaseInteraction>(); purchase.NetworkcostType=CostTypeIndex.GreenItem;
                purchase.Networkcost=Math.Max(1,Setting("BazaarExpand","Lunar Cauldron","Items Amount Needed",2));
            }
            Spawn(fortress,session,"cleansing",FortressUpgrade.Cleansing,"iscShrineCleanse_asset",new Vector3(-5,0,5));
            Spawn(fortress,session,"order",FortressUpgrade.Order,"iscShrineRestack_asset",new Vector3(5,0,5));
            Spawn(fortress,session,"drone-shop",FortressUpgrade.Hangar,"iscTripleDroneShop_asset",new Vector3(0,0,5));
            Spawn(fortress,session,"drone-upgrade",FortressUpgrade.DroneUpgrade,"iscDroneAssemblyStation_asset",new Vector3(-7,0,4));
            Spawn(fortress,session,"drone-combine",FortressUpgrade.DroneCombiner,"iscDroneCombinerStation_asset",new Vector3(7,0,4));
            Spawn(fortress,session,"drone-scrap",FortressUpgrade.DroneScrapper,"iscDroneScrapper_asset",new Vector3(0,0,10));
            Spawn(fortress,session,"chef",FortressUpgrade.Kitchen,"MealPrep_prefab",new Vector3(0,0,5));
            Spawn(fortress,session,"legendary-vault",FortressUpgrade.GoldenVault,"iscGoldChest_asset",new Vector3(0,0,15));
            if(market>=3) SpawnDirect(fortress,session,"equipment",9,"iscEquipmentBarrel_asset",new Vector3(0,0,11));
            SetAvailable(fortress,fortress.CanManage);
        }
        private static GameObject Spawn(FortressController fortress,Session session,string key,FortressUpgrade upgrade,string asset,Vector3 offset)
        {
            if(fortress.State.Rank(upgrade)<1 || !Supports(upgrade,fortress.Owner.Run)) return null;
            return SpawnDirect(fortress,session,key,FortressTree.Node(upgrade).Module,asset,offset);
        }
        private static GameObject SpawnDirect(FortressController fortress,Session session,string key,int module,string asset,Vector3 offset)
        {
            var previous=session.Services.FirstOrDefault(service=>service.Key==key); if(previous!=null) return previous.Object;
            if(fortress.State.Modules[module].Destroyed) return null;
            Vector3 position=Ground(fortress.Position(module)+offset);
            GameObject instance=null;
            if(asset.EndsWith("_asset",StringComparison.Ordinal))
            {
                var card=FortressAssets.Load<SpawnCard>(asset);
                if(card) instance=card.DoSpawn(position,Quaternion.identity,new DirectorSpawnRequest(card,new DirectorPlacementRule{placementMode=DirectorPlacementRule.PlacementMode.Direct,position=position},fortress.Owner.ArenaRng)).spawnedInstance;
            }
            else
            {
                var prefab=FortressAssets.Load<GameObject>(asset);
                if(prefab) { instance=UnityEngine.Object.Instantiate(prefab,position,Quaternion.identity); NetworkServer.Spawn(instance); }
            }
            if(!instance) { GauntletPlugin.Log.LogWarning("Castle service asset unavailable: "+asset); return null; }
            var tag=instance.AddComponent<FortressServiceTag>();tag.Fortress=fortress;tag.Module=module;
            foreach(var purchase in instance.GetComponentsInChildren<PurchaseInteraction>(true))
            {
                if(purchase.costType==CostTypeIndex.LunarCoin) { purchase.NetworkcostType=CostTypeIndex.Money;purchase.Networkcost=fortress.Price(Mathf.Max(25,purchase.cost*25)); }
                else if(purchase.costType==CostTypeIndex.Money) purchase.Networkcost=fortress.Price(purchase.cost);
            }
            session.Services.Add(new Service{Key=key,Module=module,Object=instance}); return instance;
        }
        private static Vector3 Ground(Vector3 point)
        { return Physics.Raycast(point+Vector3.up*30,Vector3.down,out var hit,80,LayerIndex.world.mask)?hit.point:point; }
        private static void Market(FortressController fortress,Session session,int slot,bool lunar)
        {
            string key=(lunar?"lunar-":"market-")+slot;
            if(session.Services.Any(service=>service.Key==key)) return;
            int module=lunar?12:9;if(fortress.State.Modules[module].Destroyed)return;
            var prefab=FortressAssets.Load<GameObject>("LunarShopTerminal_prefab");if(!prefab)return;
            float white=Setting("BiggerBazaar","2. TierRarity","tier1Rarity",.55f),green=Setting("BiggerBazaar","2. TierRarity","tier2Rarity",.3f),red=Setting("BiggerBazaar","2. TierRarity","tier3Rarity",.15f);
            float roll=fortress.Owner.ArenaRng.nextNormalizedFloat*Mathf.Max(.001f,white+green+red);
            int tier=lunar?3:roll<white?0:roll<white+green?1:2;
            var list=tier==3?fortress.Owner.Run.availableLunarItemDropList:tier==2?fortress.Owner.Run.availableTier3DropList:tier==1?fortress.Owner.Run.availableTier2DropList:fortress.Owner.Run.availableTier1DropList;
            if(list.Count==0)return;
            var pickup=new UniquePickup(list[fortress.Owner.ArenaRng.RangeInt(0,list.Count)]);
            var position=Ground(fortress.Position(module)+new Vector3((slot%4-1.5f)*3,0,5+slot/4*4));
            var instance=UnityEngine.Object.Instantiate(prefab,position,Quaternion.identity);
            var terminal=instance.GetComponent<ShopTerminalBehavior>(); var purchase=instance.GetComponent<PurchaseInteraction>();
            if(!terminal || !purchase) { UnityEngine.Object.Destroy(instance);return; }
            terminal.selfGeneratePickup=false;terminal.SetPickup(pickup);purchase.NetworkcostType=CostTypeIndex.Money;
            purchase.Networkcost=fortress.Price(tier==0?25:tier==1?50:tier==2?400:150);purchase.SetAvailable(true);
            var tag=instance.AddComponent<FortressServiceTag>(); tag.Fortress=fortress;tag.Module=module;tag.Market=true;tag.Tier=tier;
            tag.Stock=tier==0?3:tier==1?2:1;
            NetworkServer.Spawn(instance);session.Services.Add(new Service{Key=key,Module=module,Object=instance});
        }
        internal static void SetAvailable(FortressController fortress,bool available)
        {
            if(!sessions.TryGetValue(fortress,out var session))return;
            foreach(var service in session.Services)
            {
                if(!service.Object)continue;
                bool allowed=available && !fortress.State.Modules[service.Module].Destroyed;
                if(allowed)
                {
                    if(!service.Suspended)continue;
                    foreach(var saved in service.Purchases)if(saved.Key)saved.Key.SetAvailable(saved.Value);
                    foreach(var saved in service.Pickers)if(saved.Key)saved.Key.SetAvailable(saved.Value);
                    service.Purchases.Clear();service.Pickers.Clear();service.Suspended=false;
                }
                else Suspend(service);
            }
        }
        private static void Suspend(Service service)
        {
            foreach(var purchase in service.Object.GetComponentsInChildren<PurchaseInteraction>(true))
            {if(!service.Suspended)service.Purchases[purchase]=purchase.available;purchase.SetAvailable(false);}
            foreach(var picker in service.Object.GetComponentsInChildren<PickupPickerController>(true))
            {if(!service.Suspended)service.Pickers[picker]=picker.available;picker.SetAvailable(false);}
            service.Suspended=true;
        }
        internal static void DisableModule(FortressController fortress,int module)
        { if(sessions.TryGetValue(fortress,out var session)) foreach(var service in session.Services.Where(service=>service.Module==module)) if(service.Object)Suspend(service); }
        internal static void RerollMarket(FortressController fortress)
        {
            if(sessions.TryGetValue(fortress,out var session))
                foreach(var service in session.Services.Where(service=>service.Key.StartsWith("market-",StringComparison.Ordinal)).ToArray())
                { if(service.Object)NetworkServer.Destroy(service.Object);session.Services.Remove(service); }
            Refresh(fortress);
        }
        internal static void Clear(FortressController fortress)
        { if(!sessions.TryGetValue(fortress,out var session))return;foreach(var service in session.Services)if(service.Object)NetworkServer.Destroy(service.Object);sessions.Remove(fortress); }
        private static Interactability Interactability(On.RoR2.PurchaseInteraction.orig_GetInteractability original,PurchaseInteraction purchase,Interactor activator)
        { var tag=purchase.GetComponentInParent<FortressServiceTag>();return tag && (!tag.Fortress.CanManage || tag.Fortress.State.Modules[tag.Module].Destroyed)?RoR2.Interactability.Disabled:original(purchase,activator); }
        private static bool Blocked(Component component)
        {var tag=component.GetComponentInParent<FortressServiceTag>();return tag && (!tag.Fortress || !tag.Fortress.CanManage || tag.Fortress.State.Modules[tag.Module].Destroyed);}
        private static void OpenPicker(On.RoR2.PickupPickerController.orig_OnInteractionBegin original,PickupPickerController picker,Interactor activator)
        {if(!NetworkServer.active || !Blocked(picker))original(picker,activator);}
        private static void Choose(On.RoR2.PickupPickerController.orig_HandlePickupSelected original,PickupPickerController picker,int choice)
        {if(!Blocked(picker))original(picker,choice);}
        private static void Buy(On.RoR2.PurchaseInteraction.orig_OnInteractionBegin original,PurchaseInteraction purchase,Interactor activator)
        {
            var tag=purchase.GetComponentInParent<FortressServiceTag>();
            if(!tag || !NetworkServer.active){original(purchase,activator);return;}
            if(!tag.Fortress.CanManage || tag.Fortress.State.Modules[tag.Module].Destroyed)return;
            var actor=activator?activator.GetComponent<CharacterBody>()?.master:null;
            if(!actor || !sessions.TryGetValue(tag.Fortress,out var session))return;
            int count=session.Purchases.TryGetValue(actor,out int previous)?previous:0;
            if(tag.Market && (tag.Stock<=0 || count>=Math.Max(1,Setting("BiggerBazaar","4. PlayerPurchaseLimits","maxPlayerPurchases",3))))return;
            bool before=purchase.available;original(purchase,activator);
            var record=session.Services.FirstOrDefault(service=>service.Object==tag.gameObject);
            if(before && !purchase.available)
            {
                if(tag.Market)
                {
                    tag.Stock--;session.Purchases[actor]=count+1;
                    if(tag.Stock>0)tag.Fortress.StartCoroutine(Rearm(tag,purchase,record));
                }
            }
        }
        private static IEnumerator Rearm(FortressServiceTag tag,PurchaseInteraction purchase,Service service)
        {
            yield return new WaitForSeconds(.7f);
            if(!tag || !purchase || !tag.Fortress || service==null)yield break;
            var terminal=tag.GetComponent<ShopTerminalBehavior>();if(terminal)terminal.SetHasBeenPurchased(false);
            if(service.Suspended)service.Purchases[purchase]=true;
            purchase.SetAvailable(tag.Fortress.CanManage && !tag.Fortress.State.Modules[tag.Module].Destroyed);
        }
    }
}
