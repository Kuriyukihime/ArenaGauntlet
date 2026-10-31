using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.IO.Compression;
using System.Text.Json;
using BazaarGauntlet;

internal static class EconomyAudit
{
    private sealed class Enemy
    {
        public string card {get;set;}
        public string body {get;set;}
        public string theme {get;set;}
        public int cost {get;set;}
        public float health {get;set;}
        public bool champion {get;set;}
        public bool forbidden {get;set;}
        public int flags {get;set;}
        public bool hasRewards {get;set;}
        public bool released {get;set;}
        public string[] states {get;set;}
    }
    // Per-player horde share of the full-run item target.
    private const double Cycle=GauntletDropBudget.ItemsPerPlayerAtMithrix-GauntletDropBudget.ExpectedShopItemsPerPlayer-GauntletDropBudget.ClearItemsBeforeMithrix;
    internal static void Run(Action<bool,string> check)
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var all=JsonSerializer.Deserialize<Enemy[]>(File.ReadAllText(Path.Combine(root,"scripts/checks/native-economy.json")));
        var excluded=new[]{"ally","friendly","drone","turret","dummy","invades","infestor","parasite","healingcore","shopkeeper","throne","phantom","glass"};
        var pool=all.Where(e=>!e.champion && e.cost>0 && e.hasRewards && e.released &&
            GauntletBossPolicy.Standalone(e.card,e.body,e.states) && !excluded.Any(word=>(e.card+" "+e.body).Contains(word,StringComparison.OrdinalIgnoreCase)))
            .GroupBy(e=>e.body).Select(g=>g.OrderBy(e=>e.cost).ThenBy(e=>e.card,StringComparer.Ordinal).First()).Where(e=>e.flags==0).OrderBy(e=>e.body).ToArray();
        check(pool.Length>=35,"Native economy fixture includes released ordinary species across the installed DLC");
        check(!pool.Any(e=>e.body.Contains("Vulture") || e.body.Contains("HermitCrab")),"Non-placeable NoCeiling species contribute no hypothetical income");
        using var zip=ZipFile.OpenRead(Path.Combine(root,"dist/ArenaGauntlet-0.17.0-Review.zip"));
        using var memory=new MemoryStream();using(var dll=zip.GetEntry("BepInEx/plugins/ArenaGauntlet/ArenaGauntlet.dll").Open())dll.CopyTo(memory);
        var old=Assembly.Load(memory.ToArray()).GetType("BazaarGauntlet.GauntletBalance");
        object Previous(string name,params object[] args)=>old.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);
        var models=new List<object>();
        foreach(string scope in new[]{"base","all"})
        foreach(int players in new[]{1,2,4})
        {
            var enabled=pool.Where(e=>scope=="all" || e.theme=="ror2-base").ToArray();
            var rows=new List<object>();double cumulative=0,basis=0,oldBasis=0,previousTenRoundDrops=0;
            // Compare the actual published twenty-round rules with production
            // ten-round rules. No hand-written copy of either balance curve.
            foreach(bool previous in new[]{true,false})
            for(int round=1;round<=(previous?20:10);round++)
            {
                int quota=previous?(int)Previous("HordeSize",round,1,players):GauntletBalance.HordeSize(round,1,players);
                int ceiling=previous?(int)Previous("HordeCostCeiling",round,1):GauntletBalance.HordeCostCeiling(round,1);
                var eligible=enabled.Where(e=>e.cost<=ceiling).ToArray();
                double renown=0,rolls=0;var themes=eligible.Select(e=>e.theme).Distinct().ToArray();
                foreach(string theme in themes)
                for(int seed=0;seed<16;seed++)
                {
                    var weights=eligible.Select(e=>previous?(int)Previous("SpeciesWeight",e.cost,round,1,e.theme==theme):GauntletBalance.SpeciesWeight(e.cost,round,1,e.theme==theme)).ToArray();
                    var mix=new GauntletHordeMix(weights);var available=eligible.Select(_=>true).ToArray();var costs=eligible.Select(e=>e.cost).ToArray();
                    var random=new Random(101+seed*31+round);
                    for(int kill=0;kill<quota;kill++)
                    {
                        int picked=mix.Pick(available,costs,float.MaxValue,random.Next());mix.Record(picked);var e=eligible[picked];
                        renown+=GauntletCommandRoster.Renown(e.health,false,1);
                        // Native CombatDirector: max(1, int(cost * .2 * .8));
                        // Util: baseChance * log2(spawnValue + 1). No elites,
                        // summons, boss random rolls, shops or artifacts assumed.
                        int spawnValue=Math.Max(1,(int)(e.cost*.2f*.8f));
                        rolls+=.05*Math.Log2(spawnValue+1);
                    }
                }
                renown/=themes.Length*16d;rolls/=themes.Length*16d;
                if(previous){oldBasis+=rolls;continue;}
                renown/=GauntletBalance.HordePopulationRatio(round,1,players);
                cumulative+=renown/players+GauntletPlan.BossCount(round)*GauntletCommandRoster.BossRenown(round,1,GauntletPlan.BossAt(round),1);
                double desired=GauntletDropBudget.Expected(round,players,GauntletBalance.DropMultiplier);
                basis+=desired;previousTenRoundDrops+=rolls*GauntletBalance.DropMultiplier;
                check(Math.Abs(desired-(Cycle*players-7)*(1+(round-1)/3d)/25*GauntletBalance.HordePopulationRatio(round,1,players))<1e-9,"Horde loot shrinks by the same percentage as population");
                rows.Add(new{round,quota,expectedRenownPerPlayer=cumulative,expectedHordeDrops=desired,previousHordeDrops=rolls*GauntletBalance.DropMultiplier});
            }
            double oldItems=oldBasis*.72+20*players,newItems=basis+10*players;
            check(basis<(Cycle*players-7) && basis>(Cycle*players-7)*.8,"Smaller later hordes reduce full-cycle loot while preserving the opening");
            check(basis<previousTenRoundDrops,"The new curve reduces the previous ten-round loot flood");
            models.Add(new{scope,players,species=enabled.Length,rows,oldItems,newItems,previousTenRoundItems=previousTenRoundDrops+10*players,hordeItems=basis});
        }
        // Strict species-independent floor: every kill pays only ONE, no
        // elites; equal kill shares are floored each round. Buy each tier once.
        foreach(int players in new[]{1,2,4})
        {
            long minimum=0;int spent=0;
            for(int round=1;round<=10;round++)
            {
                minimum+=GauntletBalance.HordeRewardSlots(round,1,players)/players;
                minimum+=GauntletPlan.BossCount(round)*GauntletCommandRoster.BossRenown(round,1,GauntletPlan.BossAt(round),1);
                int tier=round==1?1:round==2?2:round==4?3:round==6?4:round==7?5:0;
                if(tier==0)continue;
                spent+=GauntletCommandRoster.Commanders.First(c=>c.Tier==tier).Cost;
                check(minimum>=spent,$"{players}-player one-renown floor funds sequential tier {tier} by clear {round}");
            }
        }
        var income=new GauntletRoundIncome();
        check(income.Complete(1,new ulong[]{1,1,2,3,4}),"One completed round opens one income ledger");
        income.Price(new[]{25,25,50,50,400,25},3);
        check(income.Claim(1)==288 && income.Claim(2)==288 && income.Claim(1)==0,"Actual average stock price pays once per player, rounding half gold up");
        check(!income.Complete(1,new ulong[]{1}) && !income.Complete(0,new ulong[]{1}) && income.Claim(1)==0,"Repeated clear/scene callbacks cannot mint another round's gold");
        income.Price(new[]{10000},3);check(income.Claim(3)==288,"Later price changes cannot reprice an already committed salary");
        check(income.Claim(99)==0 && income.Claim(4)==288,"Only the connected clear roster is entitled to that round");
        check(income.Complete(2,new ulong[]{1,2}),"Next completed round resets entitlement");
        income.Price(new[]{100,100,200,200,1600,100},3);check(income.Claim(1)==1150,"Compounded posted prices scale income exactly once");
        check(GauntletEconomyPolicy.Basket(new[]{-1,0,100},3)==300 && GauntletEconomyPolicy.Basket(Array.Empty<int>(),3)==0,"Non-gold/invalid/empty prices never distort the mean");
        check(GauntletEconomyPolicy.Basket(new[]{int.MaxValue,int.MaxValue},12)==uint.MaxValue,"Extreme custom prices saturate currency safely");
        check(GauntletEconomyPolicy.Basket(new[]{100},0)==0,"Legacy mode has no extra salary");
        check(GauntletEconomyPolicy.WeightedBasket(25,new[]{.55f,.30f,.15f},new[]{1f,2f,16f},3)==266,"Three average default purchases cost 10.65 scaled small chests, rounded to gold");
        check(GauntletEconomyPolicy.WeightedBasket(100,new[]{.55f,.30f,.15f},new[]{1f,2f,16f},3)==1065,"Compounded prices scale the arena clear reward once");
        check(GauntletEconomyPolicy.WeightedBasket(100,new[]{1f},new[]{.7f},3)==210,"Tier price truncation matches native float multiplication");
        check(GauntletEconomyPolicy.WeightedBasket(100,new[]{1f,1f},new[]{0f,2f},3)==300,"Free stock contributes its probability to the expected item price");
        check(GauntletEconomyPolicy.WeightedBasket(100,new[]{0f,2f},new[]{16f,2f},3)==600,"Unavailable tier weights cannot distort the average");
        check(GauntletEconomyPolicy.WeightedBasket(100,new[]{float.NaN,1f},new[]{16f,2f},3)==600,"Invalid optional mod weights are ignored");
        check(GauntletEconomyPolicy.WeightedBasket(100,new[]{1f},new[]{2f},0)==0,"Disabling the clear reward does not restore enemy gold");
        for(int mask=0;mask<8;mask++)check(GauntletEconomyPolicy.BlockEnemyGold((mask&1)!=0,(mask&2)!=0,(mask&4)!=0)==(mask==7),"Suppress base gold for all arena enemies only on the server, including non-quota summons");
        var clear=new GauntletRoundIncome();
        check(clear.Complete(1,new ulong[]{1,2}),"Round clear creates one reward");
        clear.Price(GauntletEconomyPolicy.WeightedBasket(100,new[]{.55f,.30f,.15f},new[]{1f,2f,16f},3));
        uint wallet=100+clear.Claim(1);check(wallet==1165,"Arena clear immediately credits the same wallet used by the Bazaar");
        wallet-=300;check(clear.Claim(1)==0 && wallet==865,"Shopping spends the arena reward and cannot create another payment");
        check(!clear.Complete(1,new ulong[]{1}) && clear.Claim(1)==0,"Returning or repeating the clear callback cannot award twice");
        var output=new{source="Installed RoR2 native cards/body health; production spawn weights and GauntletHordeMix; published 0.17 balance assembly",assumptions="No elites; sufficient director credits; 16 deterministic spawn trials per theme; equal multiplayer kill shares; native-only content. Items include horde rolls and player clear rewards; exclude summons/boss random rolls/artifacts/shops.",models};
        Directory.CreateDirectory(Path.Combine(root,".build/release-030"));
        File.WriteAllText(Path.Combine(root,".build/release-030/economy-model.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Economy: {pool.Length} placeable native species; 6 old/new cycle comparisons; conservative solo/2/4-player commander affordability; immediate arena reward, shared wallet and zero enemy gold verified.");
    }
}
