using BepInEx.Configuration;

namespace BazaarGauntlet
{
    internal static class GauntletConfig
    {
        internal static ConfigEntry<int> StartingLevelAdvantage, FinalLevelAdvantage, EnemyLimit;
        internal static ConfigEntry<float> DropMultiplier, ArenaRadius, MinimumScaling;
        internal static ConfigEntry<bool> AllowEndlessContinuation, CollectPortalLoot, ShowHud;
        internal static ConfigEntry<float> FortressGoldMultiplier;
        internal static ConfigEntry<int> FortressStartingGold;
        internal static ConfigEntry<int> BazaarItemsPerRound;
        internal static ConfigEntry<int> MaxCompanies;
        internal static ConfigEntry<float> RenownMultiplier;
        internal static ConfigEntry<KeyboardShortcut> CommandMenuKey;
        private const string Host = "Host setting; captured for the next run. ";
        internal static void Bind(ConfigFile file)
        {
            var revision=file.Bind("Internal","ReviewBalanceRevision",0,"Internal one-time settings migrations.");
            bool upgradeRewards=GauntletLootPolicy.NeedsRewardMigration(revision.Value);
            StartingLevelAdvantage = file.Bind("Enemy scaling", "StartingLevelAdvantage", 0,
                new ConfigDescription(Host + "Minimum enemy levels above the player team on round 1. Higher difficulty levels are preserved.", new AcceptableValueRange<int>(0, 100)));
            FinalLevelAdvantage = file.Bind("Enemy scaling", "FinalLevelAdvantage", 5,
                new ConfigDescription(Host + "Minimum enemy levels above the player team by round 10. Never lower than StartingLevelAdvantage.", new AcceptableValueRange<int>(0, 100)));
            MinimumScaling = file.Bind("Enemy scaling", "MinimumDifficultyScaling", GauntletBalance.TyphoonScaling,
                new ConfigDescription(Host + "Minimum clock-based difficulty growth. Typhoon uses 3.5; Monsoon uses 3. Higher selected difficulty is preserved. Set 0 to follow only the selected difficulty.", new AcceptableValueRange<float>(0, 10)));
            EnemyLimit = file.Bind("Combat", "SimultaneousEnemyLimit", 80,
                new ConfigDescription(Host + "Ceiling for the growing live-enemy limit: the existing opening-to-final curve is compressed into ten rounds. Solo starts at 15; multiplayer and later cycles add pressure. Remaining enemies wait for space.", new AcceptableValueRange<int>(4, 160)));
            ArenaRadius = file.Bind("Combat", "ArenaRadius", 140f,
                new ConfigDescription(Host + "Maximum assault-spawn distance around the arena center, in metres. Default approaches are 80–140m away. Ground spawns require a walkable route across the open colosseum floor. No close-range placement fallback or damaging fog.", new AcceptableValueRange<float>(40, 160)));
            DropMultiplier = file.Bind("Loot", "EnemyDropMultiplier", GauntletBalance.DropMultiplier,
                new ConfigDescription(Host + "Scales a finite horde-item allowance. Default 1.6 gives about 14.8 horde items solo over ten rounds; normal buying, starters and boss rewards put full runs around 73 items per player. Later horde size, director credits and the item allowance shrink by the same percentage. The allowance rounds randomly, then drops across the completed horde; elites have a 25% higher chance within it. 0 disables enemy drops; guaranteed boss rewards remain. Rarity uses the native drop table.", new AcceptableValueRange<float>(0, 12)));
            CollectPortalLoot = file.Bind("Loot", "CollectLootBetweenRounds", true, Host + "Collect free enemy drops at round clear. Merchant purchases and scrapper output stay on the ground for manual pickup; choice rewards remain yours to choose.");
            ShowHud = file.Bind("Presentation", "ShowArenaHud", true, "Local setting. Show the compact top-center round/progress strip.");
            AllowEndlessContinuation = file.Bind("Progression", "AllowEndlessContinuation", false,
                Host + "After round 10, shop at the alcoves, then ready up for another 10-round cycle. When disabled, round 10 ends in victory.");
            FortressGoldMultiplier=file.Bind("Castle", "GoldRewardMultiplier",GauntletBalance.GoldMultiplier,new ConfigDescription("Legacy saved value retained for compatibility. Arena enemies award no gold; the clear reward uses BazaarItemsPerRound.",new AcceptableValueRange<float>(1,12)));
            FortressStartingGold=file.Bind("Castle", "StartingGold",GauntletBalance.StartingGold,new ConfigDescription(Host+"Starting gold per connected player, granted once per run.",new AcceptableValueRange<int>(0,6000)));
            BazaarItemsPerRound=file.Bind("Economy","BazaarItemsPerRound",3,new ConfigDescription(Host+"Pay this many average posted item prices plus half an average equipment price and a quarter of an average drone price at round clear, into the ordinary run wallet. Default 3 items; around 14% extra gold at default prices. Prices compound with difficulty. Enemies give no base gold. Set 0 to disable the entire clear reward; enemy gold stays zero.",new AcceptableValueRange<int>(0,12)));
            MaxCompanies=file.Bind("War Council","MaxCompanies",3,new ConfigDescription(Host+"Additional ceiling on companies per player. Party-size caps are 3 solo, 2 with 2-3 players, and 1 with 4+ players. Excess companies are withdrawn with a renown refund when the party grows.",new AcceptableValueRange<int>(1,3)));
            RenownMultiplier=file.Bind("War Council","RenownMultiplier",1f,new ConfigDescription(Host+"Multiplier on renown earned from quota kills and champions. 0 disables renown income.",new AcceptableValueRange<float>(0,5)));
            CommandMenuKey=file.Bind("War Council","CommandMenuKey",new KeyboardShortcut(UnityEngine.KeyCode.B),"Local setting. Opens and closes the War Council panel under the round display.");
            if (revision.Value < 2)
            {
                // Existing 0.1 configs otherwise keep the rejected opening
                // values despite the new defaults. Preserve customized leads.
                if (StartingLevelAdvantage.Value == 5 && FinalLevelAdvantage.Value == 10)
                { StartingLevelAdvantage.Value = 0; FinalLevelAdvantage.Value = 5; }
                if (EnemyLimit.Value == 40) EnemyLimit.Value = 80;
                revision.Value = 2;
            }
            if(revision.Value<3)
            {
                // Move the earlier review defaults to the requested distant
                // approach. Other customized caps are left alone.
                if(ArenaRadius.Value==75f || ArenaRadius.Value==95f)ArenaRadius.Value=140f;
                revision.Value=3;
            }
            if(revision.Value<4)
            {
                // Existing saved values get the requested relative increase
                // once. Fresh defaults already include it and are not doubled.
                if(upgradeRewards)
                {
                    DropMultiplier.Value=GauntletLootPolicy.Boost(DropMultiplier.Value);
                    FortressGoldMultiplier.Value=GauntletLootPolicy.Boost(FortressGoldMultiplier.Value);
                    FortressStartingGold.Value=GauntletLootPolicy.BoostGold(FortressStartingGold.Value);
                }
                revision.Value=4;
            }
            if(revision.Value<5)
            {
                DropMultiplier.Value=GauntletLootPolicy.TenRoundDrops(DropMultiplier.Value);
                revision.Value=5;
            }
            if(revision.Value<6)
            {
                // Endless is now opt-in; saved configs still carry the old on default.
                AllowEndlessContinuation.Value=false;
                revision.Value=6;
            }
        }
    }
}
