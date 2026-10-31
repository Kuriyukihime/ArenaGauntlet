using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal sealed class GauntletMetaprogression : MonoBehaviour
    {
        internal sealed class Participant
        {
            internal GauntletCareer Career;
            internal bool OpeningCompany,Recovered,EquipmentGiven;
            internal int PatronageRound=-1,PatronageCycle=-1,Glory,Clear=-1;
            // Labors earned this run, with the run counters behind them. Fell resets at each round clear.
            internal readonly HashSet<string> Labors=new HashSet<string>();
            internal bool Fell;
            internal int Kills,Purchases,CleanRounds;
        }
        private readonly Dictionary<NetworkUserId,Participant> players=new Dictionary<NetworkUserId,Participant>();
        private readonly Dictionary<NetworkUserId,float> waiting=new Dictionary<NetworkUserId,float>();
        internal readonly Dictionary<uint,GauntletMetaStatus> Remote=new Dictionary<uint,GauntletMetaStatus>();
        internal int Ascension;
        internal string ReceiptId;
        // The host's Treasury choices, shown to everyone. The battlefield watches these for changes.
        internal string Skin="";
        internal string[] Decor=Array.Empty<string>();
        private GauntletController owner;
        private int completedRound=-1,completedCycle=-1;
        private float nextStatus;
        private void Awake(){owner=GetComponent<GauntletController>();}
        internal static void Install(){GauntletWaveBudget.QuotaDefeated+=Defeated;GlobalEventManager.onCharacterDeathGlobal+=Died;}
        private static void Defeated(GauntletController arena,CharacterMaster victim,DamageReport report,bool boss)
        {
            if(!NetworkServer.active || !arena || !arena.Meta)return;
            var killer=GauntletCompanies.PlayerOf(report?.attackerMaster);var p=arena.Meta.For(killer);if(p==null)return;
            if(++p.Kills>=GauntletMetaPolicy.MyriadKills)arena.Meta.Earn(killer,p,"myriad");
            if(boss && report.attackerMaster==killer)arena.Meta.Earn(killer,p,"giantslayer");
        }
        private static void Died(DamageReport report)
        {
            var arena=GauntletController.Current;
            if(NetworkServer.active && arena && arena.Meta && report!=null){var p=arena.Meta.For(report.victimMaster);if(p!=null)p.Fell=true;}
        }
        internal void Bought(CharacterMaster master){var p=For(master);if(p!=null && ++p.Purchases>=GauntletMetaPolicy.AgoraPurchases)Earn(master,p,"agora");}
        private void Earn(CharacterMaster master,Participant p,string id)
        {
            if(!master || !p.Labors.Add(id))return;
            var labor=GauntletMetaPolicy.Labors.First(l=>l.Id==id);
            // The career frozen at run start says whether this is the player's first time.
            if(!p.Career.Labors.Contains(id))GauntletPortals.Announce($"{Util.GetBestMasterName(master)} accomplished the Feat <color=#e8c06b>{labor.Name}</color>! +{labor.Glory} Glory");
            var user=master.playerCharacterMasterController?.networkUser;if(user)Send(user,p);
        }
        internal void Show(string skin,string[] decor){Skin=skin;if(!decor.SequenceEqual(Decor))Decor=decor;}
        internal void Initialize()
        {
            Ascension=GauntletMetaNetwork.LobbyAscension;ReceiptId=Guid.NewGuid().ToString("N");
            var host=GauntletMetaStore.For(LocalUserManager.GetFirstLocalUser());
            if(host!=null)Show(host.Skin,host.Decor.ToArray());
            foreach(var user in NetworkUser.readOnlyInstancesList)
                if(user && user.isParticipating && GauntletMetaNetwork.Lobby.TryGetValue(user.id,out var career))Capture(user,career);
        }
        internal Participant For(NetworkUser user)=>user && players.TryGetValue(user.id,out var p)?p:null;
        internal Participant For(CharacterMaster master)=>master?For(master.playerCharacterMasterController?.networkUser):null;
        internal bool Capture(NetworkUser user,GauntletCareer career)
        {
            if(!NetworkServer.active || !user || !user.isParticipating || !GauntletMetaPolicy.Valid(career))return false;
            if(players.ContainsKey(user.id))return true;
            // Freeze the accepted career for this entire run. Reconnects cannot change it.
            players[user.id]=new Participant{Career=JsonUtility.FromJson<GauntletCareer>(JsonUtility.ToJson(career))};return true;
        }
        internal bool Ready(NetworkUser user)
        {
            if(For(user)!=null)return true;
            if(!user)return false;
            float since=waiting.TryGetValue(user.id,out float start)?start:float.NaN;
            bool loading=user.connectionToClient!=null && !user.connectionToClient.isReady;
            bool overdue=GauntletNetworkPolicy.CareerOverdue(ref since,loading,Time.unscaledTime);
            waiting[user.id]=since;
            if(!overdue)return false;
            Capture(user,new GauntletCareer());return true;
        }
        internal int Capacity(CharacterMaster master)=>GauntletMetaPolicy.Capacity(owner.Run.participatingPlayerCount,owner.Companies.ConfiguredCapacity,For(master)?.Career.Barracks??0);
        internal bool Free(CharacterMaster master,int tier)
        {
            var p=For(master);return p!=null && tier>=0 && tier<(p.Career.Patronage>=2?2:p.Career.Patronage>=1?1:0) &&
                (p.PatronageRound!=owner.Flow.Round || p.PatronageCycle!=owner.Flow.Cycle);
        }
        internal bool Claim(CharacterMaster master,int tier)
        {
            if(!NetworkServer.active || !Free(master,tier))return false;
            var p=For(master);p.PatronageRound=owner.Flow.Round;p.PatronageCycle=owner.Flow.Cycle;return true;
        }
        internal void Refund(CharacterMaster master){var p=For(master);if(p!=null){p.PatronageRound=-1;p.PatronageCycle=-1;}}
        internal void RoundCleared()
        {
            if(!NetworkServer.active || completedRound==owner.Flow.Round && completedCycle==owner.Flow.Cycle)return;
            completedRound=owner.Flow.Round;completedCycle=owner.Flow.Cycle;
            foreach(var user in NetworkUser.readOnlyInstancesList)
            {
                var p=For(user);if(p==null || !user.isParticipating)continue;
                p.Glory=Math.Min(100000,p.Glory+GauntletMetaPolicy.RoundGlory(owner.Flow.Round,Ascension));
                if(owner.Flow.Round==10)p.Clear=Ascension;
                var master=user.master;
                Earn(master,p,"initiate");
                if(GauntletPlan.BossAt(owner.Flow.Round)==GauntletBossKind.Great)Earn(master,p,"halfway");
                if(owner.Flow.Round==10){Earn(master,p,"victor");if(Ascension>=GauntletMetaPolicy.FavoredAscension)Earn(master,p,"favored");}
                if(!p.Fell){if(owner.Flow.Round>=GauntletMetaPolicy.UnbrokenRound)Earn(master,p,"unbroken");if(++p.CleanRounds>=GauntletMetaPolicy.IronWillRounds)Earn(master,p,"ironwill");}
                p.Fell=false;
                if(master && master.inventory && GauntletStartingItems.Count(master.inventory)>=GauntletMetaPolicy.SpoilsItems)Earn(master,p,"spoils");
                Send(user,p);
            }
            GauntletPortals.Announce($"+{GauntletMetaPolicy.RoundGlory(owner.Flow.Round,Ascension)} Glory banked for your arena career!");
        }
        private void FixedUpdate()
        {
            if(!NetworkServer.active || !owner || owner.ShuttingDown || owner.Flow.Phase==GauntletPhase.Idle)return;
            foreach(var player in PlayerCharacterMasterController.instances)
            {
                if(!player || !player.isConnected || !player.networkUser || !player.master || !Ready(player.networkUser))continue;
                var p=For(player.networkUser);
                if(p.Career.Barracks>=1 && !p.OpeningCompany && owner.ArenaReady && GauntletCompanies.Available(p.Career.Company,owner.Run))
                {
                    p.OpeningCompany=true;
                    owner.Companies.Council.Grant(player.master.netId.Value,p.Career.Company,Capacity(player.master));
                }
            }
            if(Time.unscaledTime<nextStatus)return;nextStatus=Time.unscaledTime+1;
            foreach(var user in NetworkUser.readOnlyInstancesList)
            {
                var p=For(user);if(p==null)continue;
                if(user.master)
                {
                    // Read accounts without Council.Get, which would create one.
                    int active=owner.Companies.Council.Accounts.Where(a=>a.Key==user.master.netId.Value).Select(a=>a.Value.Active).FirstOrDefault();
                    if(active>=1)Earn(user.master,p,"strategos");
                    if(active>=2)Earn(user.master,p,"warhost");
                }
                Send(user,p);
            }
        }
        private void Send(NetworkUser user,Participant p)
        {
            if(!user || !user.master)return;
            var state=new GauntletMetaStatus{Player=user.master.netId.Value,User=user.netId.Value,Ascension=Ascension,Capacity=Capacity(user.master),
                Patronage=Free(user.master,0)?p.Career.Patronage:0,Glory=p.Glory,Clear=p.Clear,Receipt=ReceiptId,Skin=Skin,Decor=Decor,Labors=p.Labors.ToArray()};
            Remote[state.Player]=state;GauntletMetaNetwork.Status(user,state,owner.Run.netId,owner.RunKey);
        }
        internal bool Recover(CharacterMaster master,int commander)
        {
            var p=For(master);if(p==null || p.Career.Barracks<3 || p.Recovered)return false;
            p.Recovered=true;
            if(owner.Companies.Council.Recover(master.netId.Value,commander))
            {GauntletPortals.Announce("Barracks reserves restored a fallen commander. Recovery used for this run.");return true;}
            p.Recovered=false;return false;
        }
        internal static int Category(PickupIndex pickup)
        {
            var def=PickupCatalog.GetPickupDef(pickup);if(def==null)return -1;
            var item=ItemCatalog.GetItemDef(def.itemIndex);
            if(item)return item.hidden || item.ContainsTag(ItemTag.Scrap) || item.ContainsTag(ItemTag.PriorityScrap)?-1:
                item.tier==ItemTier.Tier1?0:item.tier==ItemTier.Tier2?1:item.tier==ItemTier.Tier3?2:item.tier==ItemTier.Lunar?3:-1;
            var equipment=EquipmentCatalog.GetEquipmentDef(def.equipmentIndex);
            return equipment && equipment.canDrop && !equipment.isBoss && !equipment.passiveBuffDef?equipment.isLunar?5:4:-1;
        }
        internal static string Name(PickupIndex pickup)
        {
            var def=PickupCatalog.GetPickupDef(pickup);var item=def==null?null:ItemCatalog.GetItemDef(def.itemIndex);
            return item?item.name:def==null?"":EquipmentCatalog.GetEquipmentDef(def.equipmentIndex)?.name??"";
        }
        internal static PickupIndex Resolve(string name,bool equipment=false)
        {
            if(string.IsNullOrEmpty(name))return PickupIndex.none;
            return equipment?PickupCatalog.FindPickupIndex(EquipmentCatalog.FindEquipmentIndex(name)):PickupCatalog.FindPickupIndex(ItemCatalog.FindItemIndex(name));
        }
        internal static IEnumerable<PickupIndex> Pool(Run run,int category)
        {
            if(!run)return Array.Empty<PickupIndex>();
            return category==0?run.availableTier1DropList:category==1?run.availableTier2DropList:category==2?run.availableTier3DropList:
                category==3?run.availableLunarItemDropList:category==4?run.availableEquipmentDropList:run.availableLunarEquipmentDropList;
        }
        // Validate against this run's enabled expansion and modded drop pools, not the client's catalog indices.
        internal PickupIndex[] Loadout(NetworkUser user)
        {
            var p=For(user);var c=p?.Career;if(c==null)return Array.Empty<PickupIndex>();
            var result=new List<PickupIndex>();int spent=0,reds=0;
            for(int slot=0;slot<GauntletMetaPolicy.Slots(c.Armory);slot++)
            {
                var pickup=Resolve(c.Items[slot]);int cat=Category(pickup);int cost=cat<0?0:GauntletMetaPolicy.Cost(cat);
                bool ok=cat>=0 && cat<=3 && GauntletMetaPolicy.Allowed(c.Armory,cat) && spent+cost<=GauntletMetaPolicy.Budget(c.Armory) &&
                    (cat!=2 || reds<2) && Pool(owner.Run,cat).Contains(pickup);
                result.Add(ok?pickup:PickupIndex.none);if(ok){spent+=cost;if(cat==2)reds++;}
            }
            var eq=Resolve(c.Equipment,true);int category=Category(eq);
            bool valid=category>=4 && GauntletMetaPolicy.Allowed(c.Armory,category) && spent+GauntletMetaPolicy.Cost(category)<=GauntletMetaPolicy.Budget(c.Armory) && Pool(owner.Run,category).Contains(eq);
            result.Add(valid?eq:PickupIndex.none);return result.ToArray();
        }
    }
    [Serializable]
    internal sealed class GauntletMetaStatus
    {
        public uint Player,User;
        public int Ascension,Capacity,Patronage,Glory,Clear=-1;
        public string Receipt,Skin="";
        public string[] Decor=Array.Empty<string>(),Labors=Array.Empty<string>();
    }
}
