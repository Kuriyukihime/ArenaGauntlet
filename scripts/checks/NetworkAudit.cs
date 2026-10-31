using System;
using System.Linq;
using BazaarGauntlet;

internal static class NetworkAudit
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(int players in new[]{0,1,2,3,4,5,16})
        {
            int expected=players<=1?3:players<=3?2:1;
            check(GauntletNetworkPolicy.CompanyCapacity(players,3)==expected,"Requested commander cap: "+players);
            check(GauntletNetworkPolicy.CompanyCapacity(players,6)==expected,"Old custom settings cannot exceed party-size cap: "+players);
            check(GauntletNetworkPolicy.CompanyCapacity(players,1)==1,"A lower custom ceiling is retained");
        }
        check(GauntletNetworkPolicy.MatchesRun(1,42,1,42),"Current host run accepts matching request identity");
        check(!GauntletNetworkPolicy.MatchesRun(2,42,1,42),"Delayed request from a previous run with the same seed is rejected");
        check(!GauntletNetworkPolicy.MatchesRun(1,43,1,42) && !GauntletNetworkPolicy.MatchesRun(0,42,0,42),"Wrong seed/unspawned run requests are rejected");
        var budget=new GauntletRequestBudget();
        for(int i=0;i<4;i++)check(budget.Take(0),"Connection burst permits local split-screen clicks");
        for(int i=0;i<1000;i++)check(!budget.Take(0),"Request flood cannot parse or broadcast per packet after burst");
        check(budget.Take(.125) && !budget.Take(.125),"Connection budget refills at eight requests per second");
        check(!budget.Take(double.NaN) && !budget.Take(double.PositiveInfinity),"Invalid clocks cannot bypass the budget");
        var council=new GauntletWarCouncil();council.Award(1,1000);double now=0;
        foreach(int commander in new[]{0,2,4})check(council.Hire(1,commander,10,1,3,true,now+=1)==CommandResult.Hired,"Solo can hire three different companies");
        long earned=council.Get(1).Earned;
        var first=council.EnforceCapacity(2);
        check(first.Count==1 && first[0]==(1UL,4) && council.Get(1).Active==2 && council.Get(1).Renown==940,"Second player withdraws newest excess company and refunds exactly its cost");
        check(council.Get(1).Earned==earned && council.EnforceCapacity(2).Count==0,"Cap refunds cannot farm earned renown or repeat");
        var second=council.EnforceCapacity(1);
        check(second.Count==1 && second[0]==(1UL,2) && council.Get(1).Renown==980 && council.Get(1).Active==1,"Four-player cap keeps the oldest company and refunds the next excess");
        check(council.EnforceCapacity(3).Count==0 && council.Get(1).Active==1,"A player leaving increases capacity without auto-hiring or charging");
        check(council.Hire(1,2,10,1,1,true,now+=1)==CommandResult.NoCapacity,"Host ledger enforces the smaller capacity on new hires");
        council.Award(2,1000);
        check(council.Hire(2,0,10,1,3,true,now+=1)==CommandResult.Hired && council.Hire(2,2,10,1,3,true,now+=1)==CommandResult.Hired,"Other player has a separate account");
        council.Fall(2,0);check(council.EnforceCapacity(1).Count==0 && council.Get(2).Active==1,"Fallen commanders do not consume an active commander slot");
        Console.WriteLine("Network: restart identity, bounded request bursts, party-size caps, forced-withdrawal refunds and host capacity checks passed.");
    }
}
