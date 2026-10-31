using System;
using System.Collections.Generic;

namespace BazaarGauntlet
{
    internal static class GauntletEconomyPolicy
    {
        internal static bool BlockEnemyGold(bool mode,bool server,bool enemy)=>mode && server && enemy;
        internal static uint WeightedBasket(int basePrice,float[] weights,float[] multipliers,int items)
        {
            double total=0,weight=0;
            for(int i=0;i<Math.Min(weights.Length,multipliers.Length);i++)
            {
                if(float.IsNaN(weights[i]) || float.IsInfinity(weights[i]) || weights[i]<=0 ||
                    float.IsNaN(multipliers[i]) || float.IsInfinity(multipliers[i]) || multipliers[i]<0)continue;
                double cost=Math.Min(int.MaxValue,Math.Floor((double)(Math.Max(0,basePrice)*multipliers[i])));
                total+=weights[i]*cost;weight+=weights[i];
            }
            if(weight==0 || items<=0)return 0;
            return (uint)Math.Min(uint.MaxValue,Math.Round(items*total/weight,MidpointRounding.AwayFromZero));
        }
        // Average the posted gold prices; do not compound an already-scaled
        // price again. Integer currency rounds the basket to the nearest gold.
        internal static uint Basket(IEnumerable<int> costs,int items)
        {
            long total=0,count=0;
            foreach(int cost in costs)if(cost>0){total+=cost;count++;}
            if(count==0 || items<=0)return 0;
            return (uint)Math.Min(uint.MaxValue,Math.Round((double)total*items/count,MidpointRounding.AwayFromZero));
        }
    }
    internal sealed class GauntletRoundIncome
    {
        private readonly HashSet<ulong> recipients=new HashSet<ulong>();
        private long latest;
        private bool priced;
        private uint amount;
        internal bool Complete(long round,IEnumerable<ulong> players)
        {
            if(round<=latest)return false;
            latest=round;priced=false;recipients.Clear();
            foreach(ulong player in players)recipients.Add(player);
            return true;
        }
        internal void Price(IEnumerable<int> costs,int items)
        {
            Price(GauntletEconomyPolicy.Basket(costs,items));
        }
        internal void Price(uint value)
        {if(priced || recipients.Count==0)return;amount=value;priced=true;}
        internal uint Claim(ulong player)=>priced && recipients.Remove(player)?amount:0;
    }
}
