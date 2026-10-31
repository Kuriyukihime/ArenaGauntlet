using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal static class GauntletExperience
    {
        private static GauntletController activeDeath;
        internal static void Install()
        {
            On.RoR2.DeathRewards.OnKilledServer+=EnemyKilled;
            On.RoR2.ExperienceManager.AwardExperience+=Award;
        }
        private static void EnemyKilled(On.RoR2.DeathRewards.orig_OnKilledServer original,DeathRewards rewards,DamageReport report)
        {
            var owner=GauntletController.Current;
            if(!NetworkServer.active || !owner || report==null || report.victimTeamIndex==TeamIndex.Player)
            {original(rewards,report);return;}
            var previous=activeDeath;activeDeath=owner;
            try {original(rewards,report);}
            finally {activeDeath=previous;}
        }
        private static void Award(On.RoR2.ExperienceManager.orig_AwardExperience original,ExperienceManager manager,Vector3 position,CharacterBody body,ulong amount)
        {
            if(NetworkServer.active && activeDeath && activeDeath==GauntletController.Current && body && body.master && body.master.teamIndex==TeamIndex.Player)
                amount=activeDeath.Experience.Scale(amount);
            original(manager,position,body,amount);
        }
    }
}
