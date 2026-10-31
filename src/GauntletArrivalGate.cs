namespace BazaarGauntlet
{
    // Unscaled arrival grace, consumed once. A late/loading player resets the
    // grace; the host is the only caller and owns starting the native wave.
    internal sealed class GauntletArrivalGate
    {
        private double readySince=double.NaN;
        private bool started;
        internal void Reset(){readySince=double.NaN;started=false;}
        internal bool Tick(double now,bool ready)
        {
            if(started)return false;
            if(!ready){readySince=double.NaN;return false;}
            if(double.IsNaN(readySince))readySince=now;
            if(now-readySince<3)return false;
            started=true;return true;
        }
    }
}
