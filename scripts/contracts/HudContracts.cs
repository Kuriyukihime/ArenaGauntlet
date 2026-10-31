using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class HudContracts
{
    internal static void Run(ModuleDefinition game,TypeDefinition[] plugin,Action<bool,string> check)
    {
        var register=plugin.Single(t=>t.Name=="GauntletMode").Methods.Single(m=>m.Name=="Register").Body.Instructions;
        var assignment=register.Single(i=>i.OpCode.Code==Code.Stfld && i.Operand is FieldReference f && f.DeclaringType.Name=="Run" && f.Name=="uiPrefab");
        check(assignment.Previous.Operand is GenericInstanceMethod load && load.Name=="Load" && load.DeclaringType.Name=="FortressAssets" &&
            load.GenericArguments.Single().Name=="GameObject" && Equals(assignment.Previous.Previous.Operand,"ClassicRunInfoHudPanel_prefab"),
            "The compiled mode must supply native ClassicRunInfoHudPanel, never null or an empty custom placeholder");
        check(register.Any(i=>Equals(i.Operand,"Native run HUD prefab could not be loaded.")),"Missing UI assets must fail explicitly during registration, not break the entire HUD later");
        var instantiate=game.Types.Single(t=>t.FullName=="RoR2.Run").Methods.Single(m=>m.Name=="InstantiateUi").Body.Instructions;
        check(instantiate.Any(i=>i.Operand is FieldReference f && f.Name=="uiPrefab") && instantiate.Any(i=>i.Operand is MethodReference m && m.Name=="Instantiate"),
            "The installed native Run creates its UI from the assigned prefab");
        var awake=game.Types.Single(t=>t.FullName=="RoR2.UI.HUD").Methods.Single(m=>m.Name=="Awake").Body.Instructions.ToList();
        int hide=awake.FindIndex(i=>i.Operand is MethodReference m && m.Name=="SetActive" && i.Previous.OpCode.Code==Code.Ldc_I4_0 &&
            i.Previous.Previous.Operand is FieldReference f && f.Name=="mainUIPanel");
        int create=awake.FindIndex(i=>i.Operand is MethodReference m && m.Name=="InstantiateUi");
        check(hide>=0 && create>hide,"Regression cause: HUD.Awake hides native UI before calling extensible game-mode UI creation");
        var lateJoin=game.Types.Single(t=>t.FullName=="RoR2.InfiniteTowerRun").Methods.Single(m=>m.Name=="InstantiateUi").Body.Instructions;
        check(lateJoin.Any(i=>i.Operand is MethodReference m && m.Name=="get_transform"),"Native in-wave HUD creation also needs a non-null run UI root for late joiners");
        var configure=plugin.Single(t=>t.Name=="GauntletWaves").Methods.Single(m=>m.Name=="ConfigurePrefab").Body.Instructions;
        var overlays=configure.SingleOrDefault(i=>i.OpCode.Code==Code.Newarr && i.Operand is TypeReference t && t.Name=="OverlayEntry");
        check(overlays!=null && overlays.Previous.OpCode.Code==Code.Ldc_I4_2,
            "Gauntlet waves keep two inert overlay entries: WolfoQoL reads overlayEntries[1] after every wave InstantiateUi");
        foreach(string type in new[]{"GauntletHud","GauntletCareerMenu"})
            check(plugin.Single(t=>t.Name==type).Methods.Single(m=>m.Name=="Label").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="set_enableAutoSizing"),
                "Mod labels shrink to fit instead of cutting text to an ellipsis: "+type);
        var waves=plugin.Single(t=>t.Name=="GauntletWaves");
        foreach(string hook in new[]{"ActiveWard","WaitingWard"})
            check(waves.Methods.Single(m=>m.Name==hook).Body.Instructions.Any(i=>i.OpCode.Code==Code.Stfld && i.Operand is FieldReference f && f.Name=="objectiveToken"),
                "The hidden ward must not show the native Simulacrum objective (\"Activate the Focus\" / \"Survive..\"): "+hook);
        check(plugin.Single(t=>t.Name=="GauntletController").Methods.Single(m=>m.Name=="Awake").Body.Instructions
            .Any(i=>i.Operand is GenericInstanceMethod g && g.Name=="AddComponent" && g.GenericArguments.Any(a=>a.Name=="GauntletObjective")),"The run reports the arena phase as its objective");
        check(!plugin.SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions)
            .Any(i=>i.Operand is MethodReference m && m.DeclaringType.FullName.StartsWith("WolfoQoL_Client.",StringComparison.Ordinal)),
            "HUD compatibility uses native run UI instead of adding a Wolfo dependency or overriding its hooks");
    }
}
