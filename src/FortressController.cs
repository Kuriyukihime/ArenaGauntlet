using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
namespace BazaarGauntlet
{
    // Keep the wire/config identity. No defensive objective, construction or
    // automatic combat respawns are instantiated in gladiator mode.
    internal sealed class FortressController:MonoBehaviour
    {
        internal FortressState State=new FortressState();
        internal GauntletController Owner;
        internal readonly CharacterMaster[] Masters=new CharacterMaster[FortressTree.ModuleCount];
        internal readonly Dictionary<uint,ulong> Requests=new Dictionary<uint,ulong>();
        internal readonly Dictionary<uint,float> RequestTimes=new Dictionary<uint,float>();
        private readonly GauntletArrivalGate arrival=new GauntletArrivalGate();
        private readonly HashSet<CharacterMaster> funded=new HashSet<CharacterMaster>();
        private readonly GauntletStartingItems starters=new GauntletStartingItems();
        internal bool WaitingForStart {get=>State.Preparing;set=>State.Preparing=value;}
        internal bool Leaving;
        internal Vector3? PlannedAnchor;
        internal int Round=>NetworkServer.active?Owner.Flow.Round:Owner.RemoteRound;
        internal GauntletPhase Phase=>NetworkServer.active?Owner.Flow.Phase:Owner.RemotePhase;
        internal int PlanningRound=>Round;
        internal Vector3 Center=>FortressBattlefield.Origin;
        internal bool InCastle=>SceneInfo.instance && SceneInfo.instance.sceneDef && SceneInfo.instance.sceneDef.cachedName==FortressPlacement.SceneName;
        internal bool CanManage=>!Leaving && !Owner.ShuttingDown && !Owner.Run.isGameOverServer && InCastle &&
            (WaitingForStart || Phase==GauntletPhase.AwaitingExit || Phase==GauntletPhase.FinalDecision);
        internal bool IsCombat=>!WaitingForStart && !Leaving && InCastle && (Phase==GauntletPhase.Horde || Phase==GauntletPhase.Boss);
        private void Awake(){Owner=GetComponent<GauntletController>();}
        internal void Arrive(InfiniteTowerSafeWardController ward)
        {
            if(!NetworkServer.active || !ward)return;
            FortressBattlefield.Ensure(Owner);
            Leaving=false;WaitingForStart=true;arrival.Reset();
            State.X=Center.x;State.Y=Center.y;State.Z=Center.z;State.Yaw=0;State.Map=FortressPlacement.SceneName;State.Placed=true;State.Revision++;
            ward.transform.position=Center;
            GauntletWaves.HideWard(ward.gameObject);
            var purchase=ward.GetComponent<PurchaseInteraction>();if(purchase)purchase.SetAvailable(false);
            GauntletPortals.Clear();
        }
        internal bool BeginRound()
        {
            if(!NetworkServer.active || !CanManage || !Owner.ArenaReady || !Owner.ArenaWard)return false;
            if(!WaitingForStart || Phase!=GauntletPhase.Horde)return false;
            Owner.Shop.Close();
            State.Revision++;
            try {GauntletRecovery.RestorePlayers();}
            catch(Exception error){GauntletPlugin.Log.LogWarning("A recovery callback failed; automatic combat start continues. "+error);}
            Owner.UpdateLevelFloor();
            foreach(var machine in Owner.ArenaWard.GetComponents<EntityStateMachine>())
                if(machine.state is EntityStates.InfiniteTowerSafeWard.BaseSafeWardState)machine.SetNextState(new EntityStates.InfiniteTowerSafeWard.Active());
            return GauntletWaves.StartRound(Owner);
        }
        internal void RoundEnded()
        {
            try {Owner.Meta.RoundCleared();}
            catch(Exception error){GauntletPlugin.Log.LogWarning("Glory award failed; the round still ends. "+error);}
            if(Owner.CollectLoot)Owner.PortalLoot.RoundCleared();
            State.Revision++;ClearStragglers();StartCoroutine(SettleSummons());
            try {GauntletRecovery.RestorePlayers();}
            catch(Exception error){GauntletPlugin.Log.LogWarning("Intermission recovery will retry at combat start. "+error);}
            if(Phase==GauntletPhase.FinalDecision && !Owner.AllowLoop)
            {
                GauntletPortals.Announce("Final champion defeated. Victory!");StartCoroutine(FinishVictory());
            }
            else
            {
                if(Phase==GauntletPhase.FinalDecision)Owner.Flow.ContinueThroughBazaar();
                Owner.Shop.Open();
                GauntletPortals.Announce("Round complete. The merchant alcoves are open at the south gate. Use the ready brazier when you have finished shopping.");
            }
            Owner.Economy.CompleteRound();
            GauntletNetwork.Broadcast(Owner);
        }
        private void ClearStragglers()
        {
            foreach(var member in TeamComponent.GetTeamMembers(TeamIndex.Monster).ToArray())
            {
                var body=member?member.body:null;if(!body || !body.healthComponent || !body.healthComponent.alive)continue;
                var master=body.master;NetworkServer.Destroy(body.gameObject);if(master)NetworkServer.Destroy(master.gameObject);
            }
        }
        private IEnumerator SettleSummons()
        {
            for(int i=0;i<4;i++)
            {yield return new WaitForSeconds(.75f);if(!CanManage || Owner.ShuttingDown)yield break;ClearStragglers();}
        }
        internal int Price(int baseCost)=>Mathf.Max(1,Owner.Run.GetDifficultyScaledCost(baseCost));
        private IEnumerator FinishVictory()
        {
            yield return new WaitForSeconds(4);
            if(Owner && !Owner.ShuttingDown && !Owner.Run.isGameOverServer && Owner.Flow.ClaimVictory())
            {GauntletNetwork.Broadcast(Owner);Owner.Run.BeginGameOver(RoR2Content.GameEndings.MainEnding);}
        }
        private void FixedUpdate()
        {
            if(!NetworkServer.active || !Owner || Owner.ShuttingDown || Owner.Run.isGameOverServer || !InCastle || Leaving)return;
            starters.Tick(Owner);
            foreach(var player in PlayerCharacterMasterController.instances)
                if(player && player.isConnected && player.master && funded.Add(player.master))player.master.GiveMoney((uint)Owner.StartingGold,false);
            if(WaitingForStart && Phase==GauntletPhase.Horde)
            {
                var players=PlayerCharacterMasterController.instances.Where(p=>p && p.isConnected && p.master).ToArray();
                bool ready=Owner.ArenaReady && Owner.ArenaWard && !Owner.Run.NetworkinSceneTransition && NetworkUser.AllParticipatingNetworkUsersReady() &&
                    players.All(p=>Owner.Meta.Ready(p.networkUser)) &&
                    players.Length>0 && players.Any(p=>{var body=p.master.GetBody();return body && body.healthComponent && body.healthComponent.alive;});
                if(arrival.Tick(Time.unscaledTime,ready) && !BeginRound())arrival.Reset();
            }
            if(Phase==GauntletPhase.AwaitingExit)Owner.Shop.Tick();
        }
        internal void Detach(){Leaving=true;GauntletPortals.Clear();}
        // Legacy authoring/service sources remain editable, but are not activated.
        internal static Vector3 Offset(int module)=>Vector3.zero;
        internal Vector3 Position(int module)=>Center;
        internal void Capture(){}
        internal int RepairPrice(int module)=>0;
    }
}
