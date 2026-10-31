using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;
using System.IO.Compression;
using System.Text.RegularExpressions;

internal static class Program
{
    private static int checks;
    private static void Check(bool valid, string message) { checks++;if (!valid) throw new Exception(message); }
    // Execute only the compiled request's integer bounds guard. No game types
    // are loaded: the roster field is represented by its actual compiled length.
    private static bool RejectsCommanderBeforeRuntime(MethodDefinition method,int commander,int rosterLength)
    {
        var stack=new Stack<int>();var instruction=method.Body.Instructions[0];
        for(int steps=0;steps<32 && instruction!=null;steps++)
        {
            var next=instruction.Next;
            switch(instruction.OpCode.Code)
            {
                case Code.Nop:case Code.Conv_I4:case Code.Ldlen:break;
                case Code.Ldarg:case Code.Ldarg_S:
                    if(instruction.Operand is not ParameterDefinition parameter || parameter.Name!="commander")
                        throw new Exception("Commander validation accessed another argument before checking its bounds");
                    stack.Push(commander);break;
                case Code.Ldc_I4_0:stack.Push(0);break;
                case Code.Ldsfld:
                    if(instruction.Operand is not FieldReference field || field.DeclaringType.Name!="GauntletCommandRoster" || field.Name!="Commanders")
                        throw new Exception("Commander validation accessed unrelated state before checking its bounds");
                    stack.Push(rosterLength);break;
                case Code.Blt:case Code.Blt_S:case Code.Bge:case Code.Bge_S:
                    int right=stack.Pop(),left=stack.Pop();
                    bool less=instruction.OpCode.Code==Code.Blt || instruction.OpCode.Code==Code.Blt_S;
                    if(less ? left<right : left>=right)next=(Instruction)instruction.Operand;
                    break;
                case Code.Br:case Code.Br_S:next=(Instruction)instruction.Operand;break;
                case Code.Ret:return true;
                case Code.Call:
                    if(instruction.Operand is MethodReference call && call.DeclaringType.Name=="NetworkServer" && call.Name=="get_active")return false;
                    throw new Exception("Commander validation called into runtime before checking its bounds");
                default:throw new Exception("Review the compiled commander guard opcode: "+instruction.OpCode);
            }
            instruction=next;
        }
        throw new Exception("Commander bounds validation did not terminate");
    }
    private static IEnumerable<TypeDefinition> Types(ModuleDefinition module) => module.Types.SelectMany(Flatten);
    private static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
    { yield return type;foreach (var child in type.NestedTypes) foreach (var nested in Flatten(child)) yield return nested; }
    private static string Body(MethodDefinition method)
    {
        if(!method.HasBody)return "none";
        string Operand(object value)=>value switch
        {
            Instruction target=>"branch:"+method.Body.Instructions.IndexOf(target),
            Instruction[] targets=>string.Join(",",targets.Select(target=>method.Body.Instructions.IndexOf(target))),
            MemberReference member=>StableName(member.FullName),
            ParameterDefinition parameter=>"parameter:"+parameter.Index,
            VariableDefinition variable=>"local:"+variable.Index,
            null=>"",
            _=>Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)
        };
        return string.Join("\n",method.Body.Instructions.Select(instruction=>instruction.OpCode.Name+" "+Operand(instruction.Operand)));
    }
    // Roslyn renumbers closures and cached delegates when fields are added.
    // Compare the callback bodies below as well as their calling methods.
    private static string StableName(string name)=>Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(name,@"<>c__DisplayClass\d+_","<>c__DisplayClass#_"),@"b__\d+_","b__#_"),@"<>9__\d+_","<>9__#_"),@"(<[^>]+>d__)\d+","$1#");
    public static void Main(string[] args)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string gamePath = "X:/SteamLibrary/steamapps/common/Risk of Rain 2/Risk of Rain 2_Data/Managed/RoR2.dll";
        string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Thunderstore Mod Manager/DataFolder/RiskOfRain2/profiles/My Profile");
        using var game = ModuleDefinition.ReadModule(gamePath);
        if(args.Contains("--currency-callers"))
        {
            foreach(var method in Types(game).SelectMany(t=>t.Methods).Where(m=>m.HasBody))
                if(method.Body.Instructions.Any(i=>i.Operand is MethodReference called && called.DeclaringType.FullName=="RoR2.RuleBook" && called.Name=="get_keepMoneyBetweenStages"))Console.WriteLine(method.FullName);
            return;
        }
        using var pack = ModuleDefinition.ReadModule(Path.Combine(profile, "BepInEx/plugins/RiskofThunder-RoR2BepInExPack/RoR2BepInExPack/RoR2BepInExPack.dll"));
        if(args.Contains("--shop-assets"))
        {
            foreach(var f in Types(pack).SelectMany(t=>t.Fields).Where(f=>f.Name.Contains("Drone") && (f.Name.StartsWith("isc") || f.Name.EndsWith("Broken_prefab"))))Console.WriteLine(f.FullName);
            return;
        }
        using var resolver=new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.Combine(profile,"BepInEx/core"));
        resolver.AddSearchDirectory(Path.GetDirectoryName(gamePath));
        using var plugin = ModuleDefinition.ReadModule(Path.Combine(root, "bin/Release/netstandard2.1/ArenaGauntlet.dll"),new ReaderParameters{AssemblyResolver=resolver});
        var requirements = new Dictionary<string, string[]>
        {
            ["RoR2.Run"] = new[] { "_ambientLevel", "_ambientLevelFloor" },
            ["RoR2.InfiniteTowerRun"] = new[] { "defaultWavePrefab", "waveCategories", "safeWardController", "fogDamageController", "fogDamagePrefab", "spawnMaxRadius", "initialSafeWardCard", "stageTransitionPeriod", "stageTransitionPortalCard", "interactableCredits", "enemyItemPeriod", "blacklistedItems", "blacklistedTags" },
            ["RoR2.InfiniteTowerWaveCategory"] = new[] { "wavePrefabs", "availabilityPeriod", "minWaveIndex" },
            ["RoR2.InfiniteTowerWaveController"] = new[] { "combatDirector", "combatSquad", "baseCredits", "linearCreditsPerWave", "immediateCreditsFraction", "secondsBeforeSuddenDeath", "secondsBeforeFailsafe", "maxSquadSize", "creditsPerSecond", "isBossWave", "uiPrefab", "overlayEntries", "beginChatToken", "convertGoldOnWaveFinish" },
            ["RoR2.CharacterAI.BaseAI"] = new[] { "lastDamagedTime" },
            ["RoR2.SceneDirector"] = new[] { "monsterCredit" },
            ["RoR2.SceneExitController"] = new[] { "exitState" },
            ["RoR2.SceneCollection"] = new[] { "_sceneEntries" },
            ["RoR2.ChestBehavior"] = new[] { "dropTable" },
            ["RoR2.GenericPickupController"] = new[] { "consumed" },
            ["RoR2.PickupPickerController"] = new[] { "_grabNumbers", "numberOfPickUpsSelected" }
        };
        foreach (var pair in requirements)
        {
            var type = Types(game).Single(value => value.FullName == pair.Key);
            foreach (string field in pair.Value)
            {
                // Ambient levels are auto-properties, checked below instead.
                if (field.StartsWith("_ambient")) continue;
                Check(type.Fields.Any(value => value.Name == field), pair.Key + "." + field + " contract missing");
            }
        }
        var run = game.Types.Single(type => type.FullName == "RoR2.Run");
        foreach (string property in new[] { "ambientLevel", "ambientLevelFloor" })
            Check(run.Properties.Any(value => value.Name == property && value.SetMethod != null), "Native ambient setter missing: " + property);
        foreach (string method in new[] { "OnAmbientLevelUp", "RecalculateDifficultyCoefficentInternal", "GetEffectiveRunStopwatch" })
            Check(run.Methods.Any(value => value.Name == method), "Native difficulty method missing: " + method);
        var purchase = game.Types.Single(type => type.FullName == "RoR2.PurchaseInteraction");
        foreach (string property in new[] { "NetworkcostType", "Networkcost" })
            Check(purchase.Properties.Any(value => value.Name == property && value.SetMethod != null), "Networked shop setter missing: " + property);
        var picker=game.Types.Single(type=>type.FullName=="RoR2.PickupPickerController");
        foreach(string method in new[]{"SetAvailable","HandlePickupSelected","OnInteractionBegin"})
            Check(picker.Methods.Any(value=>value.Name==method),"Native castle picker gate missing: "+method);
        var dialog=game.Types.Single(type=>type.FullName=="RoR2.UI.SimpleDialogBox");
        foreach(string field in new[]{"rootObject","buttonContainer","descriptionLabel"})
            Check(dialog.Fields.Any(value=>value.Name==field && value.IsPublic),"Native castle menu layout/cleanup field missing: "+field);
        var hurtBox=game.Types.Single(type=>type.FullName=="RoR2.HurtBox");
        Check(hurtBox.Fields.Any(field=>field.Name=="indexInGroup"),"Structure hurtbox network index missing");
        using var nativeUi=ModuleDefinition.ReadModule(Path.Combine(Path.GetDirectoryName(gamePath),"UnityEngine.UI.dll"));
        var layoutGroup=Types(nativeUi).Single(type=>type.FullName=="UnityEngine.UI.LayoutGroup");
        Check(layoutGroup.CustomAttributes.Any(attribute=>attribute.AttributeType.Name=="DisallowMultipleComponent"),"Review the native layout-group restriction before changing menu container setup");
        var pluginTypes=Types(plugin).ToArray();
        var commandRoster=pluginTypes.Single(t=>t.Name=="GauntletCommandRoster");
        var rosterArray=commandRoster.Methods.Single(m=>m.Name==".cctor").Body.Instructions
            .Single(i=>i.OpCode.Code==Code.Newarr && i.Operand is TypeReference t && t.Name=="CommanderDef");
        int rosterLength=rosterArray.Previous.OpCode.Code switch
        {
            Code.Ldc_I4_S=>(sbyte)rosterArray.Previous.Operand,
            Code.Ldc_I4=>(int)rosterArray.Previous.Operand,
            _=>throw new Exception("Review the compiled commander roster length")
        };
        var commandHandle=pluginTypes.Single(t=>t.Name=="GauntletCompanies").Methods.Single(m=>m.Name=="Handle");
        foreach(int commander in Enumerable.Range(0,256).Concat(new[]{-1,int.MinValue,int.MaxValue}))
            Check(RejectsCommanderBeforeRuntime(commandHandle,commander,rosterLength)==(commander<0 || commander>=rosterLength),
                "Compiled host request must reject invalid commander IDs before runtime/ledger access: "+commander);
        Check(!pluginTypes.SelectMany(type=>type.Methods).Where(method=>method.HasBody).SelectMany(method=>method.Body.Instructions)
            .Any(instruction=>instruction.Operand is MethodReference method && method.DeclaringType.FullName=="UnityEngine.Shader" && method.Name=="Find"),
            "Legacy Shader.Find can throw through the game's asset detour; use native addressable shader references");
        var fortressTick=pluginTypes.Single(type=>type.Name=="FortressController").Methods.Single(method=>method.Name=="FixedUpdate");
        Check(!fortressTick.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference method && method.DeclaringType.Name=="FortressVisuals"),
            "A castle visual failure must not interrupt authoritative health/funding/respawn updates");
        Check(!pluginTypes.Any(t=>t.Name=="FortressMenu" || t.Name=="FortressDialogLayout"),"Retired castle management menus must stay removed");
        // Combat keeps only War Council; career UI is selection-screen-only.
        var cursorUsers=pluginTypes.Where(t=>t.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is GenericInstanceMethod m && m.Name=="AddComponent" && m.GenericArguments.Any(a=>a.Name=="CursorOpener"))).Select(t=>t.Name).ToArray();
        Check(cursorUsers.OrderBy(n=>n).SequenceEqual(new[]{"GauntletCareerMenu","GauntletHud"}),"Only War Council and the selection-only career panel may open a cursor");
        var council=pluginTypes.Single(t=>t.Name=="GauntletHud").Methods.Single(m=>m.Name=="BuildCouncil");
        foreach(string component in new[]{"MPEventSystemLocator","CursorOpener","GraphicRaycaster","Canvas"})
            Check(council.Body.Instructions.Any(i=>i.Operand is GenericInstanceMethod m && m.Name=="AddComponent" && m.GenericArguments.Any(a=>a.Name==component)),"War Council panel needs native input component: "+component);
        Check(council.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GameObject" && m.Name=="set_layer"),"War Council's nested canvas must take the HUD's UI layer, or the native UI camera culls it");
        Check(!pluginTypes.SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="KeyboardShortcut" && m.Name=="IsDown"),
            "KeyboardShortcut.IsDown drops the press while a movement key is held; hotkeys test only their own keys");
        var waves=pluginTypes.Single(t=>t.Name=="GauntletWaves");
        Check(waves.Methods.Single(m=>m.Name=="HideWard").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="Collider" && m.Name=="set_enabled"),
            "The hidden safe ward must also drop its solid StandableSurface collider, or an invisible ball blocks the arena centre");
        foreach(string hook in new[]{"ActiveWard","WaitingWard"})
            Check(waves.Methods.Single(m=>m.Name==hook).Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="HideWard"),"Every machine hides the ward through its state hook: "+hook);
        Check(pluginTypes.Where(t=>t.FullName.StartsWith("BazaarGauntlet.GauntletCatalogue",StringComparison.Ordinal)).SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions)
            .Any(i=>i.OpCode.Code==Code.Stfld && i.Operand is FieldReference f && f.Name=="requiredFlags"),
            "Major-boss adapters must clear native node requirements; the generated arena grid has no TeleporterOK nodes for Twisted Scavengers");
        Check(game.Types.Single(t=>t.FullName=="RoR2.UI.CursorOpener").CustomAttributes.Any(a=>a.AttributeType.Name=="RequireComponent"),"Native cursor opener still binds through its event-system locator");
        Check(game.Types.Single(t=>t.FullName=="RoR2.UI.HUD").CustomAttributes.Any(a=>a.AttributeType.Name=="RequireComponent" && a.ConstructorArguments.Any(arg=>arg.Value is TypeReference type && type.Name=="MPEventSystemProvider")),"HUD panels resolve the local player's event system");
        var automatic=pluginTypes.Single(t=>t.Name=="FortressController").Methods.Single(m=>m.Name=="FixedUpdate");
        foreach(string name in new[]{"AllParticipatingNetworkUsersReady","Tick","BeginRound"})
            Check(automatic.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name==name),"Host auto-start must wait for readiness and consume a one-shot grace: "+name);
        var nativeUsers=game.Types.Single(t=>t.FullName=="RoR2.NetworkUser");
        Check(nativeUsers.Methods.Any(m=>m.Name=="AllParticipatingNetworkUsersReady" && m.IsPublic && m.IsStatic),"Native multiplayer arrival gate missing");
        var markerUpdate=pluginTypes.Single(t=>t.Name=="GauntletEnemyIndicators").Methods.Single(m=>m.Name=="Update");
        Check(markerUpdate.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="ShouldMarkEnemies"),"Client markers must use the tested final-five policy");
        Check(markerUpdate.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="RequestDefaultIndicator"),"Keep native enemy marker presentation");
        foreach(string name in new[]{"castle.meshbin","castle_ivory.png","castle_stone.png","castle_alloy.png","castle_slate.png","castle_brass.png","castle_energy.png","castle_glass.png","castle_green.png","castle_red.png"})
        {
            var resource=plugin.Resources.OfType<EmbeddedResource>().SingleOrDefault(value=>value.Name=="BazaarGauntlet.assets."+name);
            Check(resource!=null,"Missing embedded castle model resource: "+name);
            Check(resource.GetResourceData().SequenceEqual(File.ReadAllBytes(Path.Combine(root,"assets",name))),"Packaged castle model differs from the checked/previewed source: "+name);
        }
        string baseline=Path.Combine(root,"dist/ArenaGauntlet-0.32.0.zip");
        Check(File.Exists(baseline),"The published 0.32.0 baseline is required for preservation checks");
        if(File.Exists(baseline))
        {
            using var archive=ZipFile.OpenRead(baseline);using var stream=new MemoryStream();
            using(var packed=archive.GetEntry("BepInEx/plugins/ArenaGauntlet/ArenaGauntlet.dll").Open())packed.CopyTo(stream);
            stream.Position=0;using var previous=ModuleDefinition.ReadModule(stream);
            var protectedTypes=new[]{"FortressState","FortressTree","FortressServices","GauntletPlan","GauntletBalance","GauntletWaveBudget","GauntletWaves","GauntletLoot","GauntletAggression","GauntletRuntime","FortressApproach","GauntletNetwork","GauntletSnapshot","GauntletEncounterGate`1","GauntletCatalogueRules","GauntletCurrency","GauntletCurrencyPolicy","GauntletPortalLoot","GauntletLootPolicy","GauntletConfig","GauntletCompanies","GauntletCommandRoster","GauntletWarCouncil","GauntletCommandRequest","GauntletCommandSnapshot","GauntletCompanyUnit","GauntletBossPolicy","GauntletCatalogue","FortressBattlefieldGrid"};
            protectedTypes=protectedTypes.Concat(new[]{"GauntletFlow","GauntletController","GauntletRounds","GauntletHud","GauntletQuotaLedger`1","FortressController","GauntletOptions"}).ToArray();
            protectedTypes=protectedTypes.Concat(new[]{"GauntletEconomy","GauntletEconomyPolicy","GauntletRoundIncome","FortressVisuals","FortressEffect","FortressNetwork"}).ToArray();
            protectedTypes=protectedTypes.Concat(new[]{"GauntletShop","GauntletShopTag","GauntletShopPlan","GauntletShopPricing","GauntletMerchantPrices","GauntletShopVisuals","GauntletShopLoot","GauntletPaidLoot"}).ToArray();
            protectedTypes=protectedTypes.Concat(new[]{"GauntletShopPresentation","GauntletScrapHologram"}).ToArray();
            protectedTypes=protectedTypes.Concat(new[]{"GauntletExperience","GauntletExperiencePolicy","GauntletArrowGraphic","GauntletHudMarker"}).ToArray();
            protectedTypes=protectedTypes.Concat(new[]{"GauntletDropBudget"}).ToArray();
            protectedTypes=protectedTypes.Concat(new[]{"GauntletStartingItems","GauntletStarterPlan`2","GauntletStarterPolicy"}).ToArray();
            protectedTypes=protectedTypes.Concat(new[]{"GauntletHordeRewards"}).ToArray();
            // 0.33 explicitly integrates personal progression at these boundaries.
            // Prices, income, population/credit/drop budgets, company damage and native map/encounter rules remain protected.
            var reviewed=new HashSet<string>{"GauntletController.Awake","GauntletController.InitializeHost","GauntletCompanies.Handle","GauntletCompanies.RefreshCapacity","GauntletCompanies.Reconcile","GauntletCompanies.Broadcast",
                "GauntletCommandSnapshot.Serialize","GauntletCommandSnapshot.Deserialize","GauntletCommandSnapshot.OnReceived","GauntletWarCouncil.EnforceCapacity",
                "GauntletHud.Update","FortressController.FixedUpdate","FortressController.RoundEnded","GauntletRounds.WaveCleared","GauntletWaveBudget.Initialize","GauntletWaveBudget.Spawned",
                "GauntletStartingItems.Tick","GauntletStarterPlan`2.Next","GauntletStarterPlan`2.Announce","GauntletShop.Install","GauntletShop.Interactability","GauntletShop.Affordable","GauntletShop.Purchase",
                "GauntletWaves.ConfigurePrefab", // Only overlayEntries changed (inert WolfoQoL slots); HudContracts pins it.
                // 0.35: packet run identity moved from host-only Run.seed to the synced RunKey; the seed guard below pins it.
                "GauntletNetwork.Broadcast","GauntletSnapshot.OnReceived","GauntletCompanies.Request","GauntletCompanies.HandleRequest",
                "FortressVisuals.Tracer","FortressVisuals.Pulse","FortressEffect.OnReceived",
                // 0.36: shop softlocks (per-slot setup, dead players) and paired-boss species identity; MerchantContracts pins the shop.
                "GauntletShop.Restock","GauntletShop.Tick","GauntletCatalogue.Boss",
                // 0.37: readable text (auto-size labels, arena objective replaces the ward's, unclipped scrap hologram).
                "GauntletWaves.ActiveWard","GauntletWaves.WaitingWard","GauntletHud.Label","GauntletHud.RefreshCouncil","GauntletScrapHologram.Refresh",
                // 0.39: instanced loot. Drops go to one player through GauntletLootInstancing; the rolls, tables and budget are unchanged.
                "GauntletLoot.EnemyDied","GauntletLoot.BossRewards","GauntletPortalLoot.Collect","GauntletPortalLoot.Restore","GauntletPortalLoot..ctor",
                // 0.39: endless continuation is off by default, with a one-time switch-off for saved configs;
                // the run item target drops from about 82 to 73 through a smaller horde budget. DropAudit pins the new curve.
                "GauntletConfig.Bind","GauntletDropBudget.Expected"};
            foreach(var oldType in Types(previous).Where(type=>protectedTypes.Contains(type.Name)))
            {
                var currentType=pluginTypes.Single(type=>type.FullName==oldType.FullName);
                string Fields(TypeDefinition type)=>string.Join("\n",type.Fields.Where(f=>!(type.Name=="GauntletController" && (f.Name=="Meta" || f.Name=="Ascension" || f.Name=="Loot")) && !(type.Name=="GauntletPortalLoot" && f.Name=="carriedOwners") && !(type.Name=="GauntletDropBudget" && f.Name=="ItemsPerPlayerAtMithrix")).Select(f=>f.FullName+"|"+f.Attributes+"|"+f.Constant));
                Check(Fields(oldType)==Fields(currentType),"Art/rename changed protected gameplay state: "+oldType.FullName);
                foreach(var oldMethod in oldType.Methods)
                {
                    string originalName=oldMethod.Name;
                    if(originalName.StartsWith("<",StringComparison.Ordinal))originalName=originalName.Substring(1,originalName.IndexOf('>')-1);
                    if(reviewed.Contains(oldType.Name+"."+originalName))continue;
                    var current=currentType.Methods.SingleOrDefault(method=>StableName(method.FullName)==StableName(oldMethod.FullName));
                    Check(current!=null,"Protected method was removed or changed signature: "+oldMethod.FullName);
                    Check(Body(oldMethod)==Body(current),"Review changed unrelated protected gameplay: "+oldMethod.FullName);
                }
            }
            var oldField=Types(previous).Single(t=>t.Name=="FortressBattlefield");
            var newField=pluginTypes.Single(t=>t.Name=="FortressBattlefield");
            foreach(string name in new[]{"ClearNativeScenery","Hide","ReplaceGraph","BuildBoundary","FixedUpdate","NativeScene","Ensure","RefreshScenery"})
                Check(Body(oldField.Methods.Single(m=>m.Name==name))==Body(newField.Methods.Single(m=>m.Name==name)),"Arena art must retain native cleanup, boundaries, navigation and recovery: "+name);
            var oldFoes=Types(previous).Single(t=>t.Name.StartsWith("<Foes>d__"));
            var newFoes=pluginTypes.Single(t=>t.Name.StartsWith("<Foes>d__"));
            Check(Body(oldFoes.Methods.Single(m=>m.Name=="MoveNext"))==Body(newFoes.Methods.Single(m=>m.Name=="MoveNext")),"Compiler iterator numbering may change, but the ability-target filter must remain identical");
        }
        var registry = Types(pack).Where(type => type.Namespace == "RoR2BepInExPack.GameAssetPathsBetter" && !type.Name.Contains("_Junk_")).ToArray();
        if(args.Length>0)
        {
            foreach(var field in registry.SelectMany(type=>type.Fields).Where(field=>args.Any(arg=>field.Name.Contains(arg,StringComparison.OrdinalIgnoreCase))))
                Console.WriteLine(field.DeclaringType.Name+"."+field.Name);
            return;
        }
        var keys = new Dictionary<string, string>();
        foreach (var type in registry)
        foreach (var initializer in type.Methods.Where(method => method.Name == ".cctor" && method.HasBody))
        foreach (var instruction in initializer.Body.Instructions)
            if (instruction.OpCode.Code == Mono.Cecil.Cil.Code.Stsfld && instruction.Operand is FieldReference field && instruction.Previous?.Operand is string guid)
                keys[field.FullName] = guid;
        var cards = registry.SelectMany(type => type.Fields).Where(field => field.Name.StartsWith("csc") && field.Name.EndsWith("_asset") && !field.DeclaringType.Name.Contains("_Junk_")).ToArray();
        Check(cards.Length > 20, "Native path registry does not contain a viable enemy-card catalogue");
        foreach (string name in new[] { "cscLemurianBruiser_asset","cscLemurian_asset","cscBeetleGuardAlly_asset","cscBeetle_asset","cscGreaterWisp_asset","cscLesserWisp_asset",
            "cscBell_asset","cscGolem_asset","cscClayBruiser_asset","cscClayGrenadier_asset","cscLunarGolem_asset","cscLunarExploder_asset","cscRoboBallBoss_asset","cscRoboBallMini_asset",
            "cscVoidJailerAlly_asset","cscNullifierAlly_asset","cscTitanGolemPlains_asset","cscVoidMegaCrabAlly_asset","Chest1_prefab", "OptionPickup_prefab",
            "EngiTurretBody_prefab","EngiTurretMaster_prefab","HGStandard_shader","ClassicRunInfoHudPanel_prefab","LunarShopTerminal_prefab","iscScrapper_asset",
            "iscDuplicator_asset","iscDuplicatorLarge_asset","iscDuplicatorMilitary_asset",
            "LunarCauldron__WhiteToGreen_prefab","LunarCauldron__GreenToRed_Variant_prefab","LunarCauldron__RedToWhite_Variant_prefab",
            "iscShrineCleanse_asset","iscShrineRestack_asset","iscTripleDroneShop_asset","iscDroneAssemblyStation_asset",
            "iscDroneCombinerStation_asset","iscDroneScrapper_asset","MealPrep_prefab","iscGoldChest_asset","iscEquipmentBarrel_asset","OmniExplosionVFX_prefab",
            "Drone1Broken_prefab","Drone2Broken_prefab","MissileDroneBroken_prefab" })
            Check(registry.SelectMany(type => type.Fields).Any(field => field.Name == name && keys.TryGetValue(field.FullName, out var key) && key.Length == 32), "Unverified addressable GUID field: " + name);
        foreach (var card in cards) Check(keys.TryGetValue(card.FullName, out var key) && key.Length == 32, "Unresolved native spawn-card key: " + card.FullName);
        foreach (string fragment in new[] { "cscBrother_asset", "cscMiniVoidRaidCrabPhase3_asset", "cscScavLunar" })
            Check(cards.Any(field => field.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)), "Major boss spawn-card source missing: " + fragment);
        var tower = game.Types.Single(type => type.FullName == "RoR2.InfiniteTowerRun");
        Check(tower.Methods.Any(method => method.Name == "SpawnSafeWard" && method.Parameters.Count == 2 && method.Parameters[1].ParameterType.Name == "DirectorPlacementRule"), "Native fallback ward setup contract missing");
        Check(tower.Methods.Any(method=>method.Name=="OnSafeWardActivated" && method.Parameters.Count==1),"Native in-place round activation contract missing");
        Check(tower.Methods.Any(method=>method.Name=="PerformStageCleanUp" && method.Parameters.Count==0),"Bazaar arrival must use installed native stage cleanup without spawning a ward");
        Check(tower.Methods.Any(method => method.Name == "OnDestroy"), "Native restart cleanup hook missing");
        var spawnPoint = game.Types.Single(type => type.FullName == "RoR2.SpawnPoint");
        Check(spawnPoint.Methods.Single(method => method.Name == "OnDisable").Body.Instructions.Any(instruction => instruction.Operand is MethodReference method && method.Name == "Remove"), "Disabling stale spawn points must remove them immediately from the native registry");
        var sceneDirector = game.Types.Single(type => type.FullName == "RoR2.SceneDirector");
        Check(sceneDirector.Events.Any(value => value.Name == "onPreGeneratePlayerSpawnPointsServer"), "Native replacement spawn-generator event missing");
        var combat = game.Types.Single(type => type.FullName == "RoR2.CombatDirector");
        Check(combat.Fields.Any(field => field.Name == "ignoreTeamSizeLimit" && field.IsPublic), "Wave cannot bypass the native team soft limit to honor the progressive cap");
        var mode = plugin.Types.Single(type => type.Name == "GauntletMode").Methods.Single(method => method.Name == "Register");
        var fogField = mode.Body.Instructions.Single(instruction => instruction.OpCode.Code == Code.Ldstr && (string)instruction.Operand == "fogDamagePrefab");
        Check(fogField.Next.OpCode.Code == Code.Ldnull, "Gauntlet prefab must not instantiate a damaging Simulacrum fog");
        string quickRestartPath = Path.Combine(profile, "BepInEx/plugins/AceOfShades-QuickRestart/QuickRestart.dll");
        if (File.Exists(quickRestartPath))
        {
            using var quickRestart = ModuleDefinition.ReadModule(quickRestartPath);
            var reset = Types(quickRestart).Single(type => type.FullName == "QuickRestart.BoothUtil").Methods.Single(method => method.Name == "ActuallyResetGame");
            Check(reset.Body.Instructions.Any(instruction => instruction.Operand is MethodReference method && method.Name == "Destroy"), "Installed Quick Restart no longer destroys the old run; audit compatibility again");
            var restartCoroutine = Types(quickRestart).First(type => type.Name.StartsWith("<StartNewGame>"));
            Check(restartCoroutine.Methods.SelectMany(method => method.HasBody ? method.Body.Instructions : Enumerable.Empty<Instruction>()).Any(instruction => instruction.OpCode.Code == Code.Ldstr && Equals(instruction.Operand, "StartRun")), "Installed Quick Restart no longer starts through PreGameController");
            var pregameStart = game.Types.Single(type => type.FullName == "RoR2.PreGameController").Methods.Single(method => method.Name == "StartRun");
            Check(pregameStart.Body.Instructions.Any(instruction => instruction.Operand is FieldReference field && field.Name == "runPrefabComponent"), "Restart no longer respects the selected game-mode prefab");
        }
        var describe=pluginTypes.Single(t=>t.Name=="GauntletCareerMenu").Methods.Single(m=>m.Name=="Describe");
        Check(describe.Body.Instructions.Any(i=>i.Operand is GenericInstanceMethod m && m.Name=="AddComponent" && m.GenericArguments.Any(a=>a.FullName=="RoR2.UI.TooltipProvider")),"Loadout buttons need the native hover tooltip");
        string lookingGlassPath=Path.Combine(profile,"BepInEx/plugins/DropPod-LookingGlass/LookingGlass/LookingGlass.dll");
        if(File.Exists(lookingGlassPath))
        {
            // The career menu finds this by reflection; a renamed or re-signed method silently drops LookingGlass stats.
            using var lookingGlass=ModuleDefinition.ReadModule(lookingGlassPath);
            var stats=Types(lookingGlass).SingleOrDefault(t=>t.FullName=="LookingGlass.ItemStatsNameSpace.ItemStats")?.Methods.SingleOrDefault(m=>m.Name=="GetItemDescription");
            Check(stats!=null && stats.IsPublic && stats.IsStatic && stats.ReturnType.FullName=="System.String" &&
                stats.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"RoR2.ItemDef","System.Int32","RoR2.CharacterMaster","System.Boolean","System.Boolean"}),
                "Installed LookingGlass changed its item stat API; audit the loadout tooltip lookup again");
            Check(Types(lookingGlass).Any(t=>t.CustomAttributes.Any(a=>a.AttributeType.Name=="BepInPlugin" && Equals(a.ConstructorArguments[0].Value,"droppod.lookingglass"))),"Installed LookingGlass changed its plugin GUID");
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "package/manifest.json")));
        string version = manifest.RootElement.GetProperty("version_number").GetString();
        Check(plugin.Assembly.Name.Version.ToString(3) == version, "Review assembly version mismatch");
        var main = plugin.Types.Single(type => type.Name == "GauntletPlugin");
        var attr = main.CustomAttributes.Single(value => value.AttributeType.Name == "BepInPlugin");
        Check((string)attr.ConstructorArguments[2].Value == version, "Plugin metadata version mismatch");
        Check((string)attr.ConstructorArguments[1].Value == manifest.RootElement.GetProperty("name").GetString(), "Game/options and package names must match");
        Check((string)attr.ConstructorArguments[0].Value == "Kuriyukihime.BazaarGauntlet", "Rename must retain existing config/plugin identity");
        Check(plugin.Assembly.Name.Name == "ArenaGauntlet", "Renamed release assembly missing");
        Check((int)pluginTypes.Single(t=>t.Name=="GauntletPlan").Fields.Single(f=>f.Name=="RoundsPerCycle").Constant==10,"Compiled cycle length is ten");
        var dependencies=manifest.RootElement.GetProperty("dependencies").EnumerateArray().Select(d=>d.GetString()).ToArray();
        Check(!dependencies.Any(d=>d.Contains("BiggerBazaar")),"The launcher must no longer install BiggerBazaar");
        Check(!main.CustomAttributes.Any(a=>a.AttributeType.Name=="BepInDependency" && Equals(a.ConstructorArguments[0].Value,"com.MagnusMagnuson.BiggerBazaar")),"No runtime BiggerBazaar dependency remains");
        Check(mode.Body.Instructions.Any(i=>i.OpCode.Code==Code.Ldstr && Equals(i.Operand,"ArenaGauntlet")),"Game-mode selection must show ArenaGauntlet");
        var arenaMaterials=pluginTypes.Single(t=>t.Name=="ArenaMaterials");
        foreach(var texture in Directory.GetFiles(Path.Combine(root,"assets"),"arena_*.png"))
        {
            var resource=plugin.Resources.OfType<EmbeddedResource>().Single(r=>r.Name=="BazaarGauntlet.assets."+Path.GetFileName(texture));
            Check(resource.GetResourceData().SequenceEqual(File.ReadAllBytes(texture)),"Embedded original arena surface differs from the inspected asset: "+texture);
        }
        Check(arenaMaterials.Methods.Single(m=>m.Name=="Create").Body.ExceptionHandlers.Any(h=>h.HandlerType==ExceptionHandlerType.Catch),"Failed material creation must dispose its partial material before retry");
        var mixedDirector=game.Types.Single(type=>type.FullName=="RoR2.CombatDirector");
        Check(mixedDirector.Methods.Any(method=>method.Name=="PrepareNewMonsterWave" && method.Parameters.Count==1), "Native mixed-horde selection method missing");
        foreach(string field in new[]{"maximumNumberToSpawnBeforeSkipping","skipSpawnIfTooCheap","resetMonsterCardIfFailed","targetPlayers"})
            Check(mixedDirector.Fields.Any(value=>value.Name==field),"Native mixed-horde field missing: "+field);
        var bodyType=game.Types.Single(type=>type.FullName=="RoR2.CharacterBody");
        Check(bodyType.Properties.Any(value=>value.Name=="isFlying"),"Native flight trait for enemy roles missing");
        var aiDriver=Types(game).Single(type=>type.FullName=="RoR2.CharacterAI.AISkillDriver");
        foreach(string field in new[]{"skillSlot","maxDistance"})
            Check(aiDriver.Fields.Any(value=>value.Name==field),"Native enemy role driver field missing: "+field);
        Check(bodyType.Fields.Any(field=>field.Name=="isNotFlying"),"Native stationary-structure ground-target override missing");
        var nodeGraph=game.Types.Single(type=>type.FullName=="RoR2.Navigation.NodeGraph");
        foreach(string field in new[]{"nodes","links"})
            Check(nodeGraph.Fields.Any(value=>value.Name==field),"Scene-local causeway navigation field missing: "+field);
        foreach(string method in new[]{"Awake","GetLinkNeedsJump","IsLinkSuitableForHull","FindClosestNode","GetActiveNodeLinks"})
            Check(nodeGraph.Methods.Any(value=>value.Name==method),"Causeway navigation API missing: "+method);
        var registerStructure=pluginTypes.Single(type=>type.Name=="FortressAssets").Methods.Single(method=>method.Name=="RegisterKind");
        Check(registerStructure.Body.Instructions.Any(instruction=>instruction.OpCode.Code==Code.Stfld && instruction.Operand is FieldReference field && field.Name=="isNotFlying" && instruction.Previous.OpCode.Code==Code.Ldc_I4_1),"Castle structures must be native ground targets");
        var buildCastle=pluginTypes.Single(type=>type.Name=="FortressArchitecture").Methods.Single(method=>method.Name=="Build");
        Check(buildCastle.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference method && method.Name=="AddHullCollision"),"Static castle geometry must create separate movement collision");
        Check(buildCastle.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field && field.DeclaringType.Name=="LayerIndex" && field.Name=="defaultLayer"),"Castle renderers must use the visible Default layer");
        Check(!buildCastle.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field && field.Name=="collideWithCharacterHullOnly"),"Never move a castle renderer onto the camera-hidden hull-only layer");
        var addCollision=pluginTypes.Single(type=>type.Name=="FortressArchitecture").Methods.Single(method=>method.Name=="AddHullCollision");
        Check(addCollision.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field && field.Name=="collideWithCharacterHullOnly"),"Separate movement collision must retain its native hull-only layer");
        Check(addCollision.Body.Instructions.Any(instruction=>instruction.OpCode.Code==Code.Ldtoken && instruction.Operand is TypeReference type && type.Name=="MeshCollider"),"Collision-only object must have a MeshCollider");
        Check(!addCollision.Body.Instructions.Any(instruction=>instruction.Operand is TypeReference type && (type.Name=="MeshRenderer" || type.Name=="MeshFilter")),"Physics-only object must never contain a renderer");
        var roundEnded=pluginTypes.Single(type=>type.Name=="FortressController").Methods.Single(method=>method.Name=="RoundEnded");
        Check(roundEnded.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference method && method.Name=="RoundCleared" && method.DeclaringType.Name=="GauntletPortalLoot"),"Round clear must schedule native free-drop collection");
        var sceneInfo=game.Types.Single(type=>type.FullName=="RoR2.SceneInfo");
        Check(sceneInfo.Methods.Any(method=>method.Name=="OnEnable"),"Battlefield bootstrap requires native SceneInfo activation");
        Check(sceneInfo.Methods.Single(method=>method.Name=="OnEnable").Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field && field.Name=="_instance"),"Build the battlefield after SceneInfo.instance is assigned, not inside Awake before activation");
        foreach(string name in new[]{"groundNodes","airNodes"})Check(sceneInfo.Properties.Any(property=>property.Name==name && property.GetMethod!=null),"Native battlefield graph missing: "+name);
        var mapZone=game.Types.Single(type=>type.FullName=="RoR2.MapZone");
        foreach(string name in new[]{"zoneType","triggerType","explicitDestination","useExactExplicitPositionAndRotation"})Check(mapZone.Fields.Any(field=>field.Name==name),"Native return boundary field missing: "+name);
        Check(mapZone.Methods.Single(method=>method.Name=="OnDisable").Body.Instructions.Any(instruction=>instruction.Operand is MethodReference method && method.Name=="Remove"),"Disable old map-zone behaviours as well as their colliders to remove their native static tracking");
        var fieldType=pluginTypes.Single(type=>type.Name=="FortressBattlefield");
        var hideCollider=fieldType.Methods.Single(method=>method.Name=="Hide");
        Check(hideCollider.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field && field.Name=="nativeColliderIDs"),"Map clearing must only disable captured native scenery, never newly created castle collision");
        var clearScenery=fieldType.Methods.Single(method=>method.Name=="ClearNativeScenery");
        Check(clearScenery.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field && field.Name=="nativeRendererIDs"),"Map clearing must protect new castle and terrain renderers");
        var boundary=fieldType.Methods.Single(method=>method.Name=="BuildBoundary");
        Check(boundary.Body.Instructions.Any(instruction=>instruction.OpCode.Code==Code.Stfld && instruction.Operand is FieldReference field && field.Name=="zoneType" && instruction.Previous.OpCode.Code==Code.Ldc_I4_1),"Return allies without killing burrowing bosses at the arena boundary");
        var graphReplace=fieldType.Methods.Single(method=>method.Name=="ReplaceGraph");
        Check(graphReplace.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference method && method.Name=="Neighbors" && method.DeclaringType.Name=="FortressBattlefieldGrid"),"Runtime must use the same battlefield links as the tested production generator");
        var arrival=pluginTypes.Single(type=>type.Name=="FortressController").Methods.Single(method=>method.Name=="Arrive");
        Check(arrival.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference method && method.DeclaringType.Name=="FortressBattlefield" && method.Name=="Ensure"),"Castle arrival must use the custom field");
        Check(!pluginTypes.SelectMany(type=>type.Methods).Where(method=>method.HasBody && method.DeclaringType.Name!="FortressApproach").SelectMany(method=>method.Body.Instructions).Any(instruction=>instruction.Operand is MethodReference method && method.DeclaringType.Name=="FortressApproach"),"The obsolete Aphelian ramp must not run on the new map");
        foreach(string name in new[]{"PortalShop_prefab","texGPGrassTerrain_png","texGPGrassTerrainNormal_png"})
            Check(registry.SelectMany(type=>type.Fields).Any(field=>field.Name==name && keys.TryGetValue(field.FullName,out var key) && key.Length==32),"Native arena asset key missing: "+name);
        var art=plugin.Resources.OfType<EmbeddedResource>().SingleOrDefault(resource=>resource.Name=="BazaarGauntlet.assets.colosseum.meshbin");
        Check(art!=null && art.GetResourceData().SequenceEqual(File.ReadAllBytes(Path.Combine(root,"assets/colosseum.meshbin"))),"Runtime must embed the exact reviewed colosseum mesh");
        var buildArena=fieldType.Methods.Single(m=>m.Name=="BuildGeometry");
        // Colosseum and Treasury decor meshes share one builder.
        Check(buildArena.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="BuildMesh") &&
            fieldType.Methods.Single(m=>m.Name=="BuildMesh").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="RecalculateTangents"),"Stone normal maps require actual mesh tangents");
        Check(buildArena.Body.Instructions.Any(i=>i.OpCode.Code==Code.Ldstr && Equals(i.Operand,"Courtyard")),"Flush courtyard decoration must stay out of the architecture collider path");
        Check(fieldType.Methods.Single(m=>m.Name=="ReleaseMaterials").Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name=="ownedTextures"),"Teardown may destroy only original owned textures, never native grass");
        Check(!mode.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="FortressAssets" && m.Name=="Register"),"Gladiator mode must not register or summon defensive objective bodies");
        var exitMethod=pluginTypes.Single(t=>t.Name=="GauntletRounds").Methods.Single(m=>m.Name=="Exit");
        foreach(string call in new[]{"EnterBazaar","ReturnToArena","RestorePlayers","Collect"})
            Check(exitMethod.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name==call),"Accepted portal must use the authoritative arena flow/recovery/loot path: "+call);
        var gameOver=pluginTypes.Single(t=>t.Name=="GauntletRuntime").Methods.Single(m=>m.Name=="GameOver");
        Check(!gameOver.Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name=="Modules"),"Party wipe must never be suppressed by a nonexistent core");
        var loot=pluginTypes.Single(t=>t.Name=="GauntletLoot").Methods.Single(m=>m.Name=="BossRewards");
        Check(loot.Body.Instructions.Any(i=>i.OpCode.Code==Code.Ldc_R4 && (float)i.Operand==.24f),"Boss-specific item chance must be 24 percent after the authorized relative twenty-percent increase");
        var sceneLeaving=fieldType.Methods.Single(m=>m.Name=="SceneLeaving");
        Check(sceneLeaving.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="Release"),"Scene exits must release the old arena before a later visit rebuilds it");
        var nativeTower=game.Types.Single(t=>t.Name=="InfiniteTowerRun");
        foreach(string name in new[]{"BeginNextWave","InitializeWaveController","CleanUpCurrentWave","OnSafeWardActivated"})
            Check(nativeTower.Methods.Any(m=>m.Name==name),"Native encounter lifecycle method missing: "+name);
        var lifecycle=pluginTypes.Single(t=>t.Name=="GauntletWaves");
        var initController=lifecycle.Methods.Single(m=>m.Name=="InitializeController");
        Check(initController.Body.ExceptionHandlers.Any(h=>h.HandlerType==ExceptionHandlerType.Catch),"Native subscription setup must roll back failures rather than leaving a live half-initialized wave");
        var beginWave=lifecycle.Methods.Single(m=>m.Name=="BeginWave");
        Check(beginWave.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="TryBegin"),"Native wave starts require the tested encounter ownership gate");
        Check(beginWave.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="get_NativeIndex"),"Retries must preserve the native horde/boss index");
        var clearRound=pluginTypes.Single(t=>t.Name=="GauntletRounds").Methods.Single(m=>m.Name=="WaveCleared");
        Check(clearRound.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="Clear" && m.DeclaringType.Name.StartsWith("GauntletEncounterGate")),"Only the committed current wave may advance progression/rewards");
        var initializeBudget=pluginTypes.Single(t=>t.Name=="GauntletWaveBudget").Methods.Single(m=>m.Name=="Initialize");
        Check(initializeBudget.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GauntletPlan" && m.Name=="BossCount"),"The actual spawn budget must use the paired-boss policy");
        Check(pluginTypes.Single(t=>t.Name=="GauntletWaveBudget").Methods.Single(m=>m.Name=="get_Cleared").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="get_Complete"),"Native wave clear requires the tested full spawn-and-death quota");
        Check(!initializeBudget.Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name=="ActiveBudget"),"Do not publish an active budget before roster/subscription setup succeeds");
        var catalogue=pluginTypes.Single(t=>t.Name=="GauntletCatalogue");
        var prepare=catalogue.Methods.Single(m=>m.Name=="Prepare");
        Check(prepare.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GauntletBossPolicy" && m.Name=="Reason"),"Both arena pools must reject scripted mission variants before catalogue insertion");
        Check(prepare.Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name=="deathState" && f.DeclaringType.Name=="CharacterDeathBehavior"),"Death states are part of a boss's scripted identity");
        Check(prepare.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GauntletBossPolicy" && m.Name=="Released"),"Unnamed native content is excluded");
        Check(!catalogue.Methods.Any(m=>m.Name=="AddFalseSon") && !catalogue.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m && m.Name.Contains("FalseSonBossMaster")),
            "The phase-one False Son adapter crashes on its Meridian-only death state and must stay removed");
        var boss=catalogue.Methods.Single(m=>m.Name=="Boss");
        Check(boss.Parameters.Count==3 && boss.Parameters[2].Name=="previous","Paired boss selection accepts the previous card to prefer a different species");
        foreach(string ladder in new[]{"BossCostCeiling","BossCostFloor","GreatTierRange"})
            Check(boss.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name==ladder),"Champions follow the tested round ladder: "+ladder);
        var hordeBodies=new[]{catalogue}.Concat(catalogue.NestedTypes).SelectMany(t=>t.Methods).Where(m=>m.Name.Contains("HordeDeck") && m.HasBody).SelectMany(m=>m.Body.Instructions).ToArray();
        Check(hordeBodies.Any(i=>i.Operand is MethodReference m && m.Name=="HordeCostCeiling") && hordeBodies.Any(i=>i.Operand is MethodReference m && m.Name=="SpeciesWeight"),"Hordes use the tested smooth threat curve");
        // Installed native dependencies behind each scripted-encounter exclusion.
        bool Uses(string type,string owner,string member)=>Types(game).Single(t=>t.FullName==type).Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions)
            .Any(i=>i.Operand is MemberReference reference && (owner==null || reference.DeclaringType.Name==owner) && (member==null || reference.Name==member));
        Check(Uses("EntityStates.FalseSonBoss.CrystalDeathState","MeridianEventTriggerInteraction",null),"Review False Son if its death no longer needs the Meridian event");
        Check(Uses("EntityStates.FalseSonBoss.LunarGazeLeap","MeridianEventTriggerInteraction",null),"Review False Son if Lunar Gaze no longer needs the Meridian colossus");
        Check(Uses("EntityStates.VoidRaidCrab.EscapeDeath","VoidRaidGauntletController","TryOpenGauntlet"),"Review Voidling phases if escape no longer opens the Planetarium gauntlet");
        Check(Uses("RoR2.ProvidenceController",null,"PermanentCurse"),"Review Providence if its final pulse no longer curses players permanently");
        Check(Uses("EntityStates.Mimic.Monster.IsDisguisedState","Opening",".ctor"),"Review Mimic if it no longer waits for damage while disguised");
        Check(Uses("EntityStates.SolusAmalgamator.DetatchState",null,"TrySpawnObject"),"Review Amalgamator parts if they stop being boss deployables");
        Check(Uses("EntityStates.BrotherMonster.SkySpawnState","PhaseCounter",null),"Mithrix spires stay gated by the moon phase counter");
        // War Council: native AI on the player team, verified requests, quota renown.
        var companies=pluginTypes.Single(t=>t.Name=="GauntletCompanies");
        var spawn=companies.Methods.Single(m=>m.Name=="Spawn");
        foreach(string field in new[]{"teamIndexOverride","ignoreTeamMemberLimit","summonerBodyObject"})
            Check(spawn.Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name==field && f.DeclaringType.Name=="DirectorSpawnRequest"),"Hired units use native summoning: "+field);
        Check(spawn.Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name=="UseAmbientLevel"),"Hired units keep pace with the arena level");
        Check(!spawn.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name.Contains("AISkillDriver")),"Hired units keep each species' own AI drivers");
        Check(game.Types.Single(t=>t.FullName=="RoR2.CharacterBody").Methods.Single(m=>m.Name=="RecalculateStats").Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name=="UseAmbientLevel"),"Native ambient-level item still lifts minion levels");
        var request=companies.Methods.Single(m=>m.Name=="HandleRequest");
        Check(request.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="get_connectionToClient") || companies.NestedTypes.SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m && m.Name=="get_connectionToClient"),
            "Hire requests must come from the connection that owns the network user");
        Check(game.Types.Single(t=>t.FullName=="RoR2.Networking.NetworkManagerSystem").Events.Any(e=>e.Name=="onStartServerGlobal"),"Native server-start hook for the request handler");
        var defeated=pluginTypes.Single(t=>t.Name=="GauntletWaveBudget").Methods.Single(m=>m.Name=="Defeated");
        var hordeSpawn=pluginTypes.Single(t=>t.Name=="GauntletWaveBudget").Methods.Single(m=>m.Name=="Spawned");
        Check(hordeSpawn.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GauntletHordeRewards" && m.Name=="Experience") &&
            hordeSpawn.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="set_expReward"),"Quota spawns compensate native XP for the reduced horde");
        Check(!hordeSpawn.Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name=="spawnValue"),"XP compensation cannot inflate native item value");
        var renownAward=companies.Methods.Single(m=>m.Name=="Defeated");
        Check(renownAward.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GauntletWaveBudget" && m.Name=="Renown"),"Accepted horde kills keep per-owner commander affordability");
        Check(!pluginTypes.Single(t=>t.Name=="GauntletHordeRewards").Fields.Any(f=>f.IsStatic),"Fractional horde rewards cannot leak across waves or runs");
        var nativeSpawn=Types(game).SelectMany(t=>t.Methods).Where(m=>m.HasBody && m.Name.Contains("OnCardSpawned")).Single(m=>m.Body.Instructions.Any(i=>i.Operand is MethodReference call && call.Name=="set_expReward"));
        int nativeXp=nativeSpawn.Body.Instructions.ToList().FindIndex(i=>i.Operand is MethodReference m && m.Name=="set_expReward");
        int spawnEvent=nativeSpawn.Body.Instructions.ToList().FindIndex(i=>i.Operand is FieldReference f && f.Name=="onSpawnedServer");
        Check(nativeXp>=0 && spawnEvent>nativeXp,"Native director sets XP before invoking our quota spawn callback");
        var ledgerCall=defeated.Body.Instructions.First(i=>i.Operand is MethodReference m && m.Name=="Defeat");
        var awardCall=defeated.Body.Instructions.First(i=>i.Operand is MethodReference m && m.Name=="Invoke");
        Check(defeated.Body.Instructions.IndexOf(ledgerCall)<defeated.Body.Instructions.IndexOf(awardCall) && defeated.Body.ExceptionHandlers.Any(h=>h.HandlerType==ExceptionHandlerType.Catch),
            "Renown is paid once per quota member and can never interrupt round progress");
        var insignia=plugin.Resources.OfType<EmbeddedResource>().SingleOrDefault(resource=>resource.Name=="BazaarGauntlet.assets.commanders.meshbin");
        Check(insignia!=null && insignia.GetResourceData().SequenceEqual(File.ReadAllBytes(Path.Combine(root,"assets/commanders.meshbin"))),"Runtime must embed the exact reviewed insignia mesh");
        var transition=Types(game).Single(t=>t.FullName=="EntityStates.ChampRushReaper.ReaperSecondPhaseSpawnTransition");
        var transitionEnter=transition.Methods.Single(m=>m.Name=="OnEnter");
        Check(transitionEnter.Body.Instructions.Any(i=>i.Operand is string text && text=="ReaperSecondPhaseSpawnLocation"),"Review the Harvester adapter if the native phase-location requirement changes");
        Check(transitionEnter.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="ScenePropInstance" && m.Name=="FindPropByCustomName"),"Native mission Harvester phase transition still depends on a scene prop");
        Check(transition.Methods.Single(m=>m.Name=="OnExit").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="ToggleShriekParameters"),"Review native cleanup if the reported transition loop changes");
        Check(catalogue.Methods.Single(m=>m.Name=="Prepare").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="ExternalSource"),"Quick Restart must exclude deferred-destroy runtime decks/cards from discovery");
        Check(!new[]{catalogue}.Concat(catalogue.NestedTypes).SelectMany(t=>t.Methods).Where(m=>m.Name.Contains("HordeDeck")).SelectMany(m=>m.HasBody?m.Body.Instructions:Enumerable.Empty<Instruction>())
            .Any(i=>i.Operand is MethodReference m && m.DeclaringType.FullName=="UnityEngine.Object" && m.Name=="get_name"),"Horde sorting/logging must use cached names, never the destroyed-card name getter seen in the live trace");
        var nativeInitCall=initController.Body.Instructions.First(i=>i.Operand is MethodReference m && m.Name=="Invoke" && m.DeclaringType.Name=="orig_InitializeWaveController");
        var budgetCommitCall=initController.Body.Instructions.First(i=>i.Operand is MethodReference m && m.Name=="Commit" && m.DeclaringType.Name=="GauntletWaveBudget");
        Check(initController.Body.Instructions.IndexOf(nativeInitCall)<initController.Body.Instructions.IndexOf(budgetCommitCall),"Commit spawning only after native initialization returns with its defeat callback subscribed");
        var networkBook=game.Types.Single(t=>t.Name=="NetworkRuleBook");
        var setNetworkBook=networkBook.Methods.Single(m=>m.Name=="SetRuleBook");
        Check(setNetworkBook.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="SetDirtyBit") && setNetworkBook.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="Copy"),"Gold preservation must use the native networked rulebook setter, not direct mutation");
        var currency=pluginTypes.Single(t=>t.Name=="GauntletCurrency");
        var currencyRules=currency.Methods.Single(m=>m.Name=="SetRules");
        foreach(string call in new[]{"Copy","ApplyChoice","Invoke"})
            Check(currencyRules.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name==call),"Preserve incoming lobby rules and publish the corrected keep-gold rule through native assignment: "+call);
        Check(currencyRules.Body.Instructions.Any(i=>i.OpCode.Code==Code.Newobj && i.Operand is MethodReference m && m.DeclaringType.Name=="RuleBook"),"Do not mutate the shared lobby rulebook");
        var enforceGold=currency.Methods.Single(m=>m.Name=="Ensure");
        Check(enforceGold.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="SetRuleBook" && m.DeclaringType.Name=="Run"),"Repairs must publish the corrected rules to clients");
        var stateGuard=currency.Methods.Single(m=>m.Name=="ExitState");
        Check(stateGuard.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="BlockExtraction"),"Portal state guard must use the tested authoritative/accepted-exit policy");
        Check(stateGuard.Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name=="CommittedExit"),"Never affect unrelated exits or ordinary modes");
        Check(exitMethod.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GauntletCurrency" && m.Name=="Ensure"),"Both accepted portal directions must enforce gold retention before native Begin");
        var nativeExit=game.Types.Single(t=>t.Name=="SceneExitController");
        var nativeBegin=nativeExit.Methods.Single(m=>m.Name=="Begin");
        Check(nativeBegin.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="get_keepMoneyBetweenStages"),"Installed exit path no longer uses the verified gold-retention rule");
        Check(nativeExit.Methods.Single(m=>m.Name=="SetState").Body.Instructions.Any(i=>i.Operand is GenericInstanceMethod m && m.Name=="AddComponent" && m.GenericArguments.Any(t=>t.Name=="ConvertPlayerMoneyToExperience")),"Review the native extraction/collector creation path if this API changes");
        var converter=game.Types.Single(t=>t.Name=="ConvertPlayerMoneyToExperience").Methods.Single(m=>m.Name=="FixedUpdate");
        Check(converter.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="set_money") && converter.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="AwardExperience"),"Currency regression checks must cover the real native debit/XP conversion path");
        var portalLoot=pluginTypes.Single(t=>t.Name=="GauntletPortalLoot");
        Check(portalLoot.Methods.Single(m=>m.Name=="Collect" && m.Parameters.Count==1).Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="GauntletLootPolicy" && m.Name=="Sweep"),"No Bazaar display can be swept by any automatic collection path");
        Check(portalLoot.Methods.Single(m=>m.Name=="Arrived").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="Restore" && m.DeclaringType.Name=="GauntletLootPolicy"),"Queued arena rewards must stay in storage during Bazaar shopping");
        Check(!plugin.AssemblyReferences.Any(a=>a.Name=="BiggerBazaar"),"Integrated merchants cannot link to BiggerBazaar"); Check(!pluginTypes.Any(t=>t.Name=="GauntletBazaarCompatibility"),"Retired BiggerBazaar wallet hooks must not ship"); MerchantContracts.Run(game,pluginTypes,Check);         Check((float)pluginTypes.Single(t=>t.Name=="GauntletBalance").Fields.Single(f=>f.Name=="DropMultiplier").Constant==1.6f,"Compiled ten-round drop default is 1.6");
        var economy=pluginTypes.Single(t=>t.Name=="GauntletEconomy");
        var enemyGold=economy.Methods.Single(m=>m.Name=="EnemyKilled");
        int zeroed=enemyGold.Body.Instructions.ToList().FindIndex(i=>i.Operand is MethodReference m && m.Name=="set_goldReward");
        int nativeDeath=enemyGold.Body.Instructions.ToList().FindIndex(i=>i.Operand is MethodReference m && m.Name=="Invoke" && m.DeclaringType.Name=="orig_OnKilledServer");
        Check(zeroed>=0 && nativeDeath>zeroed,"All enemy death rewards are zeroed before the native gold payout, including non-quota summons");
        Check(!enemyGold.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="set_expReward" || i.Operand is FieldReference f && f.Name=="spawnValue"),"Zero enemy gold must preserve XP and spawnValue");
        Check(!economy.Methods.Any(m=>m.Name=="Settle"),"No deferred Bazaar settlement remains");
        var clearReward=economy.Methods.Single(m=>m.Name=="CompleteRound");
        Check(clearReward.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="GiveMoney" && m.DeclaringType.Name=="CharacterMaster"),"The arena clear immediately pays the ordinary run wallet");
        Check(!roundEnded.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="GiveMoney"),"The old fixed clear bonus cannot become a second payout");
        Check(roundEnded.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="CompleteRound" && m.DeclaringType.Name=="GauntletEconomy"),"Only completed rounds create income entitlement");
        var transport=pluginTypes.Single(t=>t.Name=="GauntletNetwork");
        Check(!pluginTypes.SelectMany(Flatten).SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions)
            .Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="NetworkingAPI" && m.Name=="RegisterMessageType"),"Host-only state must never register with the client-to-client R2API relay");
        var receive=transport.Methods.Single(m=>m.Name=="Receive");
        Check(receive.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="get_connection" && m.DeclaringType.Name=="NetworkClient"),"Snapshot origin must be checked against the actual host connection");
        Check(receive.Body.ExceptionHandlers.Any(h=>h.HandlerType==ExceptionHandlerType.Filter),"Malformed host packets must be contained at the decoding boundary");
        Check(transport.Methods.Single(m=>m.Name=="RegisterClient").Body.Instructions.Count(i=>i.Operand is MethodReference m && m.Name=="RegisterHandler" && m.DeclaringType.Name=="NetworkClient")==3,"State, companies and effects have client-only native handlers");
        Check(transport.Methods.Single(m=>m.Name=="Send").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="SendToAll" && m.DeclaringType.Name=="NetworkServer"),"Snapshots originate from the server transport");
        var registerServer=companies.Methods.Single(m=>m.Name=="RegisterServer");
        Check(registerServer.Body.Instructions.Count(i=>i.Operand is MethodReference m && m.Name=="RegisterHandler" && m.DeclaringType.Name=="NetworkServer")==1,"Host registers only the client command handler, never snapshot forwarding");
        Check(registerServer.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="TryGetValue"),"Server-start replay must retain an existing own handler without overwriting another mod");
        Check(request.Body.ExceptionHandlers.Any(h=>h.HandlerType==ExceptionHandlerType.Filter),"Malformed remote command packets cannot escape decode handling");
        Check(request.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="Take" && m.DeclaringType.Name=="GauntletRequestBudget"),"Bound per-connection work before request parsing");
        Check(request.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="MatchesRun"),"Same-seed restart requests must include the current run identity");
        var seedReaders=pluginTypes.SelectMany(Flatten).SelectMany(t=>t.Methods).Where(m=>m.HasBody && m.Body.Instructions.Any(i=>i.Operand is MethodReference r && r.Name=="get_seed" && r.DeclaringType.Name=="Run"))
            .Select(m=>m.DeclaringType.Name+"."+m.Name).OrderBy(n=>n).ToArray();
        Check(seedReaders.SequenceEqual(new[]{"GauntletCompanies.Reconcile","GauntletController.InitializeHost","GauntletStartingItems.Tick"}),
            "Run.seed is host-only (clients read 0); only server RNG may use it, packets use RunKey: "+string.Join(",",seedReaders));
        Check(pluginTypes.Single(t=>t.Name=="GauntletController").Methods.Single(m=>m.Name=="get_RunKey").Body.Instructions.Any(i=>i.Operand is MethodReference r && r.Name=="GetUniqueId"),
            "Packet run identity derives from the synced native run GUID");
        Check(companies.Methods.Single(m=>m.Name=="Handle").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="RefreshCapacity"),"Every host transaction enforces the current party-size cap");
        Check(companies.Methods.Single(m=>m.Name=="RefreshCapacity").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="EnforceCapacity"),"Joining players trigger forced withdrawal and refund of excess companies");
        Check(pluginTypes.Single(t=>t.Name=="GauntletSnapshot").Methods.Single(m=>m.Name=="OnReceived").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="Ensure" && m.DeclaringType.Name=="FortressBattlefield"),"Late-arriving run objects must retry client map construction");
        ExperienceContracts.Run(game,pluginTypes,Check);
        BrazierContracts.Run(game,pluginTypes,Check);
        DropContracts.Run(pluginTypes,Check);
        StarterContracts.Run(game,pluginTypes,Check);
        CompanyDamageContracts.Run(game,pluginTypes,profile,dependencies,Check);
        MetaContracts.Run(game,pluginTypes,Check);
        HudContracts.Run(game,pluginTypes,Check);
        Console.WriteLine($"PASS: {checks} installed-game/API, arena-flow, asset and metadata contracts. {cards.Length} native card keys. No Unity runtime loaded.");
    }
}
