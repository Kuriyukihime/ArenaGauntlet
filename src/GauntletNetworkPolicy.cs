using System;

namespace BazaarGauntlet
{
    internal static class GauntletNetworkPolicy
    {
        internal const int MaxPacketBytes=32768,MaxRequestBytes=64,MaxTheme=256;
        internal static int CompanyCapacity(int players,int configured)=>Math.Max(1,Math.Min(configured,players<=1?3:players<=3?2:1));
        internal static bool MatchesRun(uint current,ulong seed,uint requested,ulong requestedSeed)=>current!=0 && current==requested && seed==requestedSeed;
        internal static bool Finite(float value)=>!float.IsNaN(value) && !float.IsInfinity(value);
        // Seconds a loaded player has to deliver a career before the host freezes a fresh one for the run.
        internal const float CareerGrace=8;
        // A player still loading the scene cannot send yet, so their grace period restarts until they finish.
        internal static bool CareerOverdue(ref float since,bool loading,float now)
        {
            if(loading || float.IsNaN(since)){since=now;return false;}
            return now-since>=CareerGrace;
        }
    }
    // A small burst accommodates split-screen users while bounding work from
    // any one remote connection before parsing or touching the game ledger.
    internal sealed class GauntletRequestBudget
    {
        private double tokens=4,last=double.NaN;
        internal bool Take(double now)
        {
            if(double.IsNaN(now) || double.IsInfinity(now))return false;
            if(!double.IsNaN(last))tokens=Math.Min(4,tokens+Math.Max(0,now-last)*8);
            last=now;
            if(tokens<1)return false;
            tokens--;return true;
        }
    }
}
