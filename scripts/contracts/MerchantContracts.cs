using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MerchantContracts
{
    internal static void Run(ModuleDefinition game,TypeDefinition[] plugin,Action<bool,string> check)
    {
        var shop=plugin.Single(t=>t.Name=="GauntletShop");
        var purchase=shop.Methods.Single(m=>m.Name=="Purchase");
        foreach(string call in new[]{"CanShop","get_isConnected","get_alive","get_corePosition","CanBuy","CanBeAffordedByInteractor"})
            check(purchase.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name==call),"Native client purchase must validate host phase, actor, range, repeat budget and currency: "+call);
        check(purchase.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="PurchaseGroup"),"Host purchases must enforce the rarity group across all item displays");
        check(shop.Methods.Where(m=>m.Name.Contains("SyncPurchases") && m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m && m.Name=="PurchaseGroup"),"Client exhaustion masks must use the same shared rarity budget as host purchases");
        check(purchase.Body.ExceptionHandlers.Any(h=>h.HandlerType==ExceptionHandlerType.Finally),"Paid-drop context must unwind after third-party purchase callbacks");
        var clone=shop.Methods.Single(m=>m.Name=="Clone");
        check(clone.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="InstantiateClone" && i.Previous.OpCode.Code==Code.Ldc_I4_1),"Merchant prefabs must be registered on clients, not spawned as unknown native copies");
        var nativeCost=game.Types.Single(t=>t.Name=="CostTypeDef");
        check(game.Types.Single(t=>t.Name=="ShopTerminalBehavior").Methods.Any(m=>m.Name=="UpdatePickupDisplayAndAnimations" && m.Parameters.Count==0),"Rearming a shared terminal must refresh its native client animations after purchase-state sync");
        var hologram=game.Types.Single(t=>t.Name=="CostHologramContent");
        check(hologram.Fields.Any(f=>f.Name=="targetTextMesh" && f.FieldType.Name=="TextMeshPro") && hologram.Fields.Any(f=>f.Name=="oldDisplayValue" && f.FieldType.FullName=="System.Int32"),"Scrap cost overlay and native fallback must target the actual installed hologram fields");
        var presentation=plugin.Single(t=>t.Name=="GauntletShopPresentation");
        foreach(string hook in new[]{"add_GetContextString","add_UpdateHologramContent"})
            check(presentation.Methods.Single(m=>m.Name=="Install").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name==hook),"Scrap-specific cost must cover both interaction and world hologram: "+hook);
        check(plugin.Single(t=>t.Name=="GauntletScrapHologram").Methods.Single(m=>m.Name=="LateUpdate").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="Refresh"),"The scrap text is applied after the native FixedUpdate formatter");
        check(nativeCost.Methods.Any(m=>m.Name=="PayCost" && !m.IsStatic && m.Parameters.Count==2 && m.Parameters[0].ParameterType.Name=="PayCostContext" && m.Parameters[1].ParameterType.Name=="PayCostResults" && m.ReturnType.FullName=="System.Void"),"Scrap detour must match the actual installed cost-method signature");
        var scrap=shop.Methods.Single(m=>m.Name=="PayScrap");
        check(scrap.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="TryTransform"),"Trades use a permanent native inventory transformation");
        check(!scrap.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="get_nextNormalizedFloat"),"Scrap trades cannot randomly consume a carried item");
        var prices=shop.Methods.Single(m=>m.Name=="ClearReward");
        check(prices.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="ClearReward" && m.DeclaringType.Name=="GauntletShopPricing"),"Clear income is derived from posted item/equipment/drone prices");
        foreach(string field in new[]{"prices","equipmentPrices","dronePrices"})
            check(prices.Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name==field),"Actual shop category prices participate in the clear budget: "+field);
        var nativeDropTable=game.Types.Single(t=>t.Name=="Run").Methods.Single(m=>m.Name=="BuildDropTable");
        check(nativeDropTable.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="ItemCatalog" && m.Name=="get_itemCount"),"Native item pools iterate the full catalog including registered modded items");
        var roll=shop.Methods.Single(m=>m.Name=="Roll");
        foreach(string pool in new[]{"availableTier1DropList","availableTier2DropList","availableTier3DropList","availableLunarItemDropList","availableEquipmentDropList"})
            check(roll.Body.Instructions.Any(i=>i.Operand is FieldReference f && f.DeclaringType.Name=="Run" && f.Name==pool),"Vendor stock must use the current run's expansion-aware and mod-aware pool: "+pool);
        var tick=shop.Methods.Single(m=>m.Name=="Tick");
        foreach(string call in new[]{"NextInPlace","Close","Reset","BeginRound","ScrapperBusy"})
            check(tick.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name==call),"In-place progression needs explicit ownership/reset and shopping completion: "+call);
        check(!tick.Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name=="complete"),"A merchant slot that keeps failing must not block the ready brazier");
        var restock=shop.Methods.Single(m=>m.Name=="Restock").Body;
        var create=restock.Instructions.Single(i=>i.Operand is MethodReference m && m.Name=="Create");
        check(restock.ExceptionHandlers.Any(h=>h.HandlerType==ExceptionHandlerType.Catch && h.TryStart.Offset<=create.Offset && create.Offset<h.TryEnd.Offset),
            "Each merchant slot is created independently; one empty pool cannot skip the brazier");
        var ended=plugin.Single(t=>t.Name=="FortressController").Methods.Single(m=>m.Name=="RoundEnded");
        check(!ended.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GauntletPortals" && m.Name=="Ensure"),"Round clears must not spawn a shopping portal");
        int open=ended.Body.Instructions.ToList().FindIndex(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GauntletShop" && m.Name=="Open");
        int reward=ended.Body.Instructions.ToList().FindIndex(i=>i.Operand is MethodReference m && m.Name=="CompleteRound");
        check(open>=0 && reward>open,"Roll and price shop stock before paying exactly one clear reward");
        var loot=plugin.Single(t=>t.Name=="GauntletPortalLoot").Methods.Single(m=>m.Name=="PaidStock");
        check(loot.Body.Instructions.Any(i=>i.Operand is GenericInstanceMethod m && m.GenericArguments.Any(t=>t.Name=="GauntletPaidLoot")),"Auto-collection excludes purchased and scrapped rewards after landing");
        var player=game.Types.Single(t=>t.Name=="PlayerCharacterMasterController");
        check(player.Methods.Single(m=>m.Name=="Init").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="add_onDeathProcServer"),"Native lunar coin drop listener remains globally installed");
        check(player.Methods.Single(m=>m.IsConstructor && !m.IsStatic).Body.Instructions.Any(i=>i.OpCode.Code==Code.Ldc_R4 && (float)i.Operand==.5f),"Native lunar coins begin at a half-percent kill chance");
        var handlers=player.NestedTypes.SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToArray();
        check(handlers.Any(m=>m.Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name=="lunarCoinChanceMultiplier") && m.Body.Instructions.Any(i=>i.Operand is MethodReference called && called.Name=="CreatePickupDroplet")),"Native coin listener still creates real lunar coin drops");
    }
}
