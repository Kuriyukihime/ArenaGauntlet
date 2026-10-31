using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using RiskOfOptions;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;

namespace BazaarGauntlet
{
    internal static class GauntletOptions
    {
        internal static void Register()
        {
            if (Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions")) AddOptions();
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AddOptions()
        {
            const string guid = GauntletPlugin.Guid, name = GauntletPlugin.Name;
            ModSettingsManager.SetModDescription("Gladiator combat in a grand colosseum, with merchant alcoves between rounds. Host balance settings apply to the next run; clients follow the host. The opening fight starts after arrival; ready up at the brazier for later rounds. Earn renown from kills and spend it in the War Council (B) to hire allied companies. HUD visibility and the War Council key apply locally.", guid, name);
            ModSettingsManager.AddOption(new IntSliderOption(GauntletConfig.StartingLevelAdvantage, new IntSliderConfig { min = 0, max = 100 }), guid, name);
            ModSettingsManager.AddOption(new IntSliderOption(GauntletConfig.FinalLevelAdvantage, new IntSliderConfig { min = 0, max = 100 }), guid, name);
            ModSettingsManager.AddOption(new StepSliderOption(GauntletConfig.MinimumScaling, new StepSliderConfig { min = 0, max = 10, increment = .1f }), guid, name);
            ModSettingsManager.AddOption(new IntSliderOption(GauntletConfig.EnemyLimit, new IntSliderConfig { min = 4, max = 160 }), guid, name);
            ModSettingsManager.AddOption(new StepSliderOption(GauntletConfig.ArenaRadius, new StepSliderConfig { min = 40, max = 160, increment = 5 }), guid, name);
            ModSettingsManager.AddOption(new StepSliderOption(GauntletConfig.DropMultiplier, new StepSliderConfig { min = 0, max = 12, increment = .01f }), guid, name);
            ModSettingsManager.AddOption(new CheckBoxOption(GauntletConfig.AllowEndlessContinuation), guid, name);
            ModSettingsManager.AddOption(new CheckBoxOption(GauntletConfig.CollectPortalLoot), guid, name);
            ModSettingsManager.AddOption(new CheckBoxOption(GauntletConfig.ShowHud), guid, name);
            ModSettingsManager.AddOption(new IntSliderOption(GauntletConfig.FortressStartingGold,new IntSliderConfig{min=0,max=6000}),guid,name);
            ModSettingsManager.AddOption(new IntSliderOption(GauntletConfig.BazaarItemsPerRound,new IntSliderConfig{min=0,max=12}),guid,name);
            ModSettingsManager.AddOption(new IntSliderOption(GauntletConfig.MaxCompanies,new IntSliderConfig{min=1,max=3}),guid,name);
            ModSettingsManager.AddOption(new StepSliderOption(GauntletConfig.RenownMultiplier,new StepSliderConfig{min=0,max=5,increment=.1f}),guid,name);
            ModSettingsManager.AddOption(new KeyBindOption(GauntletConfig.CommandMenuKey),guid,name);
        }
    }
}
