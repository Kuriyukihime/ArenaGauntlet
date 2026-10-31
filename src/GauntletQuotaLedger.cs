using System;
using System.Collections.Generic;

namespace BazaarGauntlet
{
    internal sealed class GauntletQuotaLedger<T>
    {
        private readonly HashSet<T> active = new HashSet<T>();
        internal int Target { get; }
        internal int Spawned { get; private set; }
        internal int Defeated { get; private set; }
        internal bool Full => Spawned >= Target;
        internal bool Complete => Full && Defeated==Target;
        internal GauntletQuotaLedger(int target) { Target = Math.Max(1, target); }
        internal bool Spawn(T enemy)
        {
            if (Full || !active.Add(enemy)) return false;
            Spawned++;return true;
        }
        internal bool Defeat(T enemy)
        {
            if (!active.Remove(enemy)) return false;
            Defeated++;return true;
        }
        internal bool Despawn(T enemy)
        {
            if (!active.Remove(enemy)) return false;
            Spawned--;return true;
        }
    }
}
