using System;
using System.Linq;
using Mono.Cecil;

internal static class StarterContracts
{
    internal static void Run(ModuleDefinition game,TypeDefinition[] plugin,Action<bool,string> check)
    {
        var starters=plugin.Single(t=>t.Name=="GauntletStartingItems");
        var tick=starters.Methods.Single(m=>m.Name=="Tick").Body.Instructions;
        foreach(string name in new[]{"get_active","get_Current","get_isParticipating","get_inventoryDisabled"})
            check(tick.Any(i=>i.Operand is MethodReference m && m.Name==name),"Starter items require server, active mode and a participating ready inventory: "+name);
        check(starters.Fields.Single(f=>f.Name=="plan").FieldType.FullName.Contains("RoR2.NetworkUserId"),"Starter entitlements follow native account/subplayer identity across respawns and reconnects");
        var id=game.Types.Single(t=>t.FullName=="RoR2.NetworkUserId");
        check(id.Fields.Any(f=>f.Name=="subId") && id.Methods.Any(m=>m.Name=="Equals" && m.Parameters.Count==1),"Native starter identity includes distinct local subplayers");
        check(!starters.Fields.Any(f=>f.IsStatic),"Starter RNG and granted counts cannot leak between runs");
        check(!starters.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m && m.Name=="get_treasureRng" || i.Operand is FieldReference f && (f.Name=="treasureRng" || f.Name=="ArenaRng")),"Starter rolls use an isolated RNG instead of changing shop/enemy loot sequences");
        int give=tick.ToList().FindIndex(i=>i.Operand is MethodReference m && m.Name=="GiveItemPermanent");
        int notify=tick.ToList().FindIndex(i=>i.Operand is MethodReference m && m.Name=="SendPickupMessage");
        check(give>=0 && notify>give && tick.Take(notify).Any(i=>i.Operand is MethodReference m && m.Name=="Confirm"),"Native inventory grants commit before pickup notifications so UI failures cannot duplicate items");
        var all=starters.Methods.Concat(starters.NestedTypes.SelectMany(t=>t.Methods)).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).ToArray();
        foreach(string name in new[]{"availableTier1DropList","availableTier2DropList"})
            check(all.Any(i=>i.Operand is FieldReference f && f.Name==name || i.Operand is MethodReference m && m.Name=="get_"+name),"Starter items come from native eligible run pools: "+name);
        check(!all.Any(i=>i.Operand is FieldReference f && (f.Name=="availableTier3DropList" || f.Name=="availableLunarItemDropList" || f.Name=="availableEquipmentDropList")),"No red, lunar or equipment pool can supply starting items");
        check(all.Any(i=>i.Operand is MethodReference m && m.Name=="IsItemDisabled"),"Disabled items are excluded from starter stock");
        var fortress=plugin.Single(t=>t.Name=="FortressController");
        var update=fortress.Methods.Single(m=>m.Name=="FixedUpdate").Body.Instructions;
        int grant=update.ToList().FindIndex(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GauntletStartingItems" && m.Name=="Tick");
        int combat=update.ToList().FindIndex(i=>i.Operand is MethodReference m && m.Name=="BeginRound");
        check(grant>=0 && combat>grant,"Starter grants run before the automatic combat-start gate");
        var native=game.Types.Single(t=>t.Name=="GenericPickupController");
        check(native.Methods.Any(m=>m.Name=="SendPickupMessage" && m.IsStatic && m.IsPublic && m.Parameters.Count==3 && m.Parameters[1].ParameterType.Name=="UniquePickup"),"Native pickup notifications support the installed UniquePickup API");
        check(tick.Any(i=>Equals(i.Operand,"Starting items have been granted!")) && tick.Any(i=>i.Operand is MethodReference m && m.Name=="Announce" && m.DeclaringType.Name=="GauntletPortals"),"Starter completion uses the exact requested text through the established arena notification path");
        check(tick.Any(i=>i.Operand is MethodReference m && m.Name=="AllParticipatingNetworkUsersReady"),"Starter completion waits for participating clients to be ready");
    }
}
