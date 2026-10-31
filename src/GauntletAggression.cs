using System.Linq;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal sealed class GauntletAggression:MonoBehaviour
    {
        private BaseAI[] brains;
        private GauntletController owner;
        private FortressEnemyRole role;
        private bool assigned,focusCore;
        private float nextSearch;
internal bool IsBoss=>owner && !owner.ShuttingDown && (role==FortressEnemyRole.Commander || GetComponent<CharacterMaster>()==owner.BossMaster);
        internal static void Install()
        {
            On.RoR2.CharacterAI.BaseAI.FindEnemyHurtBox+=FindBossEnemy;
            On.RoR2.CharacterAI.BaseAI.OnBodyDamaged+=BossRetaliation;
        }
        private static HurtBox FindBossEnemy(On.RoR2.CharacterAI.BaseAI.orig_FindEnemyHurtBox original,BaseAI brain,float maximum,bool fullVision,bool filterByLoS)
        {
            var aggression=brain.GetComponent<GauntletAggression>();
            if(!NetworkServer.active || !aggression || !aggression.IsBoss || !brain.body)return original(brain,maximum,fullVision,filterByLoS);
            var player=aggression.Player(brain.body.corePosition,brain.body);
            return player && (player.corePosition-brain.body.corePosition).sqrMagnitude<=maximum*maximum?player.mainHurtBox:null;
        }
        private static void BossRetaliation(On.RoR2.CharacterAI.BaseAI.orig_OnBodyDamaged original,BaseAI brain,DamageReport report)
        {
            var aggression=brain.GetComponent<GauntletAggression>();
            if(NetworkServer.active && aggression && aggression.IsBoss && (!report.attackerBody || !report.attackerBody.isPlayerControlled))
            {NativeAccess.Set(brain,"lastDamagedTime",Run.FixedTimeStamp.now);return;}
            original(brain,report);
        }
        private void Awake()
        {
            owner=GauntletController.Current;brains=GetComponents<BaseAI>();
            bool boss=owner && GetComponent<CharacterMaster>()==owner.BossMaster;
            foreach(var brain in brains){brain.fullVision=true;brain.prioritizePlayers=boss;}
        }
        private void Assign(CharacterBody body)
        {
            bool ranged=GetComponents<AISkillDriver>().Any(driver=>driver &&
                (driver.skillSlot==SkillSlot.Primary || driver.skillSlot==SkillSlot.Secondary) && driver.maxDistance>=16);
            role=GauntletEnemyRoles.Classify(body.isChampion,body.isFlying,body.baseMaxHealth,body.baseMoveSpeed,ranged);
            focusCore=role==FortressEnemyRole.Breaker || role==FortressEnemyRole.Assault;
            foreach(var brain in brains)if(brain)brain.prioritizePlayers=role==FortressEnemyRole.Commander;
            assigned=true;
        }
        private CharacterBody Player(Vector3 origin,CharacterBody viewer=null)
        {
            return PlayerCharacterMasterController.instances.Where(player=>player && player.isConnected && player.master)
                .Select(player=>player.master.GetBody()).Where(body=>body && body.healthComponent && body.healthComponent.alive && (!viewer || body.GetVisibilityLevel(viewer)!=VisibilityLevel.Untargetable))
                .OrderBy(body=>(body.corePosition-origin).sqrMagnitude).FirstOrDefault();
        }
        private void FixedUpdate()
        {
            if(!NetworkServer.active || !owner || owner.ShuttingDown || Time.fixedTime<nextSearch)return;
            nextSearch=Time.fixedTime+.8f;
            if(!owner.Fortress.IsCombat)return;
            foreach(var brain in brains)
            {
                if(!brain || !brain.body || brain.currentEnemy==null)continue;
                if(!assigned)Assign(brain.body);
                bool boss=IsBoss;brain.prioritizePlayers=true;
                var target=Player(brain.body.corePosition,brain.body);
                if(!target){brain.currentEnemy.Reset();continue;}
                // Nearby friendly drones may distract ordinary enemies. Bosses
                // always pressure living players; no invisible core is involved.
                if(!boss && brain.currentEnemy.gameObject)
                {
                    var current=brain.currentEnemy.gameObject.GetComponent<CharacterBody>();
                    if(current && current.teamComponent && current.teamComponent.teamIndex==TeamIndex.Player && current.healthComponent && current.healthComponent.alive &&
                        current.GetVisibilityLevel(brain.body)!=VisibilityLevel.Untargetable && (current.corePosition-brain.body.corePosition).sqrMagnitude<18*18)target=current;
                }
                brain.currentEnemy.gameObject=target.gameObject;brain.currentEnemy.bestHurtBox=target.mainHurtBox;
                brain.currentEnemy.Update();brain.enemyAttention=brain.enemyAttentionDuration;
            }
        }
    }
}
