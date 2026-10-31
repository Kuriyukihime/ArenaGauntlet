using System;
using System.Linq;
using RoR2;
using UnityEngine;

namespace BazaarGauntlet
{
    internal sealed class GauntletWaveBudget : MonoBehaviour
    {
        private GauntletController owner;
        private InfiniteTowerWaveController wave;
        private CombatDirector director;
        private CharacterSpawnCard boss;
        private CombatSquad squad;
        private GauntletQuotaLedger<CharacterMaster> ledger;
        private GauntletDropBudget drops;
        private GauntletHordeRewards hordeRewards;
        private float nextBossAttempt, started;
        private int target, targetPlayer;
        private float nextPlayerTarget;
        private bool isBoss, finishedSpawning, retired, initialized, committed;
        private DirectorCard[] hordeCards;
        private GameObject[] hordeBodies;
        private int[] hordeCosts;
        private GauntletHordeMix mix;
        private GauntletSpawnRoutes routes;
        internal bool Committed=>committed && !retired;
        internal bool Cleared=>Committed && ledger!=null && ledger.Complete && squad && squad.memberCount==0;
        internal bool Owns(CombatDirector source)=>source==director && Committed;
        internal bool Place(SpawnCard card,out DirectorPlacementRule placement)
        {placement=null;if(!Committed || finishedSpawning)return false;if(routes==null)routes=new GauntletSpawnRoutes(owner);return routes.Place(card,out placement);}
        internal float Progress => ledger != null ? Mathf.Clamp01((float)ledger.Defeated / ledger.Target) : 0;
        internal void Initialize(GauntletController controller, InfiniteTowerWaveController current, CombatDirector combat)
        {
            owner = controller;wave = current;director = combat;started = Time.fixedTime;
            if(!director)throw new InvalidOperationException("Encounter combat director missing.");
            isBoss = owner.Flow.Phase == GauntletPhase.Boss;
            target = isBoss ? GauntletMetaPolicy.Bosses(GauntletPlan.BossCount(owner.Flow.Round),owner.Flow.BossKind==GauntletBossKind.Ordinary,owner.Meta.Ascension) : GauntletBalance.HordeSize(owner.Flow.Round, owner.Flow.Cycle, owner.Run.participatingPlayerCount);
            if(!isBoss)hordeRewards=new GauntletHordeRewards(GauntletBalance.HordeRewardSlots(owner.Flow.Round,owner.Flow.Cycle,owner.Run.participatingPlayerCount),target);
            ledger = new GauntletQuotaLedger<CharacterMaster>(target);
            squad = NativeAccess.Get<CombatSquad>(wave, "combatSquad");
            if(!squad)throw new InvalidOperationException("Encounter combat squad missing.");
            squad.onMemberDefeatedServer += Defeated;
            squad.onMemberLost += Despawned;

            if (director.onSpawnedServer == null) director.onSpawnedServer = new CombatDirector.OnSpawnedServer();
            director.onSpawnedServer.AddListener(Spawned);
            if (isBoss)
            {
                director.enabled = false;boss = GauntletCatalogue.Boss(owner);
            }
            else
            {
                director.monsterCards = GauntletCatalogue.HordeDeck(owner);
                hordeCards=director.monsterCards.categories.SelectMany(category=>category.cards).ToArray();
                hordeBodies=hordeCards.Select(card=>card.GetSpawnCard().prefab.GetComponent<CharacterMaster>().bodyPrefab).ToArray();
                hordeCosts=hordeCards.Select(card=>card.cost).ToArray();
                mix=new GauntletHordeMix(hordeCards.Select(card=>card.selectionWeight).ToArray());
                director.maximumNumberToSpawnBeforeSkipping=2;
                director.skipSpawnIfTooCheap=false;
                director.resetMonsterCardIfFailed=true;
                director.targetPlayers=false;
                NativeAccess.Set(director,"currentMonsterCard",null);
                director.shouldSpawnOneWave = false;
                director.fallBackToStageMonsterCards = false;
                director.monsterCredit = 0;
            }
            AimSpawnsAtPlayer();initialized=true;
        }
        internal void Commit()
        {
            if(!initialized || retired || committed)throw new InvalidOperationException("Encounter budget cannot be committed twice or before setup.");
            owner.ActiveBudget=this;owner.Spawned=owner.Defeated=0;owner.SpawnQuota=target;
            owner.BossMaster=null;owner.LastBossDropTable=null;committed=true;
            if(!isBoss)drops=new GauntletDropBudget(target,GauntletDropBudget.Expected(owner.Flow.Round,owner.Run.participatingPlayerCount,owner.DropMultiplier,owner.Flow.Cycle),owner.Run.treasureRng.nextNormalizedFloat);
            GauntletPlugin.Log.LogInfo($"Cycle {owner.Flow.Cycle} round {owner.Flow.Round}: {(isBoss ? "boss" : "horde")}, quota {target}, live cap {owner.ConcurrentEnemyLimit}, enemy level {owner.Run.ambientLevelFloor}.");
            if(!isBoss){director.monsterCredit=GauntletBalance.InitialCredits(owner.Flow.Round,owner.Run.participatingPlayerCount,owner.Flow.Cycle);director.enabled=true;}
        }
        internal DirectorCard NextHordeCard(CombatDirector source,DirectorCard fallback)
        {
            if(source!=director || isBoss || retired || finishedSpawning || mix==null)return fallback;
            var available=hordeCards.Select(card=>card!=null && card.IsAvailable()).ToArray();
            int index=mix.Pick(available,hordeCosts,director.monsterCredit,owner.ArenaRng.RangeInt(0,int.MaxValue));
            return index>=0?hordeCards[index]:fallback;
        }
        private Transform AimSpawnsAtPlayer()
        {
            var players = PlayerCharacterMasterController.instances;
            for (int offset = 0; offset < players.Count; offset++)
            {
                int index = (targetPlayer + offset) % players.Count;
                var player = players[index];
                var body = player && player.isConnected && player.master ? player.master.GetBody() : null;
                if (!body || !body.healthComponent || !body.healthComponent.alive) continue;
                targetPlayer = (index + 1) % players.Count;
                director.currentSpawnTarget = body.gameObject;
                return body.transform;
            }
            return null;
        }
        private void Spawned(GameObject instance)
        {
            if (!Committed || finishedSpawning) return;
            var master = instance ? instance.GetComponent<CharacterMaster>() : null;
            if (!master || !ledger.Spawn(master)) return;
            owner.Spawned = ledger.Spawned;
            if(!isBoss && hordeRewards!=null)
            {
                var body=master.GetBody();var rewards=body?body.GetComponent<DeathRewards>():null;
                // Native CombatDirector initializes expReward before this event.
                // Preserve spawnValue: item odds are independent of this XP compensation.
                if(rewards)rewards.expReward=hordeRewards.Experience(rewards.expReward);
            }
            if(!isBoss && mix!=null)
                for(int i=0;i<hordeBodies.Length;i++)if(master.bodyPrefab==hordeBodies[i]){mix.Record(i);break;}
            if (isBoss)
            {
                owner.BossMaster=master;
                var body=master.GetBody();var rewards=body?body.GetComponent<DeathRewards>():null;
                if(rewards && rewards.bossDropTable)owner.LastBossDropTable=rewards.bossDropTable;
            }
            if (master) master.gameObject.AddComponent<GauntletAggression>();
            GauntletAscension.Apply(owner,master,isBoss);
            if (master && isBoss && owner.Flow.BossKind != GauntletBossKind.Ordinary)
            {
                // Major arena encounters have more staying power, without a
                // matching arbitrary damage spike on top of their level lead.
                master.inventory.GiveItemPermanent(RoR2Content.Items.BoostHp,
                    owner.Flow.BossKind == GauntletBossKind.Final ? 10 : 5);
            }
            if (!ledger.Full)
            {
                // Each required slot picks its own champion. Once one is safely
                // spawned, prefer a different eligible species for the second.
                if(isBoss){boss=GauntletCatalogue.Boss(owner,false,boss);started=Time.fixedTime;}
                return;
            }
            finishedSpawning = true;director.enabled = false;director.monsterCredit = 0;
        }
        // Renown follows the same once-per-quota-member ledger as round progress.
        internal static event Action<GauntletController,CharacterMaster,DamageReport,bool> QuotaDefeated;
        internal long Renown(ulong player,long amount)=>Committed && !isBoss && hordeRewards!=null?hordeRewards.Renown(player,amount):amount;
        private void Defeated(CharacterMaster master, DamageReport report)
        {
            if (!Committed || !ledger.Defeat(master)) return;
            owner.Defeated = ledger.Defeated;
            try { GauntletLoot.EnemyDied(owner,master,report,isBoss,drops); }
            catch (Exception error) { GauntletPlugin.Log.LogWarning("An item drop failed; round progress continues. "+error); }
            try { QuotaDefeated?.Invoke(owner,master,report,isBoss); }
            catch (Exception error) { GauntletPlugin.Log.LogWarning("A renown award failed; round progress continues. "+error); }
        }
        private void Despawned(CharacterMaster master)
        {
            if (!Committed || !owner || owner.ShuttingDown || !owner.ArenaReady || !wave || wave.isFinished || owner.Run.NetworkinSceneTransition || owner.ActiveBudget != this || !ledger.Despawn(master)) return;
            // Engine culling/despawning does not satisfy a kill quota. Replace
            // that roster slot, while ignoring duplicate losses and summons.
            owner.Spawned = ledger.Spawned;finishedSpawning = false;
            wave.Network_totalWaveCredits = Mathf.Max(10000000, director.totalCreditsSpent + 10000000);
            director.enabled = !isBoss;
        }
        private void FixedUpdate()
        {
            if (!Committed || !owner || owner.ShuttingDown || !owner.ArenaReady || !wave || !director) return;
            if(isBoss && !owner.LastBossDropTable && owner.BossMaster)
            {
                var body=owner.BossMaster.GetBody();var rewards=body?body.GetComponent<DeathRewards>():null;
                if(rewards)owner.LastBossDropTable=rewards.bossDropTable;
            }
            if (Time.fixedTime >= nextPlayerTarget)
            { nextPlayerTarget = Time.fixedTime + 1;AimSpawnsAtPlayer(); }
            if (finishedSpawning)
            {
                // Keep the native clear condition: the quota must be spawned
                // AND its combat squad defeated. This never kills survivors.
                director.monsterCredit = 0;
                if (wave.totalWaveCredits != director.totalCreditsSpent) wave.Network_totalWaveCredits = director.totalCreditsSpent;
                return;
            }
            if (!isBoss || Time.fixedTime < nextBossAttempt) return;
            nextBossAttempt = Time.fixedTime + 1;
            var ward = NativeAccess.Get<InfiniteTowerSafeWardController>(owner.Run, "safeWardController");
            if (!ward) return;
            if (Time.fixedTime - started > 20)
            {
                // An incompatible boss/map must not strand a run. Retry with
                // an ordinary registered champion, retaining the major bonus.
                boss = GauntletCatalogue.Boss(owner, true);started = Time.fixedTime;
                GauntletPlugin.Log.LogWarning("Major boss placement failed; retrying with a compatible champion.");
            }
            director.Spawn(boss, null, AimSpawnsAtPlayer() ?? ward.transform, preventOverhead: true, spawnDistance: DirectorCore.MonsterSpawnDistance.Standard);
        }
        internal void Retire()
        {
            retired = true;
            enabled = false;
            if (director) { director.enabled = false; director.monsterCredit = 0; }
        }
        private void OnDestroy()
        {
            if (director && director.onSpawnedServer != null) director.onSpawnedServer.RemoveListener(Spawned);
            if (squad) { squad.onMemberDefeatedServer -= Defeated;squad.onMemberLost -= Despawned; }
            if (owner && owner.ActiveBudget == this) owner.ActiveBudget = null;
        }
    }
}
