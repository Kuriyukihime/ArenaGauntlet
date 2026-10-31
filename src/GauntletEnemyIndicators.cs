using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace BazaarGauntlet
{
    internal sealed class GauntletEnemyIndicators:MonoBehaviour
    {
        private InfiniteTowerWaveController wave;
        private CombatSquad squad;
        private readonly HashSet<CharacterBody> marked=new HashSet<CharacterBody>();
        private float nextRefresh;
        internal static void Install()
        {
            On.RoR2.InfiniteTowerWaveController.OnEnable+=Enabled;
            On.RoR2.InfiniteTowerWaveController.RequestIndicatorForMaster+=NativeRequest;
        }
        private static void Enabled(On.RoR2.InfiniteTowerWaveController.orig_OnEnable original,InfiniteTowerWaveController wave)
        {
            original(wave);
            if(GauntletController.Current && !Application.isBatchMode && !wave.GetComponent<GauntletEnemyIndicators>())
                wave.gameObject.AddComponent<GauntletEnemyIndicators>();
        }
        private static void NativeRequest(On.RoR2.InfiniteTowerWaveController.orig_RequestIndicatorForMaster original,InfiniteTowerWaveController wave,CharacterMaster master)
        {
            // Native Simulacrum reveals the entire squad once all credits have
            // been spent. Reveal only the final five in this mode.
            if(!GauntletController.Current)original(wave,master);
        }
        private void Update()
        {
            if(Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+.25f;
            var owner=GauntletController.Current;if(!owner || !owner.Fortress.IsCombat)return;
            if(!wave)wave=GetComponent<InfiniteTowerWaveController>();
            if(!wave || !wave.defaultEnemyIndicatorPrefab || !GauntletPlan.ShouldMarkEnemies(wave.GetSquadCount(),wave.HasFullProgress()))return;
            if(!squad)squad=NativeAccess.Get<CombatSquad>(wave,"combatSquad");
            if(!squad)return;
            foreach(var master in squad.readOnlyMembersList)
            {
                var body=master?master.GetBody():null;
                if(!body || !body.healthComponent || !body.healthComponent.alive || !body.teamComponent || !marked.Add(body))continue;
                body.teamComponent.RequestDefaultIndicator(wave.defaultEnemyIndicatorPrefab);
            }
        }
    }
}
