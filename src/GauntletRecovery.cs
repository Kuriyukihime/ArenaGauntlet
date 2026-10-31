using RoR2;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal static class GauntletRecovery
    {
        // Called at accepted blue-portal departure and at the next fight start.
        // This is an inter-round reset, not a combat heal: delayed healing,
        // healing reductions and healing-to-damage conversions cannot consume it.
        internal static void RestorePlayers()
        {
            if (!NetworkServer.active) return;
            foreach (var player in PlayerCharacterMasterController.instances)
            {
                if (!player || !player.isConnected || !player.master) continue;
                var body = player.master.GetBody();
                if ((!body || !body.healthComponent || !body.healthComponent.alive) && player.master.IsDeadAndOutOfLivesServer())
                {
                    var owner = GauntletController.Current;
                    var ward = owner ? NativeAccess.Get<InfiniteTowerSafeWardController>(owner.Run, "safeWardController") : null;
                    var point=SpawnPoint.ConsumeSpawnPoint();
                    var position = point?point.transform.position:ward ? ward.transform.position+UnityEngine.Vector3.forward*7 : player.master.deathFootPosition;
                    player.master.Respawn(position, UnityEngine.Quaternion.identity);
                    body = player.master.GetBody();
                    if (body) body.AddTimedBuff(RoR2Content.Buffs.Immune, 3);
                }
                if (!body || !body.healthComponent || !body.healthComponent.alive) continue;
                var health = body.healthComponent;
                health.Networkhealth = health.fullHealth;
                health.RechargeShieldFull();
            }
        }
    }
}
