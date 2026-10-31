using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class ExperienceContracts
{
    internal static void Run(ModuleDefinition game,TypeDefinition[] plugin,Action<bool,string> check)
    {
        var nativeAward=game.Types.Single(t=>t.Name=="ExperienceManager").Methods.Single(m=>m.Name=="AwardExperience");
        check(nativeAward.ReturnType.FullName=="System.Void" && nativeAward.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"UnityEngine.Vector3","RoR2.CharacterBody","System.UInt64"}),"XP detour matches the installed native final-award signature");
        var death=game.Types.Single(t=>t.Name=="DeathRewards").Methods.Single(m=>m.Name=="OnKilledServer");
        check(death.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="ExperienceManager" && m.Name=="AwardExperience"),"Native death XP must pass through the scoped award hook after level scaling");
        var xp=plugin.Single(t=>t.Name=="GauntletExperience");
        var killed=xp.Methods.Single(m=>m.Name=="EnemyKilled");var award=xp.Methods.Single(m=>m.Name=="Award");
        check(killed.Body.ExceptionHandlers.Any(h=>h.HandlerType==ExceptionHandlerType.Finally),"Nested/throwing death callbacks must restore the prior XP scope");
        foreach(string guard in new[]{"get_active","get_Current","get_master","get_teamIndex"})
            check(award.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name==guard),"Final XP boost requires server, active mode and player-team recipient: "+guard);
        check(award.Body.Instructions.Count(i=>i.Operand is MethodReference m && m.Name=="Scale")==1 && award.Body.Instructions.Count(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="orig_AwardExperience" && m.Name=="Invoke")==1,"Each native final award is boosted and forwarded exactly once");
        check(!xp.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m && (m.Name=="set_expReward" || m.Name=="set_spawnValue") || i.Operand is FieldReference f && f.Name=="expRewardCoefficient"),"XP boost must not change spawnValue or director coefficients used by item drops");
        var ledger=plugin.Single(t=>t.Name=="GauntletController").Fields.Single(f=>f.Name=="Experience");
        check(!ledger.IsStatic && ledger.IsInitOnly && ledger.FieldType.Name=="GauntletExperiencePolicy","Fractional XP belongs to one run, not static state shared across restarts");
    }
}
