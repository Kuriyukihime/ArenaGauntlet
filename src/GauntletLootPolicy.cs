using System;

namespace BazaarGauntlet
{
    internal static class GauntletLootPolicy
    {
        internal static bool Sweep(bool bazaar)=>!bazaar;
        internal static bool Restore(bool bazaar)=>!bazaar;
        internal static bool NeedsRewardMigration(int revision)=>revision>0 && revision<4;
        internal static float Boost(float value)=>(float)Math.Round(value*1.2d,6);
        internal static int BoostGold(int value)=>(int)Math.Round(value*1.2d,MidpointRounding.AwayFromZero);
        // Only migrate the previous default. Keep custom and disabled rates.
        internal static float TenRoundDrops(float value)=>Math.Abs(value-.72f)<.00001f?GauntletBalance.DropMultiplier:value;
    }
}
