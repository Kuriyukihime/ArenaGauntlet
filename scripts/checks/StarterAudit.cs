using System;
using BazaarGauntlet;

internal static class StarterAudit
{
    internal static void Run(Action<bool,string> check)
    {
        check(GauntletStarterPolicy.Items==3,"Each player starts with exactly three items");
        int white=0,green=0;
        for(int sample=0;sample<10000;sample++)
        {
            int tier=GauntletStarterPolicy.Tier((sample+.5)/10000,10,10);
            if(tier==0)white++;else if(tier==1)green++;
            else throw new Exception("A starter roll escaped the white/green whitelist");
        }
        check(white==8000 && green==2000,"Starter rarity is eighty percent white, twenty percent green");
        check(GauntletStarterPolicy.Tier(0,0,10)==1 && GauntletStarterPolicy.Tier(.99,10,0)==0,"An unavailable tier falls back only to the other allowed tier");
        check(GauntletStarterPolicy.Tier(.5,0,0)==-1,"Empty allowed pools never fall back to a red or other tier");
        var plan=new GauntletStarterPlan<(ulong,byte),int>();
        var party=new[]{(1UL,(byte)0),(2UL,(byte)0),(3UL,(byte)0),(4UL,(byte)0)};
        check(!plan.Announce(party) && !plan.Announce(Array.Empty<(ulong,byte)>()),"No grant notification appears before items are granted or for an empty party");
        int rolls=0;
        for(ulong player=1;player<=4;player++)
        {
            var key=(player,(byte)0);
            for(int item=0;item<3;item++)
            {
                check(plan.Next(key,()=>++rolls,v=>v>0,out int selected),"Every new player receives all three independent rolls");
                check(plan.Next(key,()=>++rolls,v=>v>0,out int retry) && retry==selected,"Retrying a pending grant keeps the same rolled item");
                check(plan.Confirm(key) && !plan.Confirm(key),"Each pending grant commits exactly once");
            }
            for(int respawn=0;respawn<20;respawn++)
                check(!plan.Next(key,()=>++rolls,v=>v>0,out _) && plan.Given(key)==3,"Respawns, new cycles and the same reconnecting identity cannot refill starter items");
        }
        check(rolls==12,"Completed players cannot reroll starter stock by retrying");
        check(plan.Announce(party) && !plan.Announce(party),"The party receives one standard notification after all starter sets finish, rather than one message per item/player");
        check(!plan.Announce(new[]{party[0]}),"Roster changes or reconnecting completed players cannot repeat the notification");
        var split=(1UL,(byte)1);
        check(plan.Next(split,()=>++rolls,v=>v>0,out _) && plan.Given(split)==0,"A separate native local-subplayer identity has its own entitlement");
        check(!plan.Announce(new[]{party[0],split}),"A partial late arrival must finish its own starter set before announcement");
        plan.Confirm(split);
        while(plan.Next(split,()=>++rolls,v=>v>0,out _))plan.Confirm(split);
        check(plan.Announce(new[]{party[0],split}) && !plan.Announce(new[]{party[0],split}),"A first-time late arrival gets one completion notice without respawn/reconnect spam");
        check(!plan.Confirm((999UL,(byte)0)),"Unknown players cannot confirm nonexistent grants");
        var empty=new GauntletStarterPlan<int,int>();
        check(!empty.Next(1,()=>-1,v=>v>=0,out _) && empty.Given(1)==0,"Missing eligible stock leaves the entitlement available for a later retry");
        check(empty.Next(1,()=>5,v=>v>=0,out int next) && next==5 && empty.Confirm(1),"A later valid pool can complete the pending startup grant");
        var restarted=new GauntletStarterPlan<(ulong,byte),int>();
        check(restarted.Next((1UL,(byte)0),()=>7,v=>v>0,out _) && restarted.Given((1UL,(byte)0))==0,"A new run gives the same player a fresh starter entitlement");
        Console.WriteLine("Starters: three items per account/subplayer/run; white/green only, 80/20 rarity, pending retries, respawns/reconnects and fresh-run reset checked.");
    }
}
