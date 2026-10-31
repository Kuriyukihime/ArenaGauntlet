using BepInEx;
using BepInEx.Logging;
using R2API.Utils;
using RoR2;

namespace BazaarGauntlet
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("___riskofthunder.RoR2BepInExPack", "1.49.0")]
    [BepInDependency("com.bepis.r2api.content_management")]
    [BepInDependency("com.bepis.r2api.prefab")]
    [BepInDependency("com.bepis.r2api.language")]
    [BepInDependency("com.bepis.r2api.networking")]
    [BepInDependency("com.bepis.r2api.recalculatestats", "1.7.0")]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    public sealed class GauntletPlugin : BaseUnityPlugin
    {
        // Preserve the plugin identity so existing config and duplicate-plugin detection carry over.
        internal const string Guid = "Kuriyukihime.BazaarGauntlet";
        internal const string Name = "ArenaGauntlet";
        internal const string Version = "1.0.2";
        internal static ManualLogSource Log;
        private void Awake()
        {
            Log = Logger;
            GauntletConfig.Bind(Config);
            GauntletMode.Register();
            GauntletShop.Register();
            GauntletObjective.Register();
            GauntletCompanyDamage.Register();
            GauntletRuntime.Install();
            GauntletCompanies.Install();
            GauntletMetaNetwork.Install();
            GauntletMetaprogression.Install();
            GauntletAscension.Install();
            GauntletLootInstancing.Install();
            GauntletOptions.Register();
            Logger.LogInfo("ArenaGauntlet registered. Other game modes retain their normal rules.");
        }
        private float nextHudScan;
        private void Update()
        {
            GauntletMetaNetwork.Tick();
            if (UnityEngine.Time.unscaledTime < nextHudScan) return;
            nextHudScan = UnityEngine.Time.unscaledTime + 1;
            GauntletHud.Attach();
            GauntletCareerMenu.Attach();
        }
    }
}
