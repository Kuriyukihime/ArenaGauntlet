using System;
using System.Linq;
using Mono.Cecil;

internal static class DropContracts
{
    internal static void Run(TypeDefinition[] plugin,Action<bool,string> check)
    {
        var loot=plugin.Single(t=>t.Name=="GauntletLoot");
        var install=loot.Methods.Single(m=>m.Name=="Install").Body.Instructions;
        check(!install.Any(i=>i.Operand is MethodReference m && m.Name=="add_onDeathProcServer"),"The broad death callback cannot add a second horde drop roll");
        var wave=plugin.Single(t=>t.Name=="GauntletWaveBudget");
        var death=wave.Methods.Single(m=>m.Name=="Defeated").Body.Instructions;
        int accepted=death.ToList().FindIndex(i=>i.Operand is MethodReference m && m.Name=="Defeat");
        int award=death.ToList().FindIndex(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GauntletLoot" && m.Name=="EnemyDied");
        check(accepted>=0 && award>accepted,"Only an accepted quota death can draw from the item budget");
        check(wave.Methods.Single(m=>m.Name=="Commit").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name==".ctor" && m.DeclaringType.Name=="GauntletDropBudget"),"Each committed horde owns a fresh finite item budget");
        var killed=loot.Methods.Single(m=>m.Name=="EnemyDied").Body.Instructions;
        check(killed.Count(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GauntletDropBudget" && m.Name=="Roll")==1,"Horde deaths roll against one round-owned loot budget");
        check(killed.Any(i=>i.Operand is MethodReference m && m.Name=="get_isElite"),"Elite status gives a modest preference within the same budget");
        check(killed.Any(i=>i.Operand is MethodReference m && m.Name=="GetExpAdjustedDropChancePercent"),"Champion quota deaths retain their existing native bonus-roll calculation");
        check(loot.Methods.Single(m=>m.Name=="NativeSacrifice").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="IsActive"),"The separate native Sacrifice roll remains suppressed in this mode");
        bool Calls(Mono.Collections.Generic.Collection<Mono.Cecil.Cil.Instruction> body,string type,string name)=>body.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name==type && m.Name==name);
        var bossRewards=loot.Methods.Single(m=>m.Name=="BossRewards").Body.Instructions;
        check(Calls(killed,"GauntletLootInstancing","Drop") && Calls(killed,"GauntletLootInstancing","NextPlayer") && !Calls(killed,"PickupDropletController","CreatePickupDroplet"),"Horde and champion drops each belong to the next player in turn");
        check(Calls(bossRewards,"GauntletLootInstancing","Drop") && Calls(bossRewards,"PlayerCharacterMasterController","get_master") && !Calls(bossRewards,"PickupDropletController","CreatePickupDroplet"),"Each clear reward belongs to the player it was rolled for");
        var instanced=plugin.Single(t=>t.Name=="GauntletLootInstancing").Methods.Single(m=>m.Name=="Install").Body.Instructions;
        foreach(var (type,hook) in new[]{("GenericPickupController","add_AttemptGrant"),("GenericPickupController","add_GetInteractability"),("PickupPickerController","add_GetInteractability"),("PickupPickerController","add_OnInteractionBegin"),("PickupDropletController","add_CreatePickup")})
            check(Calls(instanced,type,hook),"Personal loot hooks the native path: "+type+"."+hook);
        var portal=plugin.Single(t=>t.Name=="GauntletPortalLoot");
        check(Calls(portal.Methods.Single(m=>m.Name=="Collect" && m.Parameters.Count==1).Body.Instructions,"GauntletLootInstancing","OwnerOf"),"Carried rewards remember their owner");
        check(plugin.Where(t=>t.Name.StartsWith("<Restore>d__") || t.Name.StartsWith("<>c__DisplayClass")).SelectMany(t=>t.Methods).Where(m=>m.HasBody).Any(m=>Calls(m.Body.Instructions,"GauntletLootInstancing","Claim")),"Restored rewards return to their owner");
    }
}
