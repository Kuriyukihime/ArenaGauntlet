using System;

namespace BazaarGauntlet
{
    // A wave does not own progression until its entire native initialization
    // has returned successfully. Failed attempts can retry the same slot.
    internal sealed class GauntletEncounterGate<T> where T:class
    {
        internal int Cycle {get;private set;}
        internal int Round {get;private set;}
        internal GauntletPhase Phase {get;private set;}
        internal T Current {get;private set;}
        internal bool Pending {get;private set;}
        internal bool Committed {get;private set;}
        internal bool Cleared {get;private set;}
        internal bool CanRetry {get;private set;}
        internal int NativeIndex=>(int)Math.Min(int.MaxValue-1,((long)Cycle-1)*GauntletPlan.RoundsPerCycle*2+(Round-1)*2+(Phase==GauntletPhase.Boss?2:1));
        internal bool TryBegin(int cycle,int round,GauntletPhase phase)
        {
            if(cycle<1 || round<1 || round>GauntletPlan.RoundsPerCycle || (phase!=GauntletPhase.Horde && phase!=GauntletPhase.Boss) || Pending || Current!=null && !Cleared)return false;
            if(Cycle==0){if(phase!=GauntletPhase.Horde)return false;}
            else if(cycle!=Cycle || round!=Round || !(CanRetry && phase==Phase || Cleared && Phase==GauntletPhase.Horde && phase==GauntletPhase.Boss))return false;
            Cycle=cycle;Round=round;Phase=phase;Current=null;Pending=true;Committed=false;Cleared=false;CanRetry=false;return true;
        }
        internal bool Attach(T wave)
        {if(!Pending || Current!=null || wave==null)return false;Current=wave;return true;}
        internal bool Owns(T wave)=>wave!=null && ReferenceEquals(Current,wave);
        internal bool Commit(T wave)
        {if(!Pending || !Owns(wave))return false;Pending=false;Committed=true;return true;}
        internal bool Clear(T wave)
        {if(!Committed || Cleared || !Owns(wave))return false;Cleared=true;return true;}
        internal bool Fail(T wave)
        {
            if(Cleared || Current!=null && !Owns(wave) || Current==null && (!Pending || wave!=null))return false;
            Current=null;Pending=false;Committed=false;CanRetry=true;return true;
        }
        internal void Reset(){Cycle=Round=0;Current=null;Pending=Committed=Cleared=CanRetry=false;Phase=GauntletPhase.Idle;}
    }
    internal static class GauntletCatalogueRules
    {
        internal static bool ExternalSource(string name)=>!string.IsNullOrEmpty(name) && !name.StartsWith("BazaarGauntlet",StringComparison.Ordinal);
    }
}
