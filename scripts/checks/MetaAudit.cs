using System;
using System.Linq;
using BazaarGauntlet;

internal static class MetaAudit
{
    internal static void Run(Action<bool,string> check)
    {
        var fresh=new GauntletCareer();
        check(GauntletMetaPolicy.Valid(fresh) && fresh.Balance==0 && GauntletMetaPolicy.Slots(0)==3,"Existing players retain three free random starters without any career unlock");
        check(!GauntletMetaPolicy.Buy(fresh,0) && fresh.Armory==0,"An unaffordable unlock changes nothing");
        fresh.Earned=GauntletMetaPolicy.Costs[0][0];check(GauntletMetaPolicy.Buy(fresh,0) && fresh.Balance==0 && !GauntletMetaPolicy.Buy(fresh,0),"Purchases spend Glory exactly once and retain the permanent rank");
        var max=new GauntletCareer{Earned=650};
        foreach(int branch in new[]{0,1,2})while(GauntletMetaPolicy.Buy(max,branch)){}
        check(max.Armory==6 && max.Barracks==3 && max.Patronage==2 && max.Balance==0,"A 650-Glory career can unlock every branch without negative balance");
        check(!GauntletMetaPolicy.Buy(max,0) && !GauntletMetaPolicy.Buy(max,1) && !GauntletMetaPolicy.Buy(max,2),"Fully unlocked branches cannot charge again");
        check(GauntletMetaPolicy.Slots(6)==5 && GauntletMetaPolicy.Budget(6)==21 &&
            2*GauntletMetaPolicy.Cost(2)+GauntletMetaPolicy.Cost(3)+2*GauntletMetaPolicy.Cost(1)+GauntletMetaPolicy.Cost(4)==21,
            "Maximum loadout supports two reds, a lunar, two greens and equipment");
        check(!GauntletMetaPolicy.Allowed(1,2) && !GauntletMetaPolicy.Allowed(1,3) && !GauntletMetaPolicy.Allowed(1,4) &&
            GauntletMetaPolicy.Allowed(2,4) && GauntletMetaPolicy.Allowed(3,3) && GauntletMetaPolicy.Allowed(4,2),"Equipment, lunar and legendary choices unlock in their intended order");
        for(int players=1;players<=16;players++)for(int configured=1;configured<=3;configured++)
        {
            int normal=GauntletNetworkPolicy.CompanyCapacity(players,configured);
            check(GauntletMetaPolicy.Capacity(players,configured,0)==normal && GauntletMetaPolicy.Capacity(players,configured,2)==normal+1,
                "An extra company is a personal additive upgrade at every supported party/host cap");
        }
        for(int a=0;a<=6;a++)
        {
            int sum=Enumerable.Range(1,10).Sum(r=>GauntletMetaPolicy.RoundGlory(r,a));
            check(sum==20+5*a,"Fractional round rewards sum to the advertised complete-run Glory");
            for(int round=1;round<=10;round++)
                check(GauntletMetaPolicy.TotalGlory(round,a)>GauntletMetaPolicy.TotalGlory(round-1,a),"Every completed round banks Glory, including failed partial runs");
            check(GauntletMetaPolicy.Bosses(1,false,a)==1,"Major boss fights cannot duplicate phased Mithrix encounters");
            check(GauntletMetaPolicy.Bosses(2,true,a)==(a>=4?3:2),"Only upper Ascensions add one ordinary champion");
        }
        var c=new GauntletCareer();
        check(GauntletMetaPolicy.Receipt(c,"first",8,-1) && c.Earned==8,"A partial failed run saves its earned Glory");
        for(int duplicate=0;duplicate<1000;duplicate++)check(GauntletMetaPolicy.Receipt(c,"first",8,-1) && c.Earned==8,"Replayed/repeated server snapshots cannot duplicate Glory");
        check(GauntletMetaPolicy.Receipt(c,"first",20,0) && c.Earned==20 && c.HighestClear==0,"Only the new cumulative reward delta is banked; Standard clear unlocks Ascension I");
        check(!GauntletMetaPolicy.Receipt(c,"first",8,-1) && c.Earned==20,"Out-of-order smaller reward snapshots never subtract Glory");
        check(GauntletMetaPolicy.Receipt(c,"second",25,1) && c.Earned==45 && c.HighestClear==1,"A new run uses its own receipt and advances challenge unlocks");
        for(int i=0;i<100;i++)GauntletMetaPolicy.Receipt(c,"run"+i,20,-1);
        check(c.Receipts.Length==32 && c.Received.Length==32 && c.HighestClear==1,"Receipt history is bounded and failed runs never erase the highest challenge clear");
        check(!GauntletMetaPolicy.Valid(new GauntletCareer{Earned=0,Armory=6}) && !GauntletMetaPolicy.Valid(new GauntletCareer{Ascension=6}),"Malformed ranks or locked challenge selections are rejected");
        var ledger=new GauntletWarCouncil();
        check(ledger.Grant(1,0,4) && !ledger.Grant(1,0,4) && !ledger.Grant(1,4,4) && ledger.Get(1).Renown==0,"Opening companies are free, unique and restricted to tier I");
        check(ledger.Fall(1,0) && ledger.Recover(1,0) && !ledger.Recover(1,0),"Recovery revives a genuinely fallen commander without charging renown");
        for(int id=2;id<=3;id++){ledger.Award((ulong)id,10000);for(int commander=0;commander<4;commander++)ledger.Hire((ulong)id,commander,10,1,4,true,commander);}
        var removed=ledger.EnforceCapacity(id=>id==2?4:2);
        check(ledger.Get(2).Active==4 && ledger.Get(3).Active==2 && removed.All(r=>r.Player==3),"Mixed-progression co-op caps preserve the upgraded player's extra companies");
        var starters=new GauntletStarterPlan<int,int>();
        for(int i=0;i<5;i++){check(starters.Next(1,()=>1,v=>true,out _,5),"Expanded Armory gets all five starters");starters.Confirm(1);}
        check(!starters.Next(1,()=>1,v=>true,out _,5) && starters.Announce(new[]{1},_=>5) && !starters.Announce(new[]{1},_=>5),"Expanded starter sets still cannot duplicate on reconnect and notify exactly once");
        var legacy=new GauntletCareer{Earned=40,Cosmetics=null,Decor=null,Skin=null};
        check(!GauntletMetaPolicy.Valid(legacy) && GauntletMetaPolicy.Valid(GauntletMetaPolicy.Normalize(legacy)) && legacy.Balance==40,"Pre-Treasury saves load with no cosmetics and keep their Glory");
        int cost=GauntletMetaPolicy.CosmeticCost;
        var shop=new GauntletCareer{Earned=cost*2+5};
        check(GauntletMetaPolicy.BuyCosmetic(shop,"gilded") && shop.Balance==cost+5 && !GauntletMetaPolicy.BuyCosmetic(shop,"gilded") && shop.Balance==cost+5,"A cosmetic is bought once and charged once");
        check(GauntletMetaPolicy.BuyCosmetic(shop,"statues") && !GauntletMetaPolicy.BuyCosmetic(shop,"banners") && shop.Cosmetics.Length==2 && shop.Balance==5,"An unaffordable cosmetic changes nothing");
        check(!GauntletMetaPolicy.BuyCosmetic(new GauntletCareer{Earned=1000},"") && !GauntletMetaPolicy.BuyCosmetic(new GauntletCareer{Earned=1000},new string('x',25)),"Cosmetic ids are bounded");
        shop.Skin="gilded";check(GauntletMetaPolicy.Valid(shop),"An owned skin can be selected");
        shop.Skin="lunar";check(!GauntletMetaPolicy.Valid(shop),"An unowned skin cannot be selected");shop.Skin="";
        check(GauntletMetaPolicy.ShowDecor(shop,"statues",true) && GauntletMetaPolicy.ShowDecor(shop,"statues",true) && shop.Decor.SequenceEqual(new[]{"statues"}) && GauntletMetaPolicy.Valid(shop),"Showing decor twice keeps one entry");
        check(GauntletMetaPolicy.ShowDecor(shop,"statues",false) && shop.Decor.Length==0 && !GauntletMetaPolicy.ShowDecor(shop,"banners",true),"Decor hides again; unowned decor cannot be shown");
        shop.Decor=new[]{"banners"};check(!GauntletMetaPolicy.Valid(shop),"Saved decor must be owned");shop.Decor=Array.Empty<string>();
        shop.Earned=1000000;for(int i=0;i<40;i++)GauntletMetaPolicy.BuyCosmetic(shop,"c"+i);
        check(shop.Cosmetics.Length==GauntletMetaPolicy.MaxCosmetics && GauntletMetaPolicy.Valid(shop) && shop.Balance==1000000-GauntletMetaPolicy.MaxCosmetics*cost,"Owned cosmetics are capped and every one is charged");
        check(!GauntletMetaPolicy.Valid(new GauntletCareer{Earned=29,Cosmetics=new[]{"gilded"}}),"A save cannot own more cosmetics than its Glory paid for");
        var veteran=new GauntletCareer{Earned=500,Armory=6,Barracks=3,Patronage=2,Cosmetics=new[]{"gilded","lunar"},Skin="lunar"};
        check(GauntletMetaPolicy.Valid(veteran) && veteran.Balance==10,"A 1.0.0 career keeps everything it owns and its unspent Glory after the price rise");
        var climber=new GauntletCareer{Earned=100,Armory=1};
        check(GauntletMetaPolicy.Buy(climber,0) && climber.Paid==10+GauntletMetaPolicy.Costs[0][1] && climber.Balance==90-GauntletMetaPolicy.Costs[0][1] && GauntletMetaPolicy.Valid(climber),
            "A purchase after the price rise charges the new price and keeps the earlier one at what it cost");
        check(GauntletMetaPolicy.BuyCosmetic(climber,"ruins") && climber.Balance==90-GauntletMetaPolicy.Costs[0][1]-cost,"A cosmetic bought after the price rise charges the new price");
        check(!GauntletMetaPolicy.Valid(new GauntletCareer{Earned=100,Paid=-1}) && !GauntletMetaPolicy.Valid(new GauntletCareer{Earned=100,Paid=101}),"Paid Glory cannot be negative or exceed Earned");
        check(GauntletMetaPolicy.Costs.SelectMany(row=>row).Sum()==650 && cost==50,"1.0.1 prices: 650 Glory for every upgrade, 50 per cosmetic");
        var labors=GauntletMetaPolicy.Labors;
        check(labors.Length==12 && labors.Select(l=>l.Id).Distinct().Count()==12 && labors.All(l=>GauntletMetaPolicy.ShortId(l.Id) && l.Glory>0 && l.Glory<=25),"Twelve distinct Labors, each with a modest one-time reward");
        var hero=new GauntletCareer();
        foreach(var labor in labors)check(GauntletMetaPolicy.CompleteLabor(hero,labor.Id),"Every Labor can be completed: "+labor.Id);
        check(hero.Earned==labors.Sum(l=>l.Glory) && hero.Earned==140 && GauntletMetaPolicy.Valid(hero),"All twelve Labors pay 140 Glory in total");
        for(int repeat=0;repeat<100;repeat++)check(!GauntletMetaPolicy.CompleteLabor(hero,"victor") && hero.Earned==140,"Repeated status packets cannot pay a Labor twice");
        check(!GauntletMetaPolicy.CompleteLabor(new GauntletCareer(),"unknown") && !GauntletMetaPolicy.CompleteLabor(new GauntletCareer(),null),"Unknown Labors pay nothing");
        check(GauntletMetaPolicy.Valid(new GauntletCareer{Labors=new[]{"retired"}}) && !GauntletMetaPolicy.Valid(new GauntletCareer{Labors=new[]{"victor","victor"}}),"A retired Labor id keeps old saves loadable; duplicates are rejected");
        var oldSave=new GauntletCareer{Labors=null};
        check(GauntletMetaPolicy.Valid(GauntletMetaPolicy.Normalize(oldSave)) && GauntletMetaPolicy.CompleteLabor(oldSave,"initiate") && oldSave.Earned==5,"Pre-Labor saves start with none completed");
    }
}
