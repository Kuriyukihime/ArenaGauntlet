using RoR2;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal static class GauntletRuntime
    {
        internal static void Install()
        {
            GauntletCurrency.Install();
            GauntletEconomy.Install();
            GauntletExperience.Install();
            GauntletRounds.Install();
            GauntletArena.Install();
            GauntletWaves.Install();
            GauntletLoot.Install();
            GauntletShop.Install();
            GauntletNetwork.Install();
            On.RoR2.InfiniteTowerRun.RecalculateDifficultyCoefficentInternal += Recalculate;
            On.RoR2.InfiniteTowerRun.MoveSafeWard += MoveWard;
            On.RoR2.InfiniteTowerWaveController.DropRewards += DropWaveRewards;
            On.RoR2.Run.GetEffectiveRunStopwatch += DifficultyClock;
            On.RoR2.Run.BeginGameOver+=GameOver;
            On.RoR2.InfiniteTowerSafeWardController.Activate+=Activate;
            On.RoR2.HealthComponent.TakeDamageProcess+=StructureDamage;
        }
        private static void StructureDamage(On.RoR2.HealthComponent.orig_TakeDamageProcess original,HealthComponent health,DamageInfo damage)
        {
            var owner=GauntletController.Current;
            if(owner && health.GetComponent<FortressBodyMarker>() && (!owner.Fortress.IsCombat || damage.attacker && TeamComponent.GetObjectTeam(damage.attacker)==TeamIndex.Player))return;
            original(health,damage);
        }
        private static void Activate(On.RoR2.InfiniteTowerSafeWardController.orig_Activate original,InfiniteTowerSafeWardController ward)
        {if(!GauntletController.Current)original(ward);}
        private static void GameOver(On.RoR2.Run.orig_BeginGameOver original,Run run,GameEndingDef ending)
        {
            var owner=GauntletMode.IsActive(run)?run.GetComponent<GauntletController>():null;
            if(owner && !ending.isWin)owner.Flow.Lose();
            original(run,ending);
        }
        private static float DifficultyClock(On.RoR2.Run.orig_GetEffectiveRunStopwatch original, Run run)
        {
            float seconds = original(run);
            var owner = GauntletMode.IsActive(run) ? run.GetComponent<GauntletController>() : null;
            if (!owner) return seconds;
            float selected = DifficultyCatalog.GetDifficultyDef(run.selectedDifficulty).scalingValue;
            return seconds * GauntletBalance.ClockMultiplier(selected, owner.MinimumScaling);
        }
        private static void Recalculate(On.RoR2.InfiniteTowerRun.orig_RecalculateDifficultyCoefficentInternal original, InfiniteTowerRun run)
        {
            if (!GauntletMode.IsActive(run)) { original(run); return; }
            int previousLevel = run.ambientLevelFloor;
            NativeAccess.BaseDifficulty(run);
            run.GetComponent<GauntletController>()?.UpdateLevelFloor(previousLevel);
        }
        private static void MoveWard(On.RoR2.InfiniteTowerRun.orig_MoveSafeWard original, InfiniteTowerRun run)
        {
            if (!GauntletMode.IsActive(run)) original(run);
        }
        private static void DropWaveRewards(On.RoR2.InfiniteTowerWaveController.orig_DropRewards original, InfiniteTowerWaveController wave)
        {
            if (!GauntletMode.IsActive(Run.instance)) original(wave);
        }
    }
}
