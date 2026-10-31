using System;

namespace BazaarGauntlet
{
    internal static class GauntletBalance
    {
        internal const float TyphoonScaling = 3.5f;
        internal const float CreditPressure = 1.4f;
        internal const float RewardMultiplier = .8f;
        internal const float DropMultiplier = 1.6f;
        internal const float GoldMultiplier=3.6f;
        internal const int StartingGold=180,RoundGold=48;
        internal const float BossItemChance=.24f;
        // Base population/credit uplift. Later population and credit budgets
        // taper together; the established live-cap progression stays intact.
        internal const double HordeScale=1.25;
        private static double Bounded(int round)=>GauntletPlan.OriginalRound(round);
        // Before original round 14 the live cap and spawn budget get up to 50%
        // more. That point now falls between rounds seven and eight.
        internal static double EarlyBoost(int round)=>1+.5*Math.Max(0,14-Bounded(round))/13d;
        internal static int HordeSize(int round, int cycle, int players)
        {
            if(round<=4)return HordeRewardSlots(round,cycle,players);
            // Keep the opening, then taper growth toward eighty solo enemies.
            double start=(18+4*(Bounded(4)-1))*HordeScale;
            double progress=(Bounded(round)-Bounded(4))/(Bounded(GauntletPlan.RoundsPerCycle)-Bounded(4));
            double solo=start+(80-start)*progress+12d*HordeScale*Math.Max(0,cycle-1);
            return (int)Math.Min(500,Math.Ceiling(solo*(.5+.5*Math.Max(1,players))));
        }
        // The former population still defines XP/renown pacing and loot per
        // enemy. Smaller later hordes must not delay levels or commander hires.
        internal static int HordeRewardSlots(int round, int cycle, int players)
        {
            double r = Bounded(round);
            double count = (18 + 4 * (r - 1) + 12d * Math.Max(0, cycle - 1)) * (.5 + .5 * Math.Max(1, players)) * HordeScale;
            return (int)Math.Min(500, Math.Ceiling(count));
        }
        internal static double HordePopulationRatio(int round,int cycle,int players)=>(double)HordeSize(round,cycle,players)/HordeRewardSlots(round,cycle,players);
        internal static int ConcurrentLimit(int round, int cycle, int players, int ceiling)
        {
            long count = (long)Math.Ceiling((8 + 4d * (Bounded(round) - 1) +
                8L * Math.Max(0, cycle - 1) + 2L * (Math.Max(1, players) - 1)) * HordeScale);
            count = (long)Math.Round(count * EarlyBoost(round));
            return (int)Math.Max(4, Math.Min(Math.Max(4, ceiling), count));
        }
        // Heavier species phase in by 27% of director cost per round instead of
        // two cliffs (40 -> 100 -> everything at round five). Every native
        // ordinary enemy is eligible by original round twelve (now seven);
        // later cycles start open.
        internal static int HordeCostCeiling(int round,int cycle)=>cycle>1?int.MaxValue:(int)Math.Round(40*Math.Pow(1.27,Bounded(round)-1));
        // Fodder stays common while heavier species become a larger share of
        // each later horde. Weight is per successful spawn, scaled by 100.
        internal static int SpeciesWeight(int cost,int round,int cycle,bool themed)
        {
            double exponent=cycle>1?.35:.8-.45*(Bounded(round)-1)/19d;
            double weight=Math.Pow(40d/Math.Max(5,cost),exponent)*(themed?3:1);
            return (int)Math.Max(1,Math.Min(100000,Math.Round(weight*100)));
        }
        // Champions follow the native director's own cost bands: 600 opening
        // bosses, 800 standard, 1150 Grandparent, 2000 Scavenger, then all.
        internal static int BossCostCeiling(int round,int cycle)=>cycle>1 || Bounded(round)>=16?int.MaxValue:Bounded(round)>=11?2000:Bounded(round)>=6?1150:Bounded(round)>=3?800:600;
        internal static int BossCostFloor(int round,int cycle)=>cycle>1 || Bounded(round)>=11?800:Bounded(round)>=8?700:0;
        internal static int BossCost(int cardCost,float baseHealth)=>cardCost>0?cardCost:(int)Math.Max(600,Math.Round(Math.Max(0,baseHealth)*.3/50)*50);
        internal static bool OpeningBoss(float baseHealth,float baseDamage)=>baseHealth<=2800 && baseDamage<=40;
        // Major encounters escalate: Twisted Scavenger, Voidling, then Mithrix.
        internal static int GreatTier(string identity)
        {
            string name=identity??"";
            if(name.IndexOf("ScavLunar",StringComparison.OrdinalIgnoreCase)>=0)return 1;
            if(name.IndexOf("Brother",StringComparison.OrdinalIgnoreCase)>=0)return 3;
            return 2;
        }
        internal static void GreatTierRange(int round,int cycle,out int minimum,out int maximum)
        {
            if(round>=GauntletPlan.RoundsPerCycle){minimum=maximum=3;return;}
            if(cycle>1){minimum=2;maximum=3;return;}
            // Round five retains its Twisted Scavenger; ten is the final major.
            minimum=maximum=1;
        }
        internal static float InitialCredits(int round, int players, int cycle=1) =>
            (float)((20 + 2d * (Bounded(round) - 1)) * (.5 + .5 * Math.Max(1, players)) * HordeScale * EarlyBoost(round)*HordePopulationRatio(round,cycle,players));
        internal static bool AdvancesDifficultyStage(int completedRound) => completedRound > 0 && completedRound<=GauntletPlan.RoundsPerCycle && completedRound % (GauntletPlan.RoundsPerCycle/5) == 0;
        internal static float ClockMultiplier(float selectedScaling, float minimumScaling) =>
            selectedScaling > 0 ? Math.Max(1f, minimumScaling / selectedScaling) : 1f;
        internal static float CreditRate(int round, int players, float difficulty, int cycle=1) =>
            (float)((2.5 + .5 * Bounded(round)) * (.5 + .5 * Math.Max(1, players)) *
            (1f + .4f * Math.Max(1f, difficulty)) * CreditPressure * HordeScale * EarlyBoost(round)*HordePopulationRatio(round,cycle,players));
    }
}
