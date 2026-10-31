using System;
using RoR2;
using RoR2.Artifacts;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal static class GauntletLoot
    {
        private static PickupDropTable normalChest;
        private static BasicPickupDropTable greenReward;
        internal static void Install()
        {
            On.RoR2.Artifacts.SacrificeArtifactManager.OnServerCharacterDeath += NativeSacrifice;
        }
        private static void NativeSacrifice(On.RoR2.Artifacts.SacrificeArtifactManager.orig_OnServerCharacterDeath original, DamageReport report)
        {
            // The mode owns exactly one Sacrifice-style roll. Other modes and
            // other independent survivor/item drop effects are untouched.
            if (!GauntletMode.IsActive(Run.instance)) original(report);
        }
        private static void EnsureTables(Run run)
        {
            if (!normalChest)
            {
                var chest = Addressables.LoadAssetAsync<GameObject>(RoR2BepInExPack.GameAssetPathsBetter.RoR2_Base_Chest1.Chest1_prefab).WaitForCompletion();
                if (chest) normalChest = NativeAccess.Get<PickupDropTable>(chest.GetComponent<ChestBehavior>(), "dropTable");
                if (!normalChest) throw new InvalidOperationException("The native small-chest loot table is unavailable.");
            }
            if (!greenReward)
            {
                greenReward = ScriptableObject.CreateInstance<BasicPickupDropTable>();
                greenReward.name = "BazaarGauntletBossGreen";
                greenReward.tier1Weight = 0;greenReward.tier2Weight = 1;greenReward.tier3Weight = 0;
            }
            if (normalChest is BasicPickupDropTable basic) basic.RegenerateDropTable(run);
            greenReward.RegenerateDropTable(run);
        }
        internal static void PrepareRun(Run run) => EnsureTables(run);
        internal static UniquePickup ShopItem(Run run, bool green)
        {
            EnsureTables(run);
            return (green ? (PickupDropTable)greenReward : normalChest).GeneratePickup(run.treasureRng);
        }
        internal static void EnemyDied(GauntletController owner,CharacterMaster victim,DamageReport report,bool boss,GauntletDropBudget drops)
        {
            // Called only after the committed quota ledger accepts this death.
            // Duplicate reports, despawns and extra summons cannot farm loot.
            if (!owner || !NetworkServer.active || report == null || report.victimMaster!=victim || !victim || !report.victimBody) return;
            if (report.victimTeamIndex == TeamIndex.Player ||
                (report.attackerTeamIndex == report.victimTeamIndex && report.victimMaster.minionOwnership.ownerMaster)) return;
            if (boss && victim==owner.BossMaster)
            {
                var rewards = report.victimBody.GetComponent<DeathRewards>();
                if (rewards) owner.LastBossDropTable = rewards.bossDropTable;
            }
            if(boss)
            {
                float chance=Util.GetExpAdjustedDropChancePercent(5f,report.victim.gameObject)*owner.DropMultiplier;
                if(float.IsNaN(chance) || chance<=0 || !Util.CheckRoll(Mathf.Min(100,chance)))return;
            }
            else if(drops==null || !drops.Roll(report.victimBody.isElite,owner.Run.treasureRng.nextNormalizedFloat))return;
            if (!normalChest) EnsureTables(owner.Run);
            UniquePickup pickup = normalChest.GeneratePickup(owner.Run.treasureRng);
            // Each drop belongs to one player in turn; the budget already scales with party size.
            if (pickup.isValid) owner.Loot.Drop(pickup, report.victimBody.corePosition, owner.Loot.NextPlayer());
        }
        internal static void BossRewards(GauntletController owner, Vector3 position)
        {
            if (!NetworkServer.active) return;
            EnsureTables(owner.Run);
            foreach (var player in PlayerCharacterMasterController.instances)
            {
                if (!player.isConnected) continue;
                // Match the requested 24% yellow substitution when a
                // boss has a valid special table; otherwise grant one green.
                PickupDropTable fallback = owner.Flow.BossKind == GauntletBossKind.Ordinary ? normalChest : greenReward;
                var table = owner.LastBossDropTable && owner.Run.treasureRng.nextNormalizedFloat < GauntletBalance.BossItemChance ? owner.LastBossDropTable : fallback;
                UniquePickup pickup = table.GeneratePickup(owner.Run.treasureRng);
                if (pickup.isValid) owner.Loot.Drop(pickup, position, player.master);
            }
            owner.LastBossDropTable = null;
        }
    }
}
