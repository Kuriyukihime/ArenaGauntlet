using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.CharacterAI;
using RoR2.Networking;
using RoR2.Orbs;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    // War Council. The server owns renown, hires and every allied unit; native
    // enemy masters keep their own AI and skills while fighting for the player
    // team. Clients only mirror snapshots and send verified hire requests.
    internal sealed class GauntletCompanies : MonoBehaviour
    {
        internal const short RequestMessage=18467;
        internal sealed class CompanyView { internal int Commander; internal bool Fallen; internal NetworkInstanceId[] Units=Array.Empty<NetworkInstanceId>(); }
        internal sealed class View { internal ulong Player; internal long Renown; internal int Capacity; internal readonly List<CompanyView> Companies=new List<CompanyView>(); }
        private sealed class Deployment
        {
            internal ulong Player;internal int Commander;internal CharacterMaster Leader;internal bool LeaderSpawned;
            internal readonly List<CharacterMaster> Squad=new List<CharacterMaster>();
            internal float NextAbility,NextAttempt;internal int ReinforcedRound=-1,ReinforcedCycle=-1;
        }
        internal readonly GauntletWarCouncil Council=new GauntletWarCouncil();
        internal readonly Dictionary<ulong,View> Views=new Dictionary<ulong,View>();
        internal int Capacity=3,ConfiguredCapacity=3;
        internal ulong ReceivedSequence;
        private readonly List<Deployment> deployments=new List<Deployment>();
        private readonly Dictionary<NetworkConnection,GauntletRequestBudget> requestBudgets=new Dictionary<NetworkConnection,GauntletRequestBudget>();
        private GauntletController owner;
        private Xoroshiro128Plus rng;
        private ulong sentRevision=ulong.MaxValue,sequence,unitRevision,sentUnits=ulong.MaxValue;
        private float nextSnapshot,nextInsignia,nextChangeSnapshot;
        private int damageSlots;
        private static readonly Dictionary<int,bool> availability=new Dictionary<int,bool>();
        private static Run availabilityRun;
        internal static void Install()
        {
            NetworkManagerSystem.onStartServerGlobal+=RegisterServer;
            GauntletWaveBudget.QuotaDefeated+=Defeated;
        }
        private static void RegisterServer()
        {
            if(NetworkServer.handlers.TryGetValue(RequestMessage,out var existing))
            {if(existing==(NetworkMessageDelegate)HandleRequest)return;throw new InvalidOperationException("ArenaGauntlet request message ID is already registered.");}
            NetworkServer.RegisterHandler(RequestMessage,HandleRequest);
        }
        private void Awake(){owner=GetComponent<GauntletController>();}
        internal static bool Available(int commander,Run run)
        {
            if(commander<0 || commander>=GauntletCommandRoster.Commanders.Length || !run)return false;
            if(availabilityRun!=run){availability.Clear();availabilityRun=run;}
            if(availability.TryGetValue(commander,out bool known))return known;
            var def=GauntletCommandRoster.Commanders[commander];bool usable=true;
            foreach(string field in new[]{def.LeaderCard,def.SquadCard})
            {
                var card=FortressAssets.Load<CharacterSpawnCard>(field);
                var master=card && card.prefab?card.prefab.GetComponent<CharacterMaster>():null;
                usable&=master && master.bodyPrefab && GauntletCatalogue.Available(card.prefab,run) && GauntletCatalogue.Available(master.bodyPrefab,run);
            }
            return availability[commander]=usable;
        }
        // Local player's mirrored account; the host reads its own snapshot.
        internal View For(CharacterMaster master)=>master && Views.TryGetValue(master.netId.Value,out var view)?view:null;
        internal static void Request(LocalUser user,int commander,bool dismiss)
        {
            var owner=GauntletController.Current;var network=user?.currentNetworkUser;
            if(!owner || !network || commander<0 || commander>=GauntletCommandRoster.Commanders.Length)return;
            if(owner.Run.netId==NetworkInstanceId.Invalid || owner.Run.NetworkinSceneTransition)return;
            if(NetworkServer.active){owner.Companies.Handle(network,owner.Run.netId,owner.RunKey,dismiss,commander);return;}
            ClientScene.readyConnection?.Send(RequestMessage,new GauntletCommandRequest{RunId=owner.Run.netId,User=network.netId,Seed=owner.RunKey,Dismiss=dismiss,Commander=(byte)commander});
        }
        private static void HandleRequest(NetworkMessage message)
        {
            var owner=GauntletController.Current;
            if(!NetworkServer.active || message?.conn==null || !message.conn.isReady || message.reader==null ||
                message.reader.Length>GauntletNetworkPolicy.MaxRequestBytes || !owner || !owner.Companies || owner.ShuttingDown)return;
            var budgets=owner.Companies.requestBudgets;
            if(!budgets.TryGetValue(message.conn,out var budget))budgets[message.conn]=budget=new GauntletRequestBudget();
            if(!budget.Take(Time.unscaledTime))return;
            GauntletCommandRequest request;
            try{request=message.ReadMessage<GauntletCommandRequest>();}
            catch(Exception error) when(error is IOException || error is IndexOutOfRangeException || error is ArgumentException || error is OverflowException || error is FormatException)
            {return;}
            if(!GauntletNetworkPolicy.MatchesRun(owner.Run.netId.Value,owner.RunKey,request.RunId.Value,request.Seed))return;
            // The sender's own connection must own the named network user.
            var user=NetworkUser.readOnlyInstancesList.FirstOrDefault(value=>value && value.netId==request.User && value.connectionToClient==message.conn);
            if(!user)return;
            owner.Companies.Handle(user,request.RunId,request.Seed,request.Dismiss,request.Commander);
        }
        internal void Handle(NetworkUser user,NetworkInstanceId runId,ulong seed,bool dismiss,int commander)
        {
            // Remote packets carry a byte, which can exceed the ten-entry roster.
            // Reject it before either ledger processing or the result's roster lookup.
            if(commander<0 || commander>=GauntletCommandRoster.Commanders.Length)return;
            if(!NetworkServer.active || !owner || owner.ShuttingDown || owner.Run.isGameOverServer || owner.Run.NetworkinSceneTransition ||
                !GauntletNetworkPolicy.MatchesRun(owner.Run.netId.Value,owner.RunKey,runId.Value,seed))return;
            RefreshCapacity();
            var master=user?user.master:null;if(!master)return;
            ulong key=master.netId.Value;double now=Time.unscaledTime;
            var result=dismiss?Council.Dismiss(key,commander,now):
                Council.Hire(key,commander,owner.Flow.Round,owner.Flow.Cycle,owner.Meta.Capacity(master),Available(commander,owner.Run),now);
            var def=GauntletCommandRoster.Commanders[commander];
            if(result==CommandResult.Hired)GauntletPortals.Announce($"<color=#e8c06b>{user.userName} raised the {def.Name}.</color>");
            else if(result==CommandResult.Dismissed){Withdraw(key,commander);GauntletPortals.Announce($"<color=#9fb3bf>{user.userName} discharged the {def.Name}.</color>");}
            else GauntletPlugin.Log.LogInfo($"War Council request from {user.userName} for {def.Name}: {result}.");
            Broadcast();
        }
        internal static CharacterMaster PlayerOf(CharacterMaster attacker)
        {
            for(int depth=0;attacker && depth<4;depth++)
            {
                if(attacker.playerCharacterMasterController)return attacker;
                attacker=attacker.minionOwnership?attacker.minionOwnership.ownerMaster:null;
            }
            return null;
        }
        private static void Defeated(GauntletController owner,CharacterMaster victim,DamageReport report,bool boss)
        {
            var companies=owner?owner.Companies:null;if(!companies || !NetworkServer.active)return;
            if(boss)
            {
                // Champions are a party effort: every connected player is paid.
                long amount=GauntletCommandRoster.BossRenown(owner.Flow.Round,owner.Flow.Cycle,owner.Flow.BossKind,owner.RenownMultiplier);
                foreach(var player in PlayerCharacterMasterController.instances)
                    if(player && player.isConnected && player.master)companies.Council.Award(player.master.netId.Value,amount);
                return;
            }
            var body=report!=null?report.victimBody:null;var killer=PlayerOf(report!=null?report.attackerMaster:null);
            if(body && killer)
            {
                long amount=GauntletCommandRoster.Renown(body.baseMaxHealth,body.isElite,owner.RenownMultiplier);
                companies.Council.Award(killer.netId.Value,owner.ActiveBudget?owner.ActiveBudget.Renown(killer.netId.Value,amount):amount);
            }
        }
        private bool Field=>owner.ArenaReady && owner.Fortress.InCastle && !owner.Fortress.Leaving && !owner.ShuttingDown && !owner.Run.isGameOverServer && DirectorCore.instance;
        private void FixedUpdate()
        {
            if(!NetworkServer.active || !owner || owner.Flow.Phase==GauntletPhase.Idle)return;
            RefreshCapacity();
            foreach(var connection in requestBudgets.Keys.Where(connection=>!NetworkServer.connections.Contains(connection)).ToArray())requestBudgets.Remove(connection);
            if(owner.ShuttingDown){if(deployments.Count>0)WithdrawAll();return;}
            // Companies march with the party between arenas instead of idling in
            // the Bazaar. Withdrawal is not a defeat; they redeploy on arrival.
            if(!Field){if(deployments.Count>0)WithdrawAll();}
            else Reconcile();
            if((Council.Revision!=sentRevision || unitRevision!=sentUnits) && Time.unscaledTime>=nextChangeSnapshot || Time.unscaledTime>=nextSnapshot)Broadcast();
        }
        private void RefreshCapacity()
        {
            int natural=GauntletNetworkPolicy.CompanyCapacity(owner.Run.participatingPlayerCount,3);
            if(natural!=damageSlots){damageSlots=natural;GauntletCompanyDamage.RefreshAll(natural);}
            int capacity=GauntletNetworkPolicy.CompanyCapacity(owner.Run.participatingPlayerCount,ConfiguredCapacity);
            if(capacity!=Capacity){Capacity=capacity;unitRevision++;}
            var removed=Council.EnforceCapacity(key=>owner.Meta.Capacity(OwnerMaster(key)));
            foreach(var company in removed)Withdraw(company.Player,company.Commander);
            if(removed.Count>0)GauntletPortals.Announce("Party size changed: excess companies were withdrawn and their renown refunded.");
        }
        private void Reconcile()
        {
            rng??=new Xoroshiro128Plus(owner.Run.seed^0x5741524C4F5244UL);
            foreach(var pair in Council.Accounts)
                foreach(var company in pair.Value.Companies)
                    if(!company.Fallen && OwnerMaster(pair.Key) && !deployments.Any(value=>value.Player==pair.Key && value.Commander==company.Commander))
                        deployments.Add(new Deployment{Player=pair.Key,Commander=company.Commander});
            bool combat=owner.Fortress.IsCombat;
            foreach(var deployment in deployments.ToArray())
            {
                if(!OwnerMaster(deployment.Player)){Disband(deployment);deployments.Remove(deployment);continue;}
                var def=GauntletCommandRoster.Commanders[deployment.Commander];
                var company=Council.Get(deployment.Player).Find(deployment.Commander);
                if(company==null){Disband(deployment);deployments.Remove(deployment);continue;}
                int before=deployment.Squad.Count;
                deployment.Squad.RemoveAll(member=>!member || member.IsDeadAndOutOfLivesServer());
                if(before!=deployment.Squad.Count)unitRevision++;
                if(deployment.LeaderSpawned && (!deployment.Leader || deployment.Leader.IsDeadAndOutOfLivesServer()))
                {
                    deployment.Leader=null;deployment.LeaderSpawned=false;unitRevision++;
                    if(Council.Fall(deployment.Player,deployment.Commander) && !owner.Meta.Recover(OwnerMaster(deployment.Player),deployment.Commander))
                        GauntletPortals.Announce($"<color=#d9785f>The {def.Name}'s {def.LeaderName} has fallen. Its squad fights on without reinforcements.</color>");
                }
                if(company.Fallen){if(deployment.Squad.Count==0){deployments.Remove(deployment);unitRevision++;}continue;}
                var leaderMaster=OwnerMaster(deployment.Player);var near=leaderMaster?leaderMaster.GetBody():null;
                if(!deployment.Leader && Time.fixedTime>=deployment.NextAttempt && near && near.healthComponent && near.healthComponent.alive)
                {
                    deployment.NextAttempt=Time.fixedTime+3;
                    deployment.Leader=Spawn(def.LeaderCard,near,deployment,true);
                    if(deployment.Leader){deployment.LeaderSpawned=true;Fill(deployment,def,near);deployment.ReinforcedRound=owner.Flow.Round;deployment.ReinforcedCycle=owner.Flow.Cycle;}
                }
                var leaderBody=deployment.Leader?deployment.Leader.GetBody():null;
                // A living commander restores its squad once at each round start.
                if(leaderBody && combat && (deployment.ReinforcedRound!=owner.Flow.Round || deployment.ReinforcedCycle!=owner.Flow.Cycle))
                {deployment.ReinforcedRound=owner.Flow.Round;deployment.ReinforcedCycle=owner.Flow.Cycle;Fill(deployment,def,leaderBody);}
                if(leaderBody && combat && leaderBody.healthComponent.alive && Time.fixedTime>=deployment.NextAbility)
                {
                    deployment.NextAbility=Time.fixedTime+def.Period;
                    try{Pulse(def,leaderBody);}
                    catch(Exception error){GauntletPlugin.Log.LogWarning(def.Name+" ability failed; the company keeps fighting. "+error);}
                }
            }
        }
        private static CharacterMaster OwnerMaster(ulong key)=>PlayerCharacterMasterController.instances
            .Where(player=>player && player.isConnected && player.master && player.master.netId.Value==key).Select(player=>player.master).FirstOrDefault();
        private void Fill(Deployment deployment,CommanderDef def,CharacterBody near)
        {
            for(int i=deployment.Squad.Count;i<def.SquadSize;i++)
            {var member=Spawn(def.SquadCard,near,deployment,false);if(member)deployment.Squad.Add(member);}
        }
        private CharacterMaster Spawn(string field,CharacterBody near,Deployment deployment,bool leader)
        {
            var card=FortressAssets.Load<CharacterSpawnCard>(field);
            if(!card || !Available(deployment.Commander,owner.Run))return null;
            var placement=new DirectorPlacementRule{placementMode=DirectorPlacementRule.PlacementMode.Approximate,spawnOnTarget=near.transform,minDistance=leader?4:3,maxDistance=leader?14:20};
            // The player summons the unit: native minion ownership gives ally
            // cards, kill credit and the player team; the AI stays the enemy's own.
            var player=OwnerMaster(deployment.Player);
            var request=new DirectorSpawnRequest(card,placement,rng){teamIndexOverride=TeamIndex.Player,ignoreTeamMemberLimit=true,summonerBodyObject=player?player.GetBodyObject():null};
            var spawned=DirectorCore.instance.TrySpawnObject(request);
            var master=spawned?spawned.GetComponent<CharacterMaster>():null;
            if(!master)return null;
            var def=GauntletCommandRoster.Commanders[deployment.Commander];
            // Ambient level keeps hired units level with the arena's enemies.
            master.inventory.GiveItemPermanent(RoR2Content.Items.UseAmbientLevel);
            int health=GauntletCommandRoster.HealthStacks(def.Tier,leader),damage=GauntletCommandRoster.DamageStacks(def.Tier,leader);
            if(health>0)master.inventory.GiveItemPermanent(RoR2Content.Items.BoostHp,health);
            if(damage>0)master.inventory.GiveItemPermanent(RoR2Content.Items.BoostDamage,damage);
            master.gameObject.AddComponent<GauntletCompanyUnit>().Commander=deployment.Commander;
            GauntletCompanyDamage.Apply(master,deployment.Commander,leader);
            unitRevision++;return master;
        }
        private void Withdraw(ulong player,int commander)
        {
            foreach(var deployment in deployments.Where(value=>value.Player==player && value.Commander==commander).ToArray()){Disband(deployment);deployments.Remove(deployment);}
        }
        private void WithdrawAll(){foreach(var deployment in deployments)Disband(deployment);deployments.Clear();unitRevision++;}
        private void Disband(Deployment deployment)
        {
            foreach(var master in deployment.Squad.Concat(new[]{deployment.Leader}))
            {
                if(!master)continue;var body=master.GetBody();
                if(body)NetworkServer.Destroy(body.gameObject);
                NetworkServer.Destroy(master.gameObject);
            }
            deployment.Squad.Clear();deployment.Leader=null;deployment.LeaderSpawned=false;unitRevision++;
        }
        private void OnDestroy(){if(NetworkServer.active && deployments.Count>0)WithdrawAll();}
        internal static IEnumerable<CharacterBody> Foes(Vector3 center,float radius)
        {
            float squared=radius*radius;
            foreach(var team in new[]{TeamIndex.Monster,TeamIndex.Void,TeamIndex.Lunar})
                foreach(var member in TeamComponent.GetTeamMembers(team))
                {
                    var body=member?member.body:null;
                    if(body && body.healthComponent && body.healthComponent.alive && (body.corePosition-center).sqrMagnitude<=squared)yield return body;
                }
        }
        private static IEnumerable<CharacterBody> Allies(Vector3 center,float radius)
        {
            float squared=radius*radius;
            foreach(var member in TeamComponent.GetTeamMembers(TeamIndex.Player))
            {
                var body=member?member.body:null;
                if(body && body.healthComponent && body.healthComponent.alive && (body.corePosition-center).sqrMagnitude<=squared)yield return body;
            }
        }
        // Server-side commander abilities use native buffs, DoTs, orbs and
        // blasts, so their visuals and damage replicate normally.
        private static void Pulse(CommanderDef def,CharacterBody leader)
        {
            Vector3 center=leader.corePosition;bool crit=Util.CheckRoll(leader.crit,leader.master);
            var foes=def.Allies?Array.Empty<CharacterBody>():Foes(center,def.Radius).ToArray();
            var allies=def.Allies?Allies(center,def.Radius).ToArray():Array.Empty<CharacterBody>();
            switch(def.Ability)
            {
                case CommandAbility.Kindle:foreach(var foe in foes)DotController.InflictDot(foe.gameObject,leader.gameObject,null,default(ProcChainMask),DotController.DotIndex.Burn,4,def.Power);break;
                case CommandAbility.Carapace:foreach(var ally in allies)ally.healthComponent.AddBarrier(ally.healthComponent.fullCombinedHealth*def.Power);break;
                case CommandAbility.Hymn:foreach(var ally in allies)ally.healthComponent.HealFraction(def.Power,default(ProcChainMask));break;
                case CommandAbility.Toll:foreach(var foe in foes)foe.AddTimedBuff(RoR2Content.Buffs.Slow60,def.Power);break;
                case CommandAbility.Hex:foreach(var foe in foes)foe.AddTimedBuff(RoR2Content.Buffs.Weak,def.Power);break;
                case CommandAbility.Shatter:foreach(var foe in foes)foe.AddTimedBuff(RoR2Content.Buffs.Pulverized,def.Power);break;
                case CommandAbility.Collapse:foreach(var foe in foes)if(!foe.isChampion)foe.AddTimedBuff(RoR2Content.Buffs.Nullified,def.Power);break;
                case CommandAbility.Warcry:foreach(var ally in allies)ally.AddTimedBuff(RoR2Content.Buffs.Warbanner,def.Power);break;
                case CommandAbility.Galvanic:
                    var first=foes.OrderBy(foe=>(foe.corePosition-center).sqrMagnitude).FirstOrDefault();
                    if(first && first.mainHurtBox)OrbManager.instance.AddOrb(new LightningOrb{origin=center,target=first.mainHurtBox,damageValue=leader.damage*def.Power,isCrit=crit,
                        bouncesRemaining=3,bouncedObjects=new List<HealthComponent>(),teamIndex=leader.teamComponent.teamIndex,attacker=leader.gameObject,inflictor=leader.gameObject,
                        procCoefficient=.6f,lightningType=LightningOrb.LightningType.Ukulele,damageColorIndex=DamageColorIndex.Item,range=def.Radius});
                    break;
                case CommandAbility.Riftstorm:
                    if(foes.Length==0)break;
                    new BlastAttack{attacker=leader.gameObject,inflictor=leader.gameObject,teamIndex=leader.teamComponent.teamIndex,baseDamage=leader.damage*def.Power,baseForce=800,
                        position=center,radius=def.Radius,falloffModel=BlastAttack.FalloffModel.SweetSpot,procCoefficient=.6f,crit=crit,damageColorIndex=DamageColorIndex.Item,
                        attackerFiltering=AttackerFiltering.NeverHitSelf}.Fire();
                    var explosion=FortressAssets.Load<GameObject>("OmniExplosionVFX_prefab");
                    if(explosion)EffectManager.SpawnEffect(explosion,new EffectData{origin=center,scale=def.Radius},true);
                    break;
            }
        }
        private void Broadcast()
        {
            if(!NetworkServer.active || !owner)return;
            sentRevision=Council.Revision;sentUnits=unitRevision;nextSnapshot=Time.unscaledTime+1;nextChangeSnapshot=Time.unscaledTime+.1f;
            var snapshot=new GauntletCommandSnapshot{RunId=owner.Run.netId,Seed=owner.RunKey,Sequence=++sequence,Capacity=(byte)Math.Max(1,Math.Min(6,Capacity))};
            foreach(var pair in Council.Accounts)
            {
                if(!OwnerMaster(pair.Key))continue;
                var view=new View{Player=pair.Key,Renown=pair.Value.Renown,Capacity=owner.Meta.Capacity(OwnerMaster(pair.Key))};
                foreach(var company in pair.Value.Companies)
                {
                    var units=deployments.Where(value=>value.Player==pair.Key && value.Commander==company.Commander)
                        .SelectMany(value=>new[]{value.Leader}.Concat(value.Squad)).Select(master=>master?master.GetBody():null)
                        .Select(body=>body?body.netId:NetworkInstanceId.Invalid).ToArray();
                    // Leader slot first; an empty slot marks a leaderless squad.
                    view.Companies.Add(new CompanyView{Commander=company.Commander,Fallen=company.Fallen,Units=units.Take(GauntletCommandSnapshot.MaxUnits).ToArray()});
                }
                snapshot.Views.Add(view);
            }
            Accept(snapshot);
            if(owner.Run.netId!=NetworkInstanceId.Invalid)GauntletNetwork.Send(GauntletNetwork.CompanyMessage,snapshot);
        }
        internal void Accept(GauntletCommandSnapshot snapshot)
        {
            ReceivedSequence=snapshot.Sequence;Capacity=snapshot.Capacity;Views.Clear();
            foreach(var view in snapshot.Views)Views[view.Player]=view;
        }
        private void Update()
        {
            if(Application.isBatchMode || !owner || Time.unscaledTime<nextInsignia)return;
            nextInsignia=Time.unscaledTime+.5f;
            foreach(var view in Views.Values)
                foreach(var company in view.Companies)
                    for(int i=0;i<company.Units.Length;i++)
                    {
                        if(company.Units[i]==NetworkInstanceId.Invalid)continue;
                        var found=Util.FindNetworkObject(company.Units[i]);var body=found?found.GetComponent<CharacterBody>():null;
                        if(body)GauntletInsignia.Ensure(body,company.Commander,i==0 && !company.Fallen);
                    }
        }
    }
    internal sealed class GauntletCompanyUnit:MonoBehaviour
    {
        internal int Commander=-1;
        private BaseAI[] brains;private float next;
        private void Awake()
        {
            brains=GetComponents<BaseAI>();
            foreach(var brain in brains)if(brain)brain.fullVision=true;
        }
        // Each unit keeps its species' own skill drivers. The council only
        // points idle or distant brains at the closest arena enemy.
        private void FixedUpdate()
        {
            if(!NetworkServer.active || Time.fixedTime<next)return;
            next=Time.fixedTime+.6f;
            foreach(var brain in brains)
            {
                if(!brain || !brain.body || brain.currentEnemy==null)continue;
                var current=brain.currentEnemy.gameObject?brain.currentEnemy.gameObject.GetComponent<CharacterBody>():null;
                if(current && current.healthComponent && current.healthComponent.alive && current.teamComponent && current.teamComponent.teamIndex!=TeamIndex.Player &&
                    (current.corePosition-brain.body.corePosition).sqrMagnitude<70*70)continue;
                var target=GauntletCompanies.Foes(brain.body.corePosition,250).Where(foe=>foe.GetVisibilityLevel(brain.body)!=VisibilityLevel.Untargetable)
                    .OrderBy(foe=>(foe.corePosition-brain.body.corePosition).sqrMagnitude).FirstOrDefault();
                if(!target)continue;
                brain.currentEnemy.gameObject=target.gameObject;brain.currentEnemy.bestHurtBox=target.mainHurtBox;
                brain.currentEnemy.Update();brain.enemyAttention=brain.enemyAttentionDuration;
            }
        }
    }
    internal sealed class GauntletCommandRequest:MessageBase
    {
        internal NetworkInstanceId User,RunId;internal ulong Seed;internal bool Dismiss;internal byte Commander;
        public override void Serialize(NetworkWriter writer){writer.Write(RunId);writer.Write(User);writer.Write(Seed);writer.Write(Dismiss);writer.Write(Commander);}
        public override void Deserialize(NetworkReader reader){RunId=reader.ReadNetworkId();User=reader.ReadNetworkId();Seed=reader.ReadUInt64();Dismiss=reader.ReadBoolean();Commander=reader.ReadByte();}
    }
    public sealed class GauntletCommandSnapshot:MessageBase,INetMessage
    {
        internal const int MaxAccounts=16,MaxCompanies=12,MaxUnits=12;
        internal NetworkInstanceId RunId;internal ulong Seed,Sequence;internal byte Capacity;
        internal readonly List<GauntletCompanies.View> Views=new List<GauntletCompanies.View>();
        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(RunId);writer.Write(Seed);writer.Write(Sequence);writer.Write(Capacity);
            writer.Write((byte)Math.Min(MaxAccounts,Views.Count));
            foreach(var view in Views.Take(MaxAccounts))
            {
                writer.Write(view.Player);writer.Write(view.Renown);writer.Write((byte)view.Capacity);writer.Write((byte)Math.Min(MaxCompanies,view.Companies.Count));
                foreach(var company in view.Companies.Take(MaxCompanies))
                {
                    writer.Write((byte)company.Commander);writer.Write(company.Fallen);writer.Write((byte)Math.Min(MaxUnits,company.Units.Length));
                    foreach(var unit in company.Units.Take(MaxUnits))writer.Write(unit);
                }
            }
        }
        public override void Deserialize(NetworkReader reader)
        {
            RunId=reader.ReadNetworkId();Seed=reader.ReadUInt64();Sequence=reader.ReadUInt64();Capacity=reader.ReadByte();
            int accounts=reader.ReadByte();Views.Clear();
            if(accounts>MaxAccounts)throw new FormatException("Too many company accounts.");
            for(int i=0;i<accounts;i++)
            {
                var view=new GauntletCompanies.View{Player=reader.ReadUInt64(),Renown=reader.ReadInt64(),Capacity=reader.ReadByte()};int companies=reader.ReadByte();
                if(companies>MaxCompanies)throw new FormatException("Too many companies.");
                for(int j=0;j<companies;j++)
                {
                    var company=new GauntletCompanies.CompanyView{Commander=reader.ReadByte(),Fallen=reader.ReadBoolean()};
                    int units=reader.ReadByte();if(units>MaxUnits)throw new FormatException("Too many units.");
                    company.Units=new NetworkInstanceId[units];
                    for(int k=0;k<company.Units.Length;k++)company.Units[k]=reader.ReadNetworkId();
                    view.Companies.Add(company);
                }
                Views.Add(view);
            }
        }
        public void OnReceived()
        {
            if(NetworkServer.active)return;
            var owner=GauntletController.Current;
            if(!owner || !owner.Companies || owner.Run.netId!=RunId || owner.RunKey!=Seed || Sequence<=owner.Companies.ReceivedSequence || Capacity<1 || Capacity>3 ||
                Views.Count>MaxAccounts || Views.Any(view=>view.Renown<0 || view.Capacity<1 || view.Capacity>4 || view.Companies.Count>MaxCompanies ||
                    view.Companies.Any(company=>company.Commander>=GauntletCommandRoster.Commanders.Length || company.Units.Length>MaxUnits)))return;
            owner.Companies.Accept(this);
        }
    }
}
