using System;
using BazaarGauntlet;

internal static class DropAudit
{
    internal static void Run(Action<bool,string> check)
    {
        double cycleTotal=0;
        for(int round=1;round<=10;round++)
        {
            double target=GauntletDropBudget.Expected(round,1,1.6f);cycleTotal+=target;
            check(Math.Abs(target-(.72+(round-1)*.24)*GauntletBalance.HordePopulationRatio(round,1,1))<1e-9,"Horde items shrink in proportion to population, preserving expected loot per enemy");
        }
        check(cycleTotal>14 && cycleTotal<15.5,"Solo hordes yield about fifteen items across a cycle");
        check(Math.Abs(GauntletDropBudget.Expected(0,1,1.6f)-.72)<1e-9 && Math.Abs(GauntletDropBudget.Expected(99,1,1.6f)-2.88*80/118)<1e-9,"Round targets clamp to the adjusted ten-round curve");
        check(GauntletDropBudget.Expected(5,1,0)==0 && GauntletDropBudget.Expected(5,1,float.NaN)==0,"Disabled and invalid host rates cannot award horde items");
        check(Math.Abs(GauntletDropBudget.Expected(1,1,.8f)-.36)<1e-9,"Custom host multipliers retain proportional control");
        int waves=0;
        foreach(int players in new[]{1,2,3,4})
        for(int round=1;round<=10;round++)
        foreach(double eliteFraction in new[]{0d,.25,.75,1})
        for(int seed=0;seed<32;seed++)
        {
            var random=new Random(seed*131+round*7+players);
            int quota=GauntletBalance.HordeSize(round,1,players);
            double expected=GauntletDropBudget.Expected(round,players,1.6f);
            var budget=new GauntletDropBudget(quota,expected,random.NextDouble());
            check(budget.Target>=Math.Floor(expected) && budget.Target<=Math.Ceiling(expected),"Fractional co-op budgets round to adjacent integers only");
            int drops=0;
            for(int kill=0;kill<quota;kill++)
                if(budget.Roll(random.NextDouble()<eliteFraction,random.NextDouble()))drops++;
            check(drops==budget.Target && budget.RemainingDrops==0 && budget.RemainingEnemies==0,"Every completed finite horde uses exactly one budget regardless of elite composition");
            check(!budget.Roll(true,0),"Extra deaths cannot mint loot after the horde budget is exhausted");
            waves++;
        }
        foreach(int players in new[]{1,2,3,4})
        for(int round=1;round<=10;round++)
        {
            double desired=GauntletDropBudget.Expected(round,players,1.6f),total=0;
            for(int sample=0;sample<300;sample++)
                total+=new GauntletDropBudget(500,desired,(sample+.5)/300).Target;
            check(Math.Abs(total/300-desired)<=1d/300+1e-9,"Fractional per-round/co-op rounding preserves expected loot within one sample of resolution");
        }
        var ledger=new GauntletQuotaLedger<int>(3);
        ledger.Spawn(1);ledger.Spawn(2);ledger.Spawn(3);
        var replacements=new GauntletDropBudget(3,2,0);int acceptedDrops=0;
        void Accept(int id){if(ledger.Defeat(id) && replacements.Roll(true,0))acceptedDrops++;}
        Accept(1);Accept(1);
        check(ledger.Despawn(2) && ledger.Spawn(4),"A despawn reopens the original slot for a replacement");
        Accept(4);Accept(3);Accept(4);
        check(ledger.Complete && acceptedDrops==2 && replacements.RemainingEnemies==0,"Duplicate reports and replaced enemies share the original quota's item budget");
        int normals=0,elites=0;
        for(int sample=0;sample<10000;sample++)
        {
            double roll=(sample+.5)/10000;
            if(new GauntletDropBudget(100,10,0).Roll(false,roll))normals++;
            if(new GauntletDropBudget(100,10,0).Roll(true,roll))elites++;
        }
        check(normals==1000 && elites==1250,"An elite gets a 25-percent higher initial chance from the same remaining budget");
        var disabled=new GauntletDropBudget(118,0,0);
        for(int i=0;i<118;i++)check(!disabled.Roll(true,0),"Zero-budget hordes never drop, even if every enemy is elite");
        var capped=new GauntletDropBudget(3,double.PositiveInfinity,0);
        check(capped.Target==3 && capped.Roll(false,.99) && capped.Roll(true,.99) && capped.Roll(false,.99) && !capped.Roll(true,0),"Extreme custom rates cap safely at one item per quota kill");
        var empty=new GauntletDropBudget(0,12,0);
        check(empty.Target==0 && !empty.Roll(true,0),"An empty or invalid quota cannot create items");
        for(int players=1;players<=16;players++)
        {
            double horde=0;for(int round=1;round<=10;round++)horde+=GauntletDropBudget.Expected(round,players,1.6f);
            double perPlayer=(horde+7)/players+39+9;
            check(perPlayer<74 && perPlayer>=65,"Horde loot fits the seventy-three-item target alongside shopping and boss rewards");
        }
        Console.WriteLine($"Horde loot scales with reduced population; {waves} complete mixed-elite/co-op hordes, disabled/custom rates and exhaustion checked.");
    }
}
