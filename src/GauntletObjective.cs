using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    // The reused Simulacrum ward reports "Activate the Focus" / "Survive.."; this mode shows its own phase instead.
    internal sealed class GauntletObjective:MonoBehaviour
    {
        private const string Prepare="GAUNTLET_OBJECTIVE_PREPARE",Horde="GAUNTLET_OBJECTIVE_HORDE",Champion="GAUNTLET_OBJECTIVE_CHAMPION",
            Champions="GAUNTLET_OBJECTIVE_CHAMPIONS",Shop="GAUNTLET_OBJECTIVE_SHOP",Victory="GAUNTLET_OBJECTIVE_VICTORY";
        private GauntletController owner;
        private GenericObjectiveProvider provider;
        internal static void Register()
        {
            LanguageAPI.Add(Prepare,"Prepare for battle");
            LanguageAPI.Add(Horde,"Defeat the horde");
            LanguageAPI.Add(Champion,"Defeat the champion");
            LanguageAPI.Add(Champions,"Defeat the champions");
            LanguageAPI.Add(Shop,"Shop, then ready up at the brazier");
            LanguageAPI.Add(Victory,"Victory!");
        }
        private void Awake()
        {
            owner=GetComponent<GauntletController>();
            // Set the token before the objective panel's next read; a null token throws in Language.GetString.
            provider=gameObject.AddComponent<GenericObjectiveProvider>();provider.objectiveToken=Prepare;provider.markCompletedOnRetired=false;
        }
        private void Update()
        {
            bool host=NetworkServer.active;
            var phase=host?owner.Flow.Phase:owner.RemotePhase;int round=host?owner.Flow.Round:owner.RemoteRound;
            bool fighting=!owner.Fortress.WaitingForStart && (phase==GauntletPhase.Horde || phase==GauntletPhase.Boss);
            bool several=round>=1 && round<=GauntletPlan.RoundsPerCycle &&
                GauntletMetaPolicy.Bosses(GauntletPlan.BossCount(round),GauntletPlan.BossAt(round)==GauntletBossKind.Ordinary,owner.Meta.Ascension)>1;
            string token=phase==GauntletPhase.AwaitingExit?Shop:phase==GauntletPhase.FinalDecision || phase==GauntletPhase.Won?Victory:
                !fighting?Prepare:phase==GauntletPhase.Horde?Horde:several?Champions:Champion;
            provider.enabled=phase!=GauntletPhase.Lost;
            if(provider.objectiveToken!=token)provider.objectiveToken=token;
        }
    }
}
