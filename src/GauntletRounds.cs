using System;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal static class GauntletRounds
    {
        internal static void Install()
        {
            On.RoR2.InfiniteTowerRun.Start+=StartRun;
            On.RoR2.InfiniteTowerRun.OnPrePopulateSceneServer+=Populate;
            On.RoR2.InfiniteTowerRun.OnWaveAllEnemiesDefeatedServer+=WaveCleared;
            On.RoR2.InfiniteTowerRun.ShouldUpdateRunStopwatch+=Stopwatch;
            On.RoR2.Run.PickNextStageScene+=NextStage;
            On.RoR2.SceneExitController.Begin+=Exit;
            On.RoR2.BazaarController.Start+=BazaarReady;
            SceneDirector.onPostPopulateSceneServer+=AfterPopulate;
        }
        internal static bool IsBazaar=>SceneInfo.instance && SceneInfo.instance.sceneDef && SceneInfo.instance.sceneDef.cachedName=="bazaar";
        private static SceneDef CastleScene(Run run)
        {
            var scene=SceneCatalog.FindSceneDef(FortressPlacement.SceneName);
            if(scene && run.CanPickStage(scene))return scene;
            throw new InvalidOperationException("Titanic Plains is unavailable; the custom battlefield requires its base-game scene.");
        }
        private static void StartRun(On.RoR2.InfiniteTowerRun.orig_Start original,InfiniteTowerRun run)
        {
            var owner=GauntletMode.IsActive(run)?run.GetComponent<GauntletController>():null;
            if(owner && NetworkServer.active)
            {
                owner.InitializeHost();var scene=CastleScene(run);
                var group=ScriptableObject.CreateInstance<SceneCollection>();group.name="BazaarGauntletFixedCastleMap";
                NativeAccess.Set(group,"_sceneEntries",new[]{new SceneCollection.SceneEntry{sceneDef=scene,weight=1}});
                run.startingSceneGroup=group;owner.StartingSceneCollection=group;
                GauntletPlugin.Log.LogInfo("Gladiator arena: "+scene.cachedName+". Combat begins automatically after players arrive.");
            }
            original(run);
            if(owner && NetworkServer.active)
            {GauntletCurrency.Ensure(run);GauntletLoot.PrepareRun(run);}
        }
        private static void NextStage(On.RoR2.Run.orig_PickNextStageScene original,Run run,WeightedSelection<SceneDef> choices)
        {
            var owner=GauntletMode.IsActive(run)?run.GetComponent<GauntletController>():null;
            if(!owner || !NetworkServer.active){original(run,choices);return;}
            
            run.nextStageScene=CastleScene(run);
        }
        private static void Populate(On.RoR2.InfiniteTowerRun.orig_OnPrePopulateSceneServer original,InfiniteTowerRun run,SceneDirector director)
        {
            if(!GauntletMode.IsActive(run)){original(run,director);return;}
            var owner=run.GetComponent<GauntletController>();owner.CommittedExit=null;
            if(IsBazaar)
            {
                // The native Simulacrum pre-populate method creates a ward and
                // rewrites interactable credit/spawns. It must not run in the Bazaar.
                NativeAccess.Call(run,"PerformStageCleanUp");
                GauntletArena.CleanUp(owner);owner.Fortress.Leaving=true;
                owner.Flow.BazaarLoaded();owner.PortalLoot.Arrived();GauntletNetwork.Broadcast(owner);return;
            }
            owner.Flow.ArenaLoaded();
            var fallback=GauntletArena.Prepare(owner);original(run,director);GauntletArena.Ready(owner,fallback);
            owner.Fortress.Arrive(owner.ArenaWard);
            NativeAccess.Set(director,"monsterCredit",0);director.teleporterSpawnCard=null;owner.UpdateLevelFloor();owner.PortalLoot.Arrived();
            GauntletNetwork.Broadcast(owner);
        }
        private static void AfterPopulate(SceneDirector director)
        {
            if(!GauntletController.Current || IsBazaar)return;
            FortressBattlefield.Ensure(GauntletController.Current)?.RefreshScenery();
            // The arena has no exploration interactables. Clear generated native
            // altars/guaranteed shops before players and round portals appear.
            foreach(var purchase in UnityEngine.Object.FindObjectsOfType<PurchaseInteraction>())
            {
                if(purchase.gameObject.scene!=director.gameObject.scene || purchase.GetComponentInParent<CharacterBody>())continue;
                var ward=GauntletController.Current.ArenaWard;
                if(ward && purchase.transform.IsChildOf(ward.transform))continue;
                var identity=purchase.GetComponentInParent<NetworkIdentity>();
                if(identity)NetworkServer.Destroy(identity.gameObject);else purchase.gameObject.SetActive(false);
            }
            foreach(var combat in CombatDirector.instancesList.ToArray())
                if(combat && !combat.GetComponentInParent<InfiniteTowerWaveController>())combat.enabled=false;
            GauntletController.Current.Shop.Prepare();
        }
        private static void BazaarReady(On.RoR2.BazaarController.orig_Start original,BazaarController bazaar)
        {
            original(bazaar);
            if(!NetworkServer.active || !GauntletController.Current)return;
            // Dreams cannot change this fixed-arena run; do not sell a scene
            // selection the return portal would have to ignore.
            foreach(var station in UnityEngine.Object.FindObjectsOfType<SeerStationController>())
            {var purchase=station.GetComponent<PurchaseInteraction>();if(purchase)purchase.SetAvailable(false);}
        }
        private static bool Stopwatch(On.RoR2.InfiniteTowerRun.orig_ShouldUpdateRunStopwatch original,InfiniteTowerRun run)
        {var owner=GauntletMode.IsActive(run)?run.GetComponent<GauntletController>():null;return owner?owner.Fortress.IsCombat:original(run);}
        private static void WaveCleared(On.RoR2.InfiniteTowerRun.orig_OnWaveAllEnemiesDefeatedServer original,InfiniteTowerRun run,InfiniteTowerWaveController wave)
        {
            if(!GauntletMode.IsActive(run)){original(run,wave);return;}
            var owner=run.GetComponent<GauntletController>();if(!owner || owner.ShuttingDown || !owner.ArenaReady || run.waveController!=wave)return;
            var budget=wave.GetComponent<GauntletWaveBudget>();
            if(!budget || !budget.Cleared || owner.Encounter.Phase!=owner.Flow.Phase || !owner.Encounter.Clear(wave))return;
            if(owner.Flow.Phase==GauntletPhase.Horde)
            {
                if(owner.Flow.HordeCleared())
                {
                    int count=GauntletMetaPolicy.Bosses(GauntletPlan.BossCount(owner.Flow.Round),owner.Flow.BossKind==GauntletBossKind.Ordinary,owner.Meta.Ascension);
                    GauntletPortals.Announce(count>1?$"Horde defeated. {count} champions enter the arena.":"Horde defeated. The next champion enters the arena.");
                }
                return;
            }
            if(!owner.Flow.BossCleared())return;
            // The phase is already AwaitingExit: a throwing drop hook must not keep the merchants shut.
            try {GauntletLoot.BossRewards(owner,owner.Fortress.Center+Vector3.up*3);}
            catch(Exception error){GauntletPlugin.Log.LogWarning("A champion reward drop failed; the round still ends. "+error);}
            owner.Fortress.RoundEnded();
        }
        private static void Exit(On.RoR2.SceneExitController.orig_Begin original,SceneExitController exit)
        {
            var owner=GauntletController.Current;
            if(!owner || owner.Run.isGameOverServer){original(exit);return;}
            if(!NetworkServer.active || owner.CommittedExit)return;
            if(NativeAccess.Get<SceneExitController.ExitState>(exit,"exitState")!=SceneExitController.ExitState.Idle)return;
            SceneDef destination;
            if(IsBazaar)
            {
                destination=CastleScene(owner.Run);
                int completed=owner.Flow.Round;
                if(!owner.Flow.ReturnToArena())return;
                if(GauntletBalance.AdvancesDifficultyStage(completed))owner.Run.NetworkstageClearCount=owner.Run.stageClearCount+1;
            }
            else
            {
                if(!GauntletPortals.Owns(exit))return;
                destination=SceneCatalog.FindSceneDef("bazaar");
                if(!destination || !owner.Flow.EnterBazaar())return;
            }
            owner.CommittedExit=exit;
            // Authoritative departure: heal now, collect only free loot, and
            // retain choice rewards for the destination without choosing for players.
            try {GauntletRecovery.RestorePlayers();}
            catch(Exception error){GauntletPlugin.Log.LogWarning("A player recovery callback failed; portal travel continues. "+error);}
            if(owner.CollectLoot)
                try {owner.PortalLoot.Collect();}
                catch(Exception error){GauntletPlugin.Log.LogWarning("A free-loot callback failed; portal travel continues. "+error);}
            owner.Fortress.Leaving=true;owner.ArenaReady=false;
            exit.useRunNextStageScene=false;exit.isAlternatePath=false;exit.destinationScene=destination;exit.doNotIncrementStageNumber=true;
            GauntletCurrency.Ensure(owner.Run);
            GauntletNetwork.Broadcast(owner);original(exit);
        }
    }
}
