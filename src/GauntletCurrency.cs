using System;
using RoR2;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal static class GauntletCurrency
    {
        internal const string KeepGoldChoice="Misc.KeepMoneyBetweenStages.On";
        internal static void Install()
        {
            On.RoR2.Run.SetRuleBook+=SetRules;
            On.RoR2.SceneExitController.SetState+=ExitState;
        }
        private static void SetRules(On.RoR2.Run.orig_SetRuleBook original,Run run,RuleBook incoming)
        {
            if(NetworkServer.active && GauntletMode.IsActive(run) && incoming!=null)
            {
                // Never edit a shared lobby rulebook or mutate the live book
                // behind NetworkRuleBook's dirty-bit/update-event mechanism.
                var corrected=new RuleBook();corrected.Copy(incoming);
                var choice=RuleCatalog.FindChoiceDef(KeepGoldChoice);
                if(choice==null)throw new InvalidOperationException("Native keep-gold rule is unavailable.");
                corrected.ApplyChoice(choice);original(run,corrected);return;
            }
            original(run,incoming);
        }
        internal static void Ensure(Run run)
        {
            if(NetworkServer.active && GauntletMode.IsActive(run) && run.ruleBook!=null && !run.ruleBook.keepMoneyBetweenStages)
                run.SetRuleBook(run.ruleBook);
        }
        private static void ExitState(On.RoR2.SceneExitController.orig_SetState original,SceneExitController exit,SceneExitController.ExitState next)
        {
            var owner=GauntletController.Current;
            if(GauntletCurrencyPolicy.BlockExtraction(owner!=null,NetworkServer.active,owner && owner.CommittedExit==exit,next==SceneExitController.ExitState.ExtractExp))
                next=SceneExitController.ExitState.TeleportOut;
            original(exit,next);
        }
    }
}
