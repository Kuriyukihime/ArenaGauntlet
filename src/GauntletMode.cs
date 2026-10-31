using System;
using R2API;
using RoR2;
using RoR2.ContentManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarGauntlet
{
    internal static class GauntletMode
    {
        internal const string PrefabName = "BazaarGauntletRun";
        internal static GameObject Prefab;
        internal static bool IsActive(Run run) => run && run.gameObject.name.StartsWith(PrefabName, StringComparison.Ordinal);
        internal static void Register()
        {
            LanguageAPI.Add("BAZAAR_GAUNTLET_NAME", "ArenaGauntlet");
            LanguageAPI.Add("BAZAAR_GAUNTLET_DESCRIPTION", "Battle through ten rounds in a Greek stone arena. Defeat mixed hordes and champions, hire allied companies, then shop at the south-gate merchant alcoves. Spend Glory through Arena Career on the selection screen to improve your starting gear, companies and free merchant purchases. Unlock optional Ascensions for greater challenges. Requires Survivors of the Void to host.");
            var asset = new AssetReferenceT<GameObject>(RoR2BepInExPack.GameAssetPathsBetter.RoR2_DLC1_GameModes_InfiniteTowerRun.InfiniteTowerRun_prefab);
            var source = asset.LoadAssetAsync().WaitForCompletion();
            if (!source || !source.GetComponent<InfiniteTowerRun>()) throw new InvalidOperationException("Native Simulacrum run prefab could not be loaded.");
            Prefab = PrefabAPI.InstantiateClone(source, PrefabName);
            var run = Prefab.GetComponent<InfiniteTowerRun>();
            run.nameToken = "BAZAAR_GAUNTLET_NAME";run.userPickable = true;
            // Run.InstantiateUi must return a real HUD subtree. Native late joins
            // and timer/UI extensions dereference it during HUD.Awake; null can
            // abort setup after the entire normal HUD has been hidden.
            run.uiPrefab = FortressAssets.Load<GameObject>("ClassicRunInfoHudPanel_prefab");
            if(!run.uiPrefab)throw new InvalidOperationException("Native run HUD prefab could not be loaded.");
            NativeAccess.Set(run, "stageTransitionPeriod", 2);
            NativeAccess.Set(run, "interactableCredits", 0);
            // The ward coordinates the fight; this mode has no damaging fog.
            // Ordinary stages contain spawn anchors that are not Simulacrum
            // safe zones, and a missing/late ward must never damage arrivals.
            NativeAccess.Set(run, "fogDamagePrefab", null);
            NativeAccess.Set(run, "spawnMaxRadius", 12f);
            NativeAccess.Set(run, "enemyItemPeriod", int.MaxValue);
            NativeAccess.Set(run, "blacklistedItems", Array.Empty<ItemDef>());
            NativeAccess.Set(run, "blacklistedTags", Array.Empty<ItemTag>());
            GauntletWaves.ConfigurePrefab(run);
            Prefab.AddComponent<GauntletController>();
            ContentAddition.AddGameMode(Prefab, "Fight through 10 arena rounds, with major champions on rounds 5 and 10. Shop between fights, then ready up at the brazier. Open Arena Career on the survivor-selection screen for permanent upgrades and optional Ascensions. The endless setting controls continuation after round 10. Requires Survivors of the Void to host.");
        }
    }
}
