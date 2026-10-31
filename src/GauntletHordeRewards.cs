using System;
using System.Collections.Generic;

namespace BazaarGauntlet
{
    // One committed horde owns its XP carry and each player's renown carry.
    // Integer division preserves fractional rewards without per-kill inflation.
    internal sealed class GauntletHordeRewards
    {
        private readonly ulong numerator,denominator;
        private ulong experienceRemainder;
        private readonly Dictionary<ulong,ulong> renownRemainders=new Dictionary<ulong,ulong>();
        internal GauntletHordeRewards(int previousEnemies,int enemies)
        {numerator=(ulong)Math.Max(1,previousEnemies);denominator=(ulong)Math.Max(1,enemies);}
        private ulong Scale(ulong amount,ref ulong remainder)
        {
            ulong whole=amount/denominator;
            if(whole>ulong.MaxValue/numerator){remainder=0;return ulong.MaxValue;}
            ulong fraction=(amount%denominator)*numerator+remainder;
            remainder=fraction%denominator;
            ulong extra=fraction/denominator,result=whole*numerator;
            return result>ulong.MaxValue-extra?ulong.MaxValue:result+extra;
        }
        internal uint Experience(uint amount)=>(uint)Math.Min(uint.MaxValue,Scale(amount,ref experienceRemainder));
        internal long Renown(ulong player,long amount)
        {
            if(amount<=0)return 0;
            renownRemainders.TryGetValue(player,out ulong remainder);
            long result=(long)Math.Min((ulong)long.MaxValue,Scale((ulong)amount,ref remainder));
            renownRemainders[player]=remainder;return result;
        }
    }
}
