using System;
using System.Collections.Generic;

namespace BazaarGauntlet
{
    // BiggerBazaar 1.14.5's default gold shop, implemented without a plugin
    // dependency: relative rarity 55/30/15, small-chest multipliers 1/2/16.
    internal static class GauntletShopPricing
    {
        internal static int RollTier(double roll,int whiteCount,int greenCount,int redCount)
        {
            float white=whiteCount>0?.55f:0,green=greenCount>0?.30f:0,red=redCount>0?.15f:0;
            double remaining=roll*(white+green+red);
            if(white>0 && remaining<=white)return 0;remaining-=white;
            if(green>0 && remaining<=green)return 1;remaining-=green;
            return red>0?2:green>0?1:white>0?0:-1;
        }
        internal static int Price(int smallChest,int tier)
        {
            float multiplier=tier==2?16f:tier==1?2f:1f;
            // Match the native order: scale/truncate a small chest first, then
            // multiply by a float tier cost and truncate again.
            return (int)Math.Min(int.MaxValue,Math.Max(0,(double)(Math.Max(0,smallChest)*multiplier)));
        }
        internal static uint ExpectedReward(int smallChest,int items)=>GauntletEconomyPolicy.WeightedBasket(smallChest,new[]{.55f,.30f,.15f},new[]{1f,2f,16f},items);
        internal static uint ClearReward(IEnumerable<int> items,IEnumerable<int> equipment,IEnumerable<int> drones,int itemBudget)
        {
            if(itemBudget<=0)return 0;
            double reward=Mean(items)*itemBudget+Mean(equipment)*.5+Mean(drones)*.25;
            return (uint)Math.Min(uint.MaxValue,Math.Round(reward,MidpointRounding.AwayFromZero));
        }
        private static double Mean(IEnumerable<int> prices)
        {
            long total=0,count=0;
            foreach(int price in prices)if(price>=0){total+=price;count++;}
            return count>0?(double)total/count:0;
        }
    }
}
