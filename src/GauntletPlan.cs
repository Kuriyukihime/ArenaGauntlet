using System;

namespace BazaarGauntlet
{
    internal enum GauntletBossKind : byte { Ordinary, Great, Final }
    internal enum GauntletPhase : byte
    {
        Idle, LoadingArena, Horde, Boss, AwaitingExit, FinalDecision, LoadingBazaar, Shopping, Won, Lost
    }

    internal static class GauntletPlan
    {
        internal const int RoundsPerCycle = 10;
        // Sample the established 20-round balance at the same relative position.
        // Endpoints remain exactly old round 1 and old round 20.
        internal static double OriginalRound(int round)=>1+19d*(Math.Max(1,Math.Min(RoundsPerCycle,round))-1)/(RoundsPerCycle-1);
        internal static int UnlockRound(int originalRound)=>1+(int)Math.Ceiling((Math.Max(1,Math.Min(20,originalRound))-1)*(RoundsPerCycle-1)/19d);
        internal static int BossCount(int round)=>BossAt(round)==GauntletBossKind.Ordinary && round>5?2:1;
        internal static bool ShouldMarkEnemies(int living,bool allSpawned)=>allSpawned && living>0 && living<=5;
        internal static GauntletBossKind BossAt(int round)
        {
            if (round < 1 || round > RoundsPerCycle) throw new ArgumentOutOfRangeException(nameof(round));
            return round == RoundsPerCycle ? GauntletBossKind.Final : round % 5 == 0 ? GauntletBossKind.Great : GauntletBossKind.Ordinary;
        }
        internal static int LevelAdvantage(int round, int starting = 0, int ending = 5)
        {
            int bounded = Math.Max(1, Math.Min(RoundsPerCycle, round));
            starting = Math.Max(0, starting); ending = Math.Max(starting, ending);
            return starting + (int)((long)(ending - starting) * (bounded - 1) / (RoundsPerCycle - 1));
        }
        internal static int EnemyLevelFloor(int nativeLevel, int playerTeamLevel, int round, int starting = 0, int ending = 5)
        {
            long adapted = Math.Max(1, playerTeamLevel) + (long)LevelAdvantage(round, starting, ending);
            return (int)Math.Min(int.MaxValue, Math.Max(Math.Max(1, nativeLevel), adapted));
        }
    }

    // Only the server mutates this coordinator. Native waveIndex is not a round
    // counter: a round contains two encounters, followed by a Bazaar trip.
    internal sealed class GauntletFlow
    {
        internal int Round { get; private set; }
        internal int Cycle { get; private set; }
        internal ulong Revision { get; private set; }
        internal GauntletPhase Phase { get; private set; }
        internal GauntletBossKind BossKind => GauntletPlan.BossAt(Round);
        private bool Transition(GauntletPhase expected, GauntletPhase next)
        {
            if (Phase != expected) return false;
            Phase = next; Revision++; return true;
        }
        internal bool Start()
        {
            if (Phase != GauntletPhase.Idle) return false;
            Round = 1; Cycle = 1; return Transition(GauntletPhase.Idle, GauntletPhase.LoadingArena);
        }
        internal bool ArenaLoaded() => Transition(GauntletPhase.LoadingArena, GauntletPhase.Horde);
        internal bool HordeCleared() => Transition(GauntletPhase.Horde, GauntletPhase.Boss);
        internal bool BossCleared() => Transition(GauntletPhase.Boss,
            Round == GauntletPlan.RoundsPerCycle ? GauntletPhase.FinalDecision : GauntletPhase.AwaitingExit);
        internal bool EnterBazaar()=>Transition(GauntletPhase.AwaitingExit,GauntletPhase.LoadingBazaar);
        internal bool BazaarLoaded()=>Transition(GauntletPhase.LoadingBazaar,GauntletPhase.Shopping);
        internal bool ReturnToArena()
        {
            if(Phase!=GauntletPhase.Shopping)return false;
            if(Round==GauntletPlan.RoundsPerCycle){Round=1;Cycle++;}else Round++;
            return Transition(GauntletPhase.Shopping,GauntletPhase.LoadingArena);
        }
        internal bool ClaimVictory() => Transition(GauntletPhase.FinalDecision, GauntletPhase.Won);
        internal bool ContinueThroughBazaar()=>Transition(GauntletPhase.FinalDecision,GauntletPhase.AwaitingExit);
        internal bool NextInPlace()
        {
            if(Phase!=GauntletPhase.AwaitingExit)return false;
            if(Round==GauntletPlan.RoundsPerCycle){Round=1;Cycle++;}else Round++;
            return Transition(GauntletPhase.AwaitingExit,GauntletPhase.Horde);
        }
        internal bool Lose()
        {
            if (Phase == GauntletPhase.Idle || Phase == GauntletPhase.Won || Phase == GauntletPhase.Lost) return false;
            Phase = GauntletPhase.Lost; Revision++; return true;
        }
    }
}
