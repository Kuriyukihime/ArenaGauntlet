namespace BazaarGauntlet
{
    internal static class GauntletCompanyDamagePolicy
    {
        internal const int Profiles=13,SolusProbe=13;
        // Leader/squad pairs: Clay, Lunar, Solus, Covenant, Titan, Abyssal.
        // Ranged uptime, durability and party-wide support all consume power.
        private static readonly float[] factors={.4f,.4f,.5f,.5f,.6f,.6f,.65f,1f,.75f,.75f,.7f,.6f,.6f};
        internal static int ProfileFor(int commander,bool leader)=>commander>=4 && commander<=9?1+2*(commander-4)+(leader?0:1):0;
        // One replicated hidden count encodes profile and the natural 3/2/1
        // company allowance. Setting a code replaces it; it never stacks.
        internal static int Encode(int profile,int slots)=>profile>=1 && profile<=Profiles && slots>=1 && slots<=3?profile+Profiles*(slots-1):0;
        internal static int Profile(int code)=>code>=1 && code<=Profiles*3?1+(code-1)%Profiles:0;
        internal static int Slots(int code)=>Profile(code)>0?1+(code-1)/Profiles:0;
        internal static float Multiplier(int code)
        {
            int profile=Profile(code),slots=Slots(code);
            // Partial compensation for fewer companies per person in co-op;
            // focus fire and shared support make a full 3/slots multiplier excessive.
            return profile==0?1:factors[profile-1]*(slots==1?2.25f:slots==2?1.35f:1f);
        }
    }
}
