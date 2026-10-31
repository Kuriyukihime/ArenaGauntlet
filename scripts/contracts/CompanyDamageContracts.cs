using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class CompanyDamageContracts
{
    internal static void Run(ModuleDefinition game,TypeDefinition[] plugin,string profile,string[] dependencies,Action<bool,string> check)
    {
        var runtime=plugin.Single(t=>t.Name=="GauntletCompanyDamage");
        var register=runtime.Methods.Single(m=>m.Name=="Register").Body.Instructions;
        var apply=runtime.Methods.Single(m=>m.Name=="Apply").Body.Instructions;
        var stats=runtime.Methods.Single(m=>m.Name=="Recalculate").Body.Instructions;
        check(dependencies.Contains("RiskofThunder-R2API_RecalculateStats-1.7.0"),"The launcher installs the stats API used by the targeted company reduction");
        var pluginMain=plugin.Single(t=>t.Name=="GauntletPlugin");
        check(pluginMain.CustomAttributes.Any(a=>a.AttributeType.Name=="BepInDependency" && Equals(a.ConstructorArguments[0].Value,"com.bepis.r2api.recalculatestats")),"BepInEx orders the required stats API before company balance registration");
        check(pluginMain.Methods.Single(m=>m.Name=="Awake").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name==runtime.Name && m.Name=="Register"),"Every peer registers the same marker and stats hook at startup");
        check(register.Any(i=>i.Operand is MethodReference m && m.Name=="AddItemDef"),"The hidden marker belongs to native synchronized content");
        foreach(string field in new[]{"hidden","canRemove"})
            check(register.Any(i=>i.OpCode.Code==Code.Stfld && i.Operand is FieldReference f && f.Name==field),"The internal marker is hidden and non-removable: "+field);
        check(register.Any(i=>Equals(i.Operand,"deprecatedTier")) && game.Types.Single(t=>t.FullName=="RoR2.ItemDef").Fields.Any(f=>f.Name=="deprecatedTier" && f.FieldType.FullName=="RoR2.ItemTier"),"Pre-catalog NoTier registration uses the installed native fallback field");
        check(register.Any(i=>i.Operand is MethodReference m && m.Name=="add_GetStatCoefficients"),"Damage follows the supported stats API instead of replacing attacks or prefabs");
        foreach(string call in new[]{"get_active","ProfileFor","Stamp"})
            check(apply.Any(i=>i.Operand is MethodReference m && m.Name==call),"Only the host stamps company role profiles: "+call);
        var stamp=runtime.Methods.Single(m=>m.Name=="Stamp").Body.Instructions;
        foreach(string call in new[]{"get_active","Encode","GetItemCountPermanent","GiveItemPermanent","RemoveItemPermanent"})
            check(stamp.Any(i=>i.Operand is MethodReference m && m.Name==call),"A native inventory count replaces the old profile instead of stacking: "+call);
        var companies=plugin.Single(t=>t.Name=="GauntletCompanies");
        check(companies.Methods.Single(m=>m.Name=="Spawn").Body.Instructions.Count(i=>i.Operand is MethodReference m && m.DeclaringType.Name==runtime.Name && m.Name=="Apply")==1,"The existing company spawn path applies the marker exactly once");
        foreach(string call in new[]{"IsActive","get_teamIndex","GetItemCountPermanent","Multiplier"})
            check(stats.Any(i=>i.Operand is MethodReference m && m.Name==call),"The damage hook requires active arena/player-team/native marker state: "+call);
        check(!stats.Any(i=>i.Operand is MethodReference m && m.Name=="get_active" && m.DeclaringType.Name=="NetworkServer"),"Remote clients and late joiners recalculate damage from synchronized inventory too");
        var writes=stats.Where(i=>i.OpCode.Code==Code.Stfld).Select(i=>(FieldReference)i.Operand).ToArray();
        check(writes.Length==1 && writes[0].Name=="damageTotalMult", "The stat hook changes damage only, preserving health, armor, speed and attack cadence");
        check(register.Any(i=>i.Operand is MethodReference m && m.Name=="add_AddDeployable"),"Native Solus summons receive the same company damage budget");
        check(runtime.Methods.Single(m=>m.Name=="Deploy").Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name=="Commander"),"Only a hired Solus commander can mark its extra native Probes");
        check(companies.Methods.Single(m=>m.Name=="RefreshCapacity").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="RefreshAll"),"Party-size changes refresh existing company and native-Probe profiles");
        check(apply.Any(i=>i.OpCode.Code==Code.Stfld && i.Operand is FieldReference f && f.Name=="maxUserHealthFraction"),"The friendly Titan laser can be selected without first losing health");
        string path=Path.Combine(profile,"BepInEx/plugins/RiskofThunder-R2API_RecalculateStats/R2API.RecalculateStats/R2API.RecalculateStats.dll");
        using var api=ModuleDefinition.ReadModule(path);
        var args=api.Types.Single(t=>t.Name=="RecalculateStatsAPI").NestedTypes.Single(t=>t.Name=="StatHookEventArgs");
        var ctor=args.Methods.Single(m=>m.IsConstructor && !m.IsStatic).Body.Instructions;
        int initializer=ctor.ToList().FindIndex(i=>i.OpCode.Code==Code.Stfld && i.Operand is FieldReference f && f.Name=="damageTotalMult");
        check(initializer>0 && ctor[initializer-1].OpCode.Code==Code.Ldc_R4 && (float)ctor[initializer-1].Operand==1f,"Native stats API starts each recalculation at one, preventing cumulative reductions");
    }
}
