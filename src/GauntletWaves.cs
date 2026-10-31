using System;
using System.Linq;
using RoR2;
using R2API;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal static class GauntletWaves
    {
        internal static void ConfigurePrefab(InfiniteTowerRun run)
        {
            var hordeSource = NativeAccess.Get<GameObject>(run, "defaultWavePrefab");
            if (!hordeSource) throw new InvalidOperationException("Native standard wave prefab was not found.");
            var horde = PrefabAPI.InstantiateClone(hordeSource, "BazaarGauntletHordeWave");
            // Both phases retain native squad/clear/network behavior. Bosses
            // are explicitly spawned, rather than buying an indefinite number.
            var boss = PrefabAPI.InstantiateClone(hordeSource, "BazaarGauntletBossWave");
            foreach (var prefab in new[] { horde, boss })
            {
                var wave = prefab.GetComponent<InfiniteTowerWaveController>();
                NativeAccess.Set(wave, "isBossWave", prefab == boss);
                NativeAccess.Set(wave, "uiPrefab", null);
                // Two inert entries (null prefab/HUD child never instantiate): WolfoQoL reads overlayEntries[1] after every wave InstantiateUi.
                NativeAccess.Set(wave, "overlayEntries", new InfiniteTowerWaveController.OverlayEntry[2]);
                NativeAccess.Set(wave, "beginChatToken", "");
                NativeAccess.Set(wave, "baseCredits", 10000000f);
                NativeAccess.Set(wave, "linearCreditsPerWave", 0f);
                NativeAccess.Set(wave, "immediateCreditsFraction", 0f);
                NativeAccess.Set(wave, "secondsBeforeSuddenDeath", float.PositiveInfinity);
                NativeAccess.Set(wave, "secondsBeforeFailsafe", 300f);
                wave.secondsAfterWave = 4;
                var director = NativeAccess.Get<CombatDirector>(wave, "combatDirector");
                director.moneyWaveIntervals = Array.Empty<RangeFloat>();
            }
            var hordeCategory = Category("BazaarGauntletHorde", horde, 1);
            var bossCategory = Category("BazaarGauntletBoss", boss, 2);
            NativeAccess.Set(run, "defaultWavePrefab", horde);
            NativeAccess.Set(run, "waveCategories", new[] { bossCategory, hordeCategory });
        }
        private static InfiniteTowerWaveCategory Category(string name, GameObject prefab, int period)
        {
            var category = ScriptableObject.CreateInstance<InfiniteTowerWaveCategory>();category.name = name;
            NativeAccess.Set(category, "wavePrefabs", new[] { new InfiniteTowerWaveCategory.WeightedWave { wavePrefab = prefab, weight = 1f } });
            NativeAccess.Set(category, "availabilityPeriod", period);NativeAccess.Set(category, "minWaveIndex", 0);
            return category;
        }
        internal static void Install()
        {
            GauntletEnemyIndicators.Install();
            On.RoR2.InfiniteTowerRun.BeginNextWave+=BeginWave;
            On.RoR2.InfiniteTowerRun.OnSafeWardActivated+=WardActivated;
            On.RoR2.InfiniteTowerRun.InitializeWaveController+=InitializeController;
            On.RoR2.InfiniteTowerWaveController.OnEnable+=WaveEnabled;
            On.RoR2.InfiniteTowerWaveController.FixedUpdate+=WaveTick;
            On.RoR2.InfiniteTowerWaveController.Initialize += Initialize;
            On.RoR2.CombatDirector.PrepareNewMonsterWave += PrepareHorde;
            On.RoR2.CombatDirector.Spawn_SpawnArgs += PlaceAssault;
            GauntletAggression.Install();
            On.EntityStates.InfiniteTowerSafeWard.Active.OnEnter += ActiveWard;
            On.EntityStates.InfiniteTowerSafeWard.AwaitingActivation.OnEnter += WaitingWard;
            On.RoR2.InfiniteTowerWaveController.GetNormalizedProgress += Progress;
        }
        internal static bool StartRound(GauntletController owner)
        {
            if(owner.Encounter.Pending)return true;
            Request(owner);
            return owner.Encounter.Pending || owner.Encounter.Committed || owner.Encounter.CanRetry;
        }
        private static void Request(GauntletController owner)
        {
            owner.RequestingWave=true;
            try {NativeAccess.Call(owner.Run,"BeginNextWave");}
            catch(Exception error){Retry(owner,error);}
            finally {owner.RequestingWave=false;}
        }
        internal static void Tick(GauntletController owner)
        {
            if(!NetworkServer.active || !owner || owner.ShuttingDown || !owner.ArenaReady || owner.Fortress.Leaving || !owner.Fortress.InCastle || owner.Run.isGameOverServer)return;
            if(owner.Encounter.CanRetry && owner.RetryWaveAt>=0 && Time.unscaledTime>=owner.RetryWaveAt)
            {owner.RetryWaveAt=-1;Request(owner);}
        }
        private static void WardActivated(On.RoR2.InfiniteTowerRun.orig_OnSafeWardActivated original,InfiniteTowerRun run,InfiniteTowerSafeWardController ward)
        {
            // The native callback force-finishes an existing wave before starting
            // another. Only the arrival gate starts our first encounter.
            if(!GauntletMode.IsActive(run))original(run,ward);
        }
        private static void BeginWave(On.RoR2.InfiniteTowerRun.orig_BeginNextWave original,InfiniteTowerRun run)
        {
            var owner=GauntletMode.IsActive(run)?run.GetComponent<GauntletController>():null;
            if(!owner){original(run);return;}
            if(!NetworkServer.active || owner.ShuttingDown || !owner.ArenaReady || !owner.ArenaWard || owner.Fortress.Leaving || !owner.Fortress.InCastle)return;
            bool nextBoss=owner.Flow.Phase==GauntletPhase.Boss && owner.Encounter.Phase==GauntletPhase.Horde && owner.Encounter.Cleared;
            if(!owner.RequestingWave && !nextBoss)return;
            if(!owner.Encounter.TryBegin(owner.Flow.Cycle,owner.Flow.Round,owner.Flow.Phase))return;
            owner.RetryWaveAt=-1;
            try
            {
                // A failed attempt must not consume the horde/boss parity slot.
                run.Network_waveIndex=owner.Encounter.NativeIndex-1;
                original(run);
                var wave=run.waveInstance?run.waveInstance.GetComponent<InfiniteTowerWaveController>():null;
                if(!wave || !owner.Encounter.Owns(wave) && !owner.Encounter.Attach(wave))throw new InvalidOperationException("The native encounter did not produce its reserved wave.");
            }
            catch(Exception error){Retry(owner,error);}
        }
        private static void WaveEnabled(On.RoR2.InfiniteTowerWaveController.orig_OnEnable original,InfiniteTowerWaveController wave)
        {
            original(wave);
            var owner=GauntletController.Current;
            if(!owner || !NetworkServer.active)return;
            var director=NativeAccess.Get<CombatDirector>(wave,"combatDirector");
            if(director){director.enabled=false;director.monsterCredit=0;}
            if(owner.Encounter.Pending)owner.Encounter.Attach(wave);
        }
        private static void WaveTick(On.RoR2.InfiniteTowerWaveController.orig_FixedUpdate original,InfiniteTowerWaveController wave)
        {
            var owner=GauntletController.Current;
            if(owner)
            {
                if(NetworkServer.active)
                {
                    var budget=wave.GetComponent<GauntletWaveBudget>();
                    if(!owner.Encounter.Owns(wave) || !owner.Encounter.Committed || !budget || !budget.Committed)return;
                }
                else if(owner.Run.waveController!=wave || !wave.defaultEnemyIndicatorPrefab)return;
            }
            original(wave);
        }
        private static void InitializeController(On.RoR2.InfiniteTowerRun.orig_InitializeWaveController original,InfiniteTowerRun run)
        {
            var owner=GauntletMode.IsActive(run)?run.GetComponent<GauntletController>():null;
            if(!owner || !NetworkServer.active){original(run);return;}
            var wave=run.waveInstance?run.waveInstance.GetComponent<InfiniteTowerWaveController>():null;
            if(!wave || !owner.Encounter.Owns(wave))return;
            if(owner.Encounter.Committed){NativeAccess.Set(run,"_waveController",wave);return;}
            try
            {
                original(run); // Includes the native defeat-event subscription.
                var budget=wave.GetComponent<GauntletWaveBudget>();
                if(!budget || !owner.Encounter.Commit(wave))throw new InvalidOperationException("Encounter initialization lost its reserved owner.");
                budget.Commit();
                owner.Fortress.WaitingForStart=false;owner.Fortress.State.Revision++;
            }
            catch(Exception error){Retry(owner,error);return;}
            GauntletNetwork.Broadcast(owner);
        }
        private static void Retry(GauntletController owner,Exception error)
        {
            var wave=owner.Encounter.Current;
            if(!owner.Encounter.Fail(wave))return;
            var budget=wave?wave.GetComponent<GauntletWaveBudget>():null;if(budget)budget.Retire();
            if(wave)
                foreach(var director in wave.GetComponentsInChildren<CombatDirector>(true)){director.enabled=false;director.monsterCredit=0;}
            NativeAccess.Call(owner.Run,"CleanUpCurrentWave");owner.ActiveBudget=null;
            owner.Fortress.WaitingForStart=true;owner.Fortress.State.Revision++;
            owner.ThemeName="Retrying "+(owner.Flow.Phase==GauntletPhase.Boss?"champion":"horde")+" setup";
            owner.RetryWaveAt=Time.unscaledTime+2;
            GauntletPlugin.Log.LogWarning("Encounter setup failed before spawning. Retrying the same round/phase; no progress or rewards were granted. "+error);
            GauntletNetwork.Broadcast(owner);
        }
        private static void Initialize(On.RoR2.InfiniteTowerWaveController.orig_Initialize original,
            InfiniteTowerWaveController wave, int index, Inventory inventory, GameObject target)
        {
            var owner = GauntletController.Current;
            if (owner && NetworkServer.active)
            {
                NativeAccess.Set(wave, "convertGoldOnWaveFinish", false);
                var director = NativeAccess.Get<CombatDirector>(wave, "combatDirector");
                if (director)
                {
                    director.maxSquadCount = (uint)owner.ConcurrentEnemyLimit;
                    // The wave's progressive cap owns concurrency. Otherwise
                    // the monster team's native soft cap still stops at 40.
                    director.ignoreTeamSizeLimit = true;
                    director.maxSpawnDistance = owner.ArenaRadius;
                director.spawnDistanceMultiplier = 3.2f;
                    director.minSpawnRange = 10;
                    director.minSeriesSpawnInterval /= 1.25f;director.maxSeriesSpawnInterval /= 1.25f;
                    // Actual Typhoon already applies its reward reduction in
                    // CombatDirector.Awake. Never compound it a second time.
                    if (DifficultyCatalog.GetDifficultyDef(owner.Run.selectedDifficulty).nameToken.IndexOf("TYPHOON", StringComparison.OrdinalIgnoreCase) < 0)
                    { director.expRewardCoefficient *= GauntletBalance.RewardMultiplier;director.goldRewardCoefficient *= GauntletBalance.RewardMultiplier; }
                    director.goldRewardCoefficient*=owner.GoldMultiplier;
                }
                NativeAccess.Set(wave, "maxSquadSize", owner.ConcurrentEnemyLimit);
                var squad = NativeAccess.Get<CombatSquad>(wave, "combatSquad");
                if (squad) squad.grantBonusHealthInMultiplayer = owner.Flow.Phase == GauntletPhase.Boss;
            }
            original(wave, index, inventory, target);
            if (owner && NetworkServer.active)
            {
                NativeAccess.Set(wave, "creditsPerSecond", GauntletBalance.CreditRate(owner.Flow.Round, owner.Run.participatingPlayerCount, owner.Run.difficultyCoefficient,owner.Flow.Cycle));
                var budget = wave.gameObject.AddComponent<GauntletWaveBudget>();
                budget.Initialize(owner, wave, NativeAccess.Get<CombatDirector>(wave, "combatDirector"));
            }
        }
        private static bool PlaceAssault(On.RoR2.CombatDirector.orig_Spawn_SpawnArgs original,CombatDirector director,CombatDirector.SpawnArgs args)
        {
            var owner=GauntletController.Current;var budget=owner?owner.ActiveBudget:null;
            if(!NetworkServer.active || !owner)return original(director,args);
            if(director.GetComponentInParent<InfiniteTowerWaveController>() && (!budget || !budget.Owns(director)))return false;
            if(!budget || !budget.Owns(director))return original(director,args);
            if(!budget.Place(args.spawnCard,out var placement))return false;
            args.placementRule=placement;return original(director,args);
        }
        private static void PrepareHorde(On.RoR2.CombatDirector.orig_PrepareNewMonsterWave original,CombatDirector director,DirectorCard card)
        {
            var owner=GauntletController.Current;
            if(NetworkServer.active && owner && owner.ActiveBudget)
                card=owner.ActiveBudget.NextHordeCard(director,card);
            original(director,card);
        }
        private static float Progress(On.RoR2.InfiniteTowerWaveController.orig_GetNormalizedProgress original, InfiniteTowerWaveController wave)
        {
            var owner = GauntletController.Current;
            return owner && owner.Run.waveController == wave && owner.SpawnQuota > 0 ?
                Mathf.Clamp01((float)owner.Defeated / owner.SpawnQuota) : original(wave);
        }
        private static void ActiveWard(On.EntityStates.InfiniteTowerSafeWard.Active.orig_OnEnter original,
            EntityStates.InfiniteTowerSafeWard.Active state)
        {
            var owner = GauntletController.Current;
            // GauntletObjective reports the arena phase; drop the native "Survive.." objective.
            if (owner) {state.radius = owner.ArenaRadius > 0 ? owner.ArenaRadius : 75;state.objectiveToken=null;}
            original(state);
            if(owner)
            {
                var purchase=state.outer.GetComponent<PurchaseInteraction>();
                if(purchase && NetworkServer.active)purchase.SetAvailable(false);
                HideWard(state.outer.gameObject);
            }
        }
        // The native ward keeps a solid World-layer "StandableSurface" sphere; hiding
        // only its renderers left an invisible ball in the arena centre. Triggers stay.
        internal static void HideWard(GameObject ward)
        {
            foreach(var renderer in ward.GetComponentsInChildren<Renderer>(true))renderer.forceRenderingOff=true;
            foreach(var collider in ward.GetComponentsInChildren<Collider>(true))if(!collider.isTrigger)collider.enabled=false;
        }
        private static void WaitingWard(On.EntityStates.InfiniteTowerSafeWard.AwaitingActivation.orig_OnEnter original,
            EntityStates.InfiniteTowerSafeWard.AwaitingActivation state)
        {
            var owner = GauntletController.Current;
            // The hidden ward cannot be activated; drop the native "Activate the Focus" objective.
            if (owner) {state.radius = owner.ArenaRadius > 0 ? owner.ArenaRadius : 75;state.objectiveToken=null;}
            original(state);
            if(owner)
            {
                var purchase=state.outer.GetComponent<PurchaseInteraction>();
                if(purchase && NetworkServer.active)purchase.SetAvailable(false);
                HideWard(state.outer.gameObject);
            }
        }
    }
}
