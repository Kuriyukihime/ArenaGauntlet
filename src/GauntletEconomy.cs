using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    // Arena clears pay the ordinary run wallet. The Bazaar never pays income.
    internal sealed class GauntletEconomy:MonoBehaviour
    {
        private readonly GauntletRoundIncome income=new GauntletRoundIncome();
        private GauntletController owner;
        private int items;
        internal bool Enabled {get;private set;}
        internal static void Install()=>On.RoR2.DeathRewards.OnKilledServer+=EnemyKilled;
        private static void EnemyKilled(On.RoR2.DeathRewards.orig_OnKilledServer original,DeathRewards rewards,DamageReport report)
        {
            if(GauntletEconomyPolicy.BlockEnemyGold(GauntletController.Current!=null,NetworkServer.active,report!=null && report.victimTeamIndex!=TeamIndex.Player))
                rewards.goldReward=0;
            original(rewards,report);
        }
        internal void Initialize(GauntletController controller)
        {
            owner=controller;items=GauntletConfig.BazaarItemsPerRound.Value;
            Enabled=items>0;
        }
        internal void CompleteRound()
        {
            if(!Enabled || !NetworkServer.active || !owner || owner.ShuttingDown)return;
            if(!income.Complete((long)(owner.Flow.Cycle-1)*GauntletPlan.RoundsPerCycle+owner.Flow.Round,
                PlayerCharacterMasterController.instances.Where(p=>p && p.isConnected && p.master).Select(p=>(ulong)p.master.netId.Value)))return;
            uint reward=owner.Shop.ClearReward(items);
            income.Price(reward);
            foreach(var player in PlayerCharacterMasterController.instances)
            {
                if(!player || !player.isConnected || !player.master)continue;
                uint amount=System.Math.Min(income.Claim(player.master.netId.Value),uint.MaxValue-player.master.money);
                if(amount==0)continue;
                player.master.GiveMoney(amount,false);
                GauntletPlugin.Log.LogInfo($"Arena clear reward: {player.master.netId.Value} received {amount} gold in the shared run wallet.");
            }
            if(reward>0)GauntletPortals.Announce($"<color=#e8c06b>Round clear reward: {reward:N0} gold per player.</color>");
        }
    }
}
