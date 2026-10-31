using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal sealed class GauntletController : MonoBehaviour
    {
        internal GauntletFlow Flow { get; } = new GauntletFlow();
        internal readonly GauntletExperiencePolicy Experience=new GauntletExperiencePolicy();
        internal int StartingAdvantage { get; private set; }
        internal int FinalAdvantage { get; private set; }
        internal int EnemyLimit { get; private set; }
        internal float ArenaRadius { get; set; }
        internal float DropMultiplier { get; private set; }
        internal float MinimumScaling { get; set; }
        internal float GoldMultiplier {get;private set;}
        internal int StartingGold {get;private set;}
        internal float RenownMultiplier {get;private set;}
        internal GauntletCompanies Companies;
        internal GauntletEconomy Economy;
        internal GauntletShop Shop;
        internal GauntletMetaprogression Meta;
        internal GauntletAscension Ascension;
        internal GauntletLootInstancing Loot;
        internal string ThemeName = "Preparing arena";
        internal int Spawned, SpawnQuota, Defeated;
        internal GauntletWaveBudget ActiveBudget;
        internal readonly GauntletEncounterGate<InfiniteTowerWaveController> Encounter=new GauntletEncounterGate<InfiniteTowerWaveController>();
        internal bool RequestingWave;
        internal float RetryWaveAt=-1;
        internal GauntletPortalLoot PortalLoot;
        internal FortressController Fortress;
        internal SceneExitController CommittedExit;
        internal ulong SentSnapshot, ReceivedSnapshot;
        internal SceneCollection StartingSceneCollection;
        internal CharacterMaster BossMaster;
        internal bool CollectLoot { get; private set; }
        internal bool AllowLoop { get; set; }
        internal int RemoteEnemyLevel { get; set; }
        internal int RemoteRound { get; set; }
        internal int RemoteCycle { get; set; }
        internal GauntletPhase RemotePhase { get; set; }
        private float nextSnapshot;
        internal Xoroshiro128Plus ArenaRng;
        internal PickupDropTable LastBossDropTable;
        internal InfiniteTowerSafeWardController ArenaWard;
        internal bool ArenaReady, ShuttingDown;
        internal int ConcurrentEnemyLimit => GauntletBalance.ConcurrentLimit(Flow.Round, Flow.Cycle, Run.participatingPlayerCount, EnemyLimit);
        internal static GauntletController Current => GauntletMode.IsActive(RoR2.Run.instance) ? RoR2.Run.instance.GetComponent<GauntletController>() : null;
        internal InfiniteTowerRun Run { get; private set; }
        // Run.seed is assigned only on the host (clients read 0); packets use the synced run GUID instead.
        internal ulong RunKey => Run ? System.BitConverter.ToUInt64(Run.GetUniqueId().ToByteArray(), 0) : 0;
        private void Awake()
        {
            Run = GetComponent<InfiniteTowerRun>();
            PortalLoot = gameObject.AddComponent<GauntletPortalLoot>();
            Fortress = gameObject.AddComponent<FortressController>();
            Companies = gameObject.AddComponent<GauntletCompanies>();
            Economy = gameObject.AddComponent<GauntletEconomy>();
            Shop = gameObject.AddComponent<GauntletShop>();
            Meta = gameObject.AddComponent<GauntletMetaprogression>();
            Ascension = gameObject.AddComponent<GauntletAscension>();
            Loot = gameObject.AddComponent<GauntletLootInstancing>();
            gameObject.AddComponent<GauntletObjective>();
        }
        internal void InitializeHost()
        {
            if (!NetworkServer.active) return;
            if (Flow.Phase != GauntletPhase.Idle) return;
            StartingAdvantage = GauntletConfig.StartingLevelAdvantage.Value;
            FinalAdvantage = Mathf.Max(StartingAdvantage, GauntletConfig.FinalLevelAdvantage.Value);
            EnemyLimit = GauntletConfig.EnemyLimit.Value;ArenaRadius = GauntletConfig.ArenaRadius.Value;
            DropMultiplier = GauntletConfig.DropMultiplier.Value;CollectLoot = GauntletConfig.CollectPortalLoot.Value;
            MinimumScaling = GauntletConfig.MinimumScaling.Value;
            GoldMultiplier=GauntletConfig.FortressGoldMultiplier.Value;StartingGold=GauntletConfig.FortressStartingGold.Value;
            RenownMultiplier=GauntletConfig.RenownMultiplier.Value;Companies.ConfiguredCapacity=GauntletConfig.MaxCompanies.Value;
            Meta.Initialize();
            Economy.Initialize(this);
            AllowLoop = GauntletConfig.AllowEndlessContinuation.Value;
            ArenaRng = new Xoroshiro128Plus(Run.seed ^ 0x42415A414152UL);
            Flow.Start();
            GauntletCatalogue.Prepare(this);
        }
        private void FixedUpdate()
        {
            GauntletWaves.Tick(this);
            if (!NetworkServer.active || ShuttingDown || Flow.Phase == GauntletPhase.Idle || Time.unscaledTime < nextSnapshot) return;
            nextSnapshot = Time.unscaledTime + 1f;
            GauntletNetwork.Broadcast(this);
        }
        private void OnDestroy()
        {
            GauntletArena.CleanUp(this);
            if (StartingSceneCollection) Destroy(StartingSceneCollection);
        }
        internal void UpdateLevelFloor(int previousLevel = -1)
        {
            if (!Run) return;
            if (!NetworkServer.active)
            {
                NativeAccess.RaiseEnemyLevel(Run, RemoteEnemyLevel, previousLevel);
                return;
            }
            if (!TeamManager.instance || Flow.Round < 1 || !ArenaReady || ShuttingDown) return;
            int level = (int)System.Math.Min(int.MaxValue, TeamManager.instance.GetTeamLevel(TeamIndex.Player));
            int round = Flow.Cycle > 1 ? GauntletPlan.RoundsPerCycle : Flow.Round;
            NativeAccess.RaiseEnemyLevel(Run, GauntletPlan.EnemyLevelFloor(Run.ambientLevelFloor, level, round, StartingAdvantage, FinalAdvantage), previousLevel);
        }
    }
}
