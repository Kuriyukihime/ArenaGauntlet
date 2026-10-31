using System;
using System.Linq;
using BazaarGauntlet;

internal static class ShopAudit
{
    internal static void Run(Action<bool,string> check)
    {
        var shop=new GauntletShopPlan();
        check(GauntletShopPlan.ItemChoices==6 && GauntletShopPlan.LunarChoices==6 && GauntletShopPlan.EquipmentChoices==6,"Items, lunar items and equipment each have six offers");
        for(int slot=0;slot<GauntletShopPlan.SlotCount;slot++)
        {
            var position=GauntletShopPlan.Position(slot);
            check(position.X*position.X+position.Z*position.Z>168*168,"Every shop offer stays outside the enemy navigation circle");
            for(int other=0;other<slot;other++)
            {
                var second=GauntletShopPlan.Position(other);float dx=position.X-second.X,dy=position.Y-second.Y,dz=position.Z-second.Z;
                check(dx*dx+dy*dy+dz*dz>4,"Extra offers do not overlap another interaction or break slot identification");
            }
        }
        int[] tiers=new int[3];
        for(int roll=0;roll<100000;roll++)tiers[GauntletShopPricing.RollTier((roll+.5)/100000,100,100,100)]++;
        check(tiers.SequenceEqual(new[]{55000,30000,15000}),"Integrated item rarity stays exactly 55/30/15 with independent slot draws");
        for(int small=1;small<5000;small+=7)for(int tier=0;tier<3;tier++)
            check(GauntletShopPricing.Price(small,tier)==(int)((float)small*(tier==2?16f:tier==1?2f:1f)),"Integrated item prices exactly match BiggerBazaar float multiplication/truncation");
        check(GauntletShopPricing.RollTier(.9,10,0,0)==0 && GauntletShopPricing.RollTier(.1,0,10,0)==1 && GauntletShopPricing.RollTier(.1,0,0,10)==2,"Unavailable tiers are removed before weighting");
        check(GauntletShopPricing.RollTier(.5,0,0,0)==-1,"No content cannot roll an invalid tier");
        check(GauntletShopPricing.ExpectedReward(25,3)==266 && GauntletShopPricing.ExpectedReward(100,3)==1065,"Expected clear budget retains the prior three-purchase calculation");
        check(!shop.Tick(0,Array.Empty<uint>(),true),"An empty party cannot start a round");
        uint[] party={1,2};shop.Tick(0,party,true);
        shop.Toggle(1);check(!shop.Tick(1,party,true),"One ready player cannot skip a shopping teammate");
        shop.Toggle(2);check(!shop.Tick(2,party,true) && !shop.Tick(4.99f,party,true),"All-ready gives a three-second grace");
        check(shop.Tick(5,party,true),"All-ready can advance after grace");
        shop.Unready(2);check(!shop.Tick(6,party,true),"Shopping cancels the countdown");
        shop.Toggle(2);shop.Tick(7,party,true);
        check(!shop.Tick(9,new uint[]{1,2,3},true),"A joining player cancels countdown and starts unready");
        shop.Toggle(3);shop.Tick(10,new uint[]{1,2,3},true);
        check(!shop.Tick(12,new uint[]{1,2,3},false),"Busy scrapper/loading blocks combat even with votes");
        check(!shop.Tick(13,party,true) && shop.Tick(16,party,true),"Disconnecting updates the party and restarts the full grace");
        check(shop.Buy(1,0) && !shop.Buy(1,0) && !shop.CanBuy(1,0),"Repeated packets cannot buy the same offer twice");
        check(shop.CanBuy(2,0) && shop.Buy(2,0),"Other players can buy the same shared offer");
        for(int slot=1;slot<17;slot++)check(shop.Buy(1,slot),"Independent item/equipment/lunar/trade slots");
        check(shop.Mask(1,_=>1)==(1<<17)-1,"Snapshot purchase mask exactly represents the player's exhausted offers");
        shop.Reset();check(shop.Mask(1,_=>1)==0 && shop.CanBuy(1,0) && !shop.IsReady(1),"Restock resets purchases and votes");
        int Group(int slot)=>slot<6?-1-slot%3:slot;
        int Limit(int slot)=>slot<6?GauntletShopPlan.ItemLimit(slot%3):1;
        for(uint player=1;player<=4;player++)for(int tier=0;tier<3;tier++)
        {
            int limit=GauntletShopPlan.ItemLimit(tier);
            for(int copy=0;copy<limit;copy++)
            {
                int slot=tier+(copy%2)*3;
                check(shop.CanBuy(player,slot,limit,Group) && shop.Buy(player,slot,limit,Group),"Each player can split their tier-wide allowance across different displays");
                check(shop.Count(player,tier)+shop.Count(player,tier+3)==copy+1,"Tier-wide allowance counts all displays exactly once");
            }
            foreach(int slot in new[]{tier,tier+3})
            {
                check(!shop.CanBuy(player,slot,limit,Group) && !shop.Buy(player,slot,limit,Group),"Changing displays cannot bypass a player's exhausted rarity allowance");
                check((shop.Mask(player,Limit,Group)&(1<<slot))!=0,"The client disables every display of the exhausted tier, including unpurchased ones");
            }
        }
        check(shop.Count(1,0)+shop.Count(1,3)==3 && shop.Count(2,0)+shop.Count(2,3)==3,"Each player has their own three-white budget");
        check(shop.Buy(1,GauntletShopPlan.LunarStart,1,Group) && !shop.Buy(1,GauntletShopPlan.LunarStart,1,Group) && shop.Buy(1,GauntletShopPlan.LunarStart+1,1,Group),"Other vendors retain their existing independent display rules");
        shop.Reset();
        int AllWhite(int slot)=>slot<6?-1:slot;
        int WhiteLimit(int slot)=>slot<6?3:1;
        for(int slot=0;slot<3;slot++)check(shop.Buy(1,slot,3,AllWhite),"Three whites may come from three separate offers");
        check(!shop.Buy(1,5,3,AllWhite) && (shop.Mask(1,WhiteLimit,AllWhite)&63)==63,"All six white offers close after three total purchases, even unseen or duplicate offers");
        check(shop.CanBuy(2,5,3,AllWhite),"One player's cap cannot consume another player's allowance");
        shop.Reset();check(shop.CanBuy(1,5,3,AllWhite),"The next shopping break restores the player's tier budget");
        shop.Reset();
        for(int slot=0;slot<GauntletShopPlan.SlotCount;slot++)check(shop.Buy(1,slot),"Every expanded slot fits the purchase ledger");
        check(shop.Mask(1,_=>1)==GauntletShopPlan.MaxPurchaseMask,"The highest equipment/drone/trade bits survive the expanded mask");
        check(GauntletShopPlan.ValidMask(1<<GauntletShopPlan.RedTrade) && GauntletShopPlan.ValidMask(GauntletShopPlan.MaxPurchaseMask),"Remote-client validation accepts new vendor and exchange bits");
        check(!GauntletShopPlan.ValidMask(-1) && !GauntletShopPlan.ValidMask(1<<GauntletShopPlan.SlotCount),"Remote-client validation still rejects negative and oversized masks");
        check(!shop.Buy(1,-1) && !shop.Buy(1,GauntletShopPlan.SlotCount),"Out-of-range slots cannot alias another mask bit");
        var flow=new GauntletFlow();flow.Start();flow.ArenaLoaded();
        for(int cycle=1;cycle<=3;cycle++)for(int round=1;round<=10;round++)
        {
            check(flow.Cycle==cycle && flow.Round==round && flow.Phase==GauntletPhase.Horde,"In-place rounds retain compressed difficulty and cycle numbers");
            check(!flow.NextInPlace(),"A shopping click cannot skip an active horde");
            flow.HordeCleared();check(!flow.NextInPlace(),"A shopping click cannot skip champions");flow.BossCleared();
            if(round==10){check(!flow.NextInPlace(),"Final victory needs the explicit continuation decision");flow.ContinueThroughBazaar();}
            check(flow.NextInPlace() && !flow.NextInPlace(),"A clear advances exactly once without any scene load");
        }
        foreach(var costs in new[]{new[]{25,25,25,50,50,400},new[]{101,202,1616,101,202,101},new[]{1000000,2000000,16000000,1000000,1000000,2000000}})
            check(GauntletShopPricing.ClearReward(costs,new[]{50,50,50,50,50,50},new[]{40,40,60},3)==(uint)Math.Round(costs.Average()*3+25+35d/3,MidpointRounding.AwayFromZero),"Clear reward retains three actual average items plus equipment and drone allowances");
        double expectedOld=0,expectedNew=0;
        for(int combination=0;combination<729;combination++)
        {
            int value=combination;var costs=new int[6];double probability=1;
            for(int slot=0;slot<6;slot++)
            {
                int tier=value%3;value/=3;costs[slot]=new[]{25,50,400}[tier];probability*=new[]{.55,.30,.15}[tier];
            }
            expectedOld+=probability*GauntletEconomyPolicy.Basket(costs,3);
            expectedNew+=probability*GauntletShopPricing.ClearReward(costs,new[]{50,50,50,50,50,50},new[]{40,40,60},3);
        }
        double increase=100*(expectedNew/expectedOld-1);
        check(increase>13 && increase<15,"Exact six-offer enumeration gives the planned roughly fourteen percent gold increase");
        check(GauntletShopPricing.ClearReward(new[]{25},new[]{50},new[]{40},0)==0,"Disabling clear income also disables the new allowances");
        check(GauntletShopPricing.ClearReward(new[]{int.MaxValue},new[]{int.MaxValue},new[]{int.MaxValue},12)==uint.MaxValue,"Extreme compounded prices cannot overflow the clear wallet reward");
        Console.WriteLine($"Shopping: six offers per vendor, player-wide 3-white/2-green/1-red budgets across all item displays; exact default expected gold {expectedOld:F2} -> {expectedNew:F2} (+{increase:F2}%).");
    }
}
