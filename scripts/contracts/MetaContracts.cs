using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MetaContracts
{
    internal static void Run(ModuleDefinition game,TypeDefinition[] plugin,Action<bool,string> check)
    {
        TypeDefinition Type(string name)=>plugin.Single(t=>t.Name==name);
        bool Calls(TypeDefinition t,string method,string target)=>t.Methods.Single(m=>m.Name==method).Body.Instructions.Any(i=>i.Operand is MethodReference r && r.Name==target);
        var menu=Type("GauntletCareerMenu");var network=Type("GauntletMetaNetwork");var runtime=Type("GauntletMetaprogression");
        check(Calls(menu,"Attach","get_Selecting") && Calls(menu,"Update","get_Selecting"),"Career attachment and ongoing visibility both require the active arena selection screen");
        check(menu.Methods.Single(m=>m.Name=="Attach").Body.Instructions.Any(i=>i.Operand is GenericInstanceMethod m && m.GenericArguments.Any(a=>a.Name=="CharacterSelectController")),"Career menu attaches only to native survivor-selection screens");
        var selecting=network.Methods.Single(m=>m.Name=="get_Selecting").Body.Instructions;
        foreach(string call in new[]{"get_instance","get_gameModeIndex","GetGameModePrefabComponent","IsActive"})check(selecting.Any(i=>i.Operand is MethodReference m && m.Name==call),"Selection visibility checks native lobby mode identity: "+call);
        check(!Type("GauntletHud").Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name==menu.Name),"Combat HUD cannot open the metaprogression menu");
        var request=network.Methods.Single(m=>m.Name=="ReadRequest").Body.Instructions;
        foreach(string call in new[]{"get_active","get_Length","Take","ReadMessage"})check(request.Any(i=>i.Operand is MethodReference m && m.Name==call),"Career requests bound and validate work before application: "+call);
        check(request.Any(i=>i.Operand is FieldReference f && f.Name=="isReady"),"Career requests require a scene-ready sending connection");
        var all=network.Methods.Concat(network.NestedTypes.SelectMany(t=>t.Methods)).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).ToArray();
        check(all.Any(i=>i.Operand is MethodReference m && m.Name=="get_connectionToClient") && all.Any(i=>i.Operand is FieldReference f && f.Name=="User"),"Client career claims must belong to the actual sending connection's network user");
        check(Calls(network,"Accept","MatchesRun") && Calls(network,"Apply","MatchesRun"),"Both directions reject career packets from a prior run, even with the same seed");
        check(Calls(network,"IsHost","get_active") && Calls(network,"IsHost","GetFirstLocalUser") && Calls(network,"Submit","IsHost") && Calls(menu,"Challenges","IsHost"),"Only the primary local host profile may select Ascension; split-screen partners and remote clients cannot overwrite it");
        var capture=runtime.Methods.Single(m=>m.Name=="Capture").Body.Instructions;
        check(capture.Any(i=>i.Operand is MethodReference m && m.Name=="ContainsKey") && capture.Any(i=>i.Operand is MethodReference m && m.Name=="FromJson"),"Career claims are copied once and frozen through reconnects for a run");
        check(Calls(runtime,"Loadout","Pool") && Calls(runtime,"Loadout","Allowed"),"Host validates selected names against unlocked categories and current run pools");
        check(Calls(Type("GauntletMetaStore"),"Save","Replace") && Calls(Type("GauntletMetaStore"),"Save","WriteAllText"),"Career saves use a temporary UTF-8 file and atomic replacement with backup");
        check(Calls(Type("GauntletMetaStore"),"PathFor","get_persistentDataPath") && !Calls(Type("GauntletMetaStore"),"PathFor","get_ConfigPath"),
            "Careers are saved game-wide, not inside a mod-manager profile, so Glory survives updates and profile switches");
        check(Calls(Type("GauntletMetaStore"),"For","LegacyPaths") && Calls(Type("GauntletMetaStore"),"For","Save"),"Pre-0.37 per-profile careers are imported once into the game-wide save");
        var resets=plugin.SelectMany(t=>t.Methods.Concat(t.NestedTypes.SelectMany(n=>n.Methods))).Where(m=>m.HasBody &&
            m.Body.Instructions.Any(i=>i.Operand is MethodReference r && r.Name=="Reset" && r.DeclaringType.Name=="GauntletMetaStore")).ToArray();
        check(resets.Length==1 && resets[0].Name.Contains("ConfirmReset") && Calls(Type("GauntletMetaStore"),"Reset","Save"),
            "Career reset is reachable only from the menu's verification screen and is saved like any other change");
        check(Calls(runtime,"RoundCleared","RoundGlory") && Calls(Type("FortressController"),"RoundEnded","RoundCleared"),"Glory is awarded by the once-committed round-clear path");
        var purchase=Type("GauntletShop").Methods.Single(m=>m.Name=="Purchase").Body.Instructions;
        int free=purchase.ToList().FindIndex(i=>i.Operand is MethodReference m && m.Name=="Claim"),paid=purchase.ToList().FindIndex(i=>i.Operand is MethodReference m && m.Name=="CanBuy");
        check(free>=0 && paid>free && purchase.Skip(free).Take(paid-free).Any(i=>i.Operand is MethodReference m && m.Name=="GiveItemPermanent") &&
            purchase.Skip(free).Take(paid-free).Any(i=>i.OpCode.Code==Code.Ret) && !purchase.Skip(free).Take(paid-free).Any(i=>i.Operand is MethodReference m && m.Name=="Buy"),
            "Patronage grants before paid-cap enforcement and returns without recording a paid purchase");
        var pool=runtime.Methods.Single(m=>m.Name=="Pool").Body.Instructions;
        foreach(string field in new[]{"availableTier1DropList","availableTier2DropList","availableTier3DropList","availableLunarItemDropList","availableEquipmentDropList","availableLunarEquipmentDropList"})
            check(pool.Any(i=>i.Operand is FieldReference f && f.Name==field),"Career choices support native/modded eligible run category: "+field);
        check(Calls(Type("GauntletAscension"),"Apply","GiveItemPermanent"),"Ascension stats use native synchronized inventory, not host-only body edits");
        check(game.Types.Single(t=>t.Name=="Inventory").Methods.Any(m=>m.IsPublic && m.Name=="SetEquipmentIndex" && m.Parameters.Count==2 && m.Parameters[1].ParameterType.GetElementType().Name=="EquipmentLocation"),"Starting equipment uses the installed non-obsolete native slot API");
        check(!plugin.Where(t=>t.Namespace=="BazaarGauntlet").SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m && m.DeclaringType.FullName.StartsWith("MetaMarket.",StringComparison.Ordinal)),"MetaMarket remains optional and its reward path is untouched");
    }
}
