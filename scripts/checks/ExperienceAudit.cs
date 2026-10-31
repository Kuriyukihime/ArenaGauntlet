using System;
using BazaarGauntlet;

internal static class ExperienceAudit
{
    internal static void Run(Action<bool,string> check)
    {
        var xp=new GauntletExperiencePolicy();
        check(xp.Scale(1)==1 && xp.Scale(0)==0 && xp.Scale(1)==2,"Half-XP carry grants exactly 50% extra across tiny kill rewards");
        check(new GauntletExperiencePolicy().Scale(1)==1,"A fresh run cannot inherit another run's fractional XP");
        foreach(int seed in new[]{1,17,255})
        {
            xp=new GauntletExperiencePolicy();var random=new Random(seed);ulong original=0,awarded=0;
            for(int kill=0;kill<2000;kill++)
            {
                ulong amount=(ulong)random.Next(0,10000);original+=amount;awarded+=xp.Scale(amount);
                check(awarded==original+original/2,"Rounding is independent of the number/order of kill payouts");
            }
        }
        xp=new GauntletExperiencePolicy();
        check(xp.Scale(ulong.MaxValue)==ulong.MaxValue && xp.Scale(0)==0,"Extreme modded awards saturate without overflow");
        xp=new GauntletExperiencePolicy();
        check(xp.Scale(1000)==1500 && xp.Scale(1000000)==1500000,"Ordinary and large enemy awards receive the same multiplier");
        Console.WriteLine("Experience: 1.5x final combat XP, fractional carry per run, zero rewards and overflow checked.");
    }
}
