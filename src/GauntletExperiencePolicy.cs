namespace BazaarGauntlet
{
    // The player team shares XP. Carry half a point between awards so many
    // one-XP enemies receive the same 50% increase as a single large award.
    internal sealed class GauntletExperiencePolicy
    {
        private bool halfPending;
        internal ulong Scale(ulong amount)
        {
            ulong bonus=amount/2;
            if((amount&1)!=0)
            {
                if(halfPending)bonus++;
                halfPending=!halfPending;
            }
            return amount>ulong.MaxValue-bonus?ulong.MaxValue:amount+bonus;
        }
    }
}
