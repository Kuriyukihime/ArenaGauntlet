using System;

namespace BazaarGauntlet
{
    // A finite horde shares one item budget. Elites can draw from it sooner,
    // but their credit/XP value cannot inflate the round's total loot.
    internal sealed class GauntletDropBudget
    {
        // Full-run baseline before the population discount below; the smaller
        // hordes plus starters model around 73 total items.
        internal const int ItemsPerPlayerAtMithrix=73,ExpectedShopItemsPerPlayer=39,ClearItemsBeforeMithrix=9,ExpectedSharedChampionItems=7;
        internal int RemainingEnemies { get; private set; }
        internal int RemainingDrops { get; private set; }
        internal int Target { get; }
        internal static double Expected(int round,int players,float multiplier,int cycle=1)
        {
            if(float.IsNaN(multiplier) || multiplier<=0)return 0;
            int bounded=Math.Max(1,Math.Min(GauntletPlan.RoundsPerCycle,round));
            // Nine shop breaks and nine boss-clear items precede Mithrix.
            // Champion bonus rolls are party-shared, so reserve those once.
            double cycleItems=(ItemsPerPlayerAtMithrix-ExpectedShopItemsPerPlayer-ClearItemsBeforeMithrix)*(double)Math.Max(1,players)-ExpectedSharedChampionItems;
            double weight=1+3d*(bounded-1)/(GauntletPlan.RoundsPerCycle-1);
            double population=GauntletBalance.HordePopulationRatio(bounded,cycle,players);
            return Math.Max(0,cycleItems)*weight/(GauntletPlan.RoundsPerCycle*2.5d)*(multiplier/GauntletBalance.DropMultiplier)*population;
        }
        internal GauntletDropBudget(int enemies,double desired,double roundingRoll)
        {
            RemainingEnemies=Math.Max(0,enemies);
            if(double.IsNaN(desired) || desired<=0)desired=0;
            desired=Math.Min(RemainingEnemies,desired);
            int whole=(int)Math.Floor(desired);
            Target=RemainingDrops=whole+(roundingRoll<desired-whole?1:0);
        }
        internal bool Roll(bool elite,double roll)
        {
            if(RemainingEnemies<=0)return false;
            double chance=(double)RemainingDrops/RemainingEnemies*(elite?1.25:1);
            bool drop=RemainingDrops>0 && (RemainingDrops>=RemainingEnemies || roll<chance);
            RemainingEnemies--;
            if(drop)RemainingDrops--;
            return drop;
        }
    }
}
