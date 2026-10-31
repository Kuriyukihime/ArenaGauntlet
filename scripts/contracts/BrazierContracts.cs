using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrazierContracts
{
    internal static void Run(ModuleDefinition game,TypeDefinition[] plugin,Action<bool,string> check)
    {
        var native=game.Types.Single(t=>t.FullName=="RoR2.PositionIndicator");
        foreach(string name in new[]{"targetTransform","insideViewObject","outsideViewObject","shouldRotateOutsideViewObject"})
            check(native.Fields.Any(f=>f.Name==name && f.IsPublic),"Installed direction indicator field: "+name);
        foreach(var pair in new[]{("OnEnable","Add"),("OnDisable","Remove")})
            check(native.Methods.Single(m=>m.Name==pair.Item1).Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name==pair.Item2),"Native marker registry lifecycle: "+pair.Item1);
        var positions=native.Methods.Single(m=>m.Name=="UpdatePositions").Body.Instructions;
        check(positions.Any(i=>i.Operand is MethodReference m && m.Name=="WorldToScreenPoint") && positions.Any(i=>i.Operand is FieldReference f && f.Name=="outsideViewRotationOffset"),"Native marker projects world targets and rotates off-screen arrows");
        var hud=plugin.Single(t=>t.Name=="GauntletHud");
        var build=hud.Methods.Single(m=>m.Name=="BuildBrazierArrow").Body.Instructions;
        check(build.Any(i=>i.Operand is GenericInstanceMethod m && m.Name=="AddComponent" && m.GenericArguments.Any(t=>t.Name=="PositionIndicator")),"Brazier uses native screen and off-screen positioning");
        check(build.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GauntletShop" && m.Name=="Position"),"Marker uses the same world position as the ready interaction");
        check(build.Any(i=>i.Operand is MethodReference m && m.Name=="set_layer"),"Direction arrow inherits the visible HUD layer");
        var update=hud.Methods.Single(m=>m.Name=="Update").Body.Instructions;
        check(update.Any(i=>i.Operand is MethodReference m && m.Name=="get_RemotePhase") && update.Any(i=>i.Operand is MethodReference m && m.Name=="get_InCastle"),"Client marker follows authoritative phase in the arena");
        var cleanup=hud.Methods.Single(m=>m.Name=="OnDestroy").Body.Instructions;
        foreach(string name in new[]{"brazierArrow","brazierTarget"})
            check(cleanup.Any(i=>i.Operand is FieldReference f && f.Name==name) && cleanup.Any(i=>i.Operand is MethodReference m && m.Name=="Destroy"),"HUD destroys its marker resource: "+name);
        var visuals=plugin.Single(t=>t.Name=="GauntletShopVisuals");
        check(!visuals.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is TypeReference t && t.Namespace=="TMPro" || i.Operand is MethodReference m && m.DeclaringType.Namespace=="TMPro"),"Merchant scenery creates no overhead text");
        int AddPosition(string type)=>build.ToList().FindIndex(i=>i.Operand is GenericInstanceMethod m && m.Name=="AddComponent" && m.GenericArguments.Any(t=>t.Name==type));
        check(AddPosition("GauntletHudMarker")>AddPosition("PositionIndicator"),"Canvas correction must be registered after native marker positioning");
        var marker=plugin.Single(t=>t.Name=="GauntletHudMarker");
        var place=marker.Methods.Single(m=>m.Name=="PlaceOnCanvas").Body.Instructions;
        foreach(string name in new[]{"WorldToScreenPoint","ScreenPointToWorldPointInRectangle","get_parent","set_position"})
            check(place.Any(i=>i.Operand is MethodReference m && m.Name==name),"Marker must preserve the native screen location on its actual HUD plane: "+name);
        check(place.Count(i=>i.Operand is FieldReference f && f.Name=="cameraRigController" || i.Operand is MethodReference m && m.Name=="get_cameraRigController")==2,"Canvas correction must belong to this HUD's camera rig, including split screen");
        foreach(var pair in new[]{("OnEnable","add_onUICameraPreCull"),("OnDisable","remove_onUICameraPreCull")})
            check(marker.Methods.Single(m=>m.Name==pair.Item1).Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name==pair.Item2),"Camera correction subscription lifecycle: "+pair.Item1);
        var ui=game.Types.Single(t=>t.FullName=="RoR2.UICamera");
        check(ui.Events.Any(e=>e.Name=="onUICameraPreCull"),"Native UI camera pre-cull event exists");
    }
}
