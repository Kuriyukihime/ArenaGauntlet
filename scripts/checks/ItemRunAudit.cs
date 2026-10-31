using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using BazaarGauntlet;

internal static class ItemRunAudit
{
    private sealed class Enemy
    {
        public string card{get;set;} public string body{get;set;} public string theme{get;set;}
        public int cost{get;set;} public float health{get;set;} public float damage{get;set;}
        public bool champion{get;set;} public bool forbidden{get;set;} public bool released{get;set;} public bool hasRewards{get;set;}
        public string[] states{get;set;}
    }
    private static double ChampionBonuses(Enemy[] source,bool baseOnly)
    {
        var excluded=new[]{"ally","friendly","drone","turret","dummy","invades","infestor","parasite","healingcore","shopkeeper","throne","phantom","glass"};
        var pool=source.Where(e=>e.released && GauntletBossPolicy.Standalone(e.card,e.body,e.states) &&
            !excluded.Any(s=>(e.card+" "+e.body).Contains(s,StringComparison.OrdinalIgnoreCase)) &&
            (!baseOnly || e.theme=="ror2-base" || e.card.Contains("ScavLunar"))).ToArray();
        bool Great(Enemy e)=>new[]{"Brother","ScavLunar","MiniVoidRaidCrabPhase3"}.Any(s=>(e.card+e.body).Contains(s,StringComparison.OrdinalIgnoreCase));
        var ordinary=pool.Where(e=>e.champion && !e.forbidden && !Great(e)).ToArray();
        int Cost(Enemy e)=>GauntletBalance.BossCost(e.cost,e.health);
        double Chance(Enemy e)=>e.hasRewards?Math.Min(1,.05*Math.Log2(Math.Max(1,(int)(e.cost*.2f*.8f))+1)*GauntletBalance.DropMultiplier):0;
        double total=0;
        for(int round=1;round<10;round++)
        {
            var eligible=round==5?pool.Where(e=>e.card.Contains("ScavLunar")).ToArray():
                ordinary.Where(e=>Cost(e)>=GauntletBalance.BossCostFloor(round,1) && Cost(e)<=GauntletBalance.BossCostCeiling(round,1) &&
                    (round!=1 || GauntletBalance.OpeningBoss(e.health,e.damage))).ToArray();
            if(eligible.Length==0)eligible=ordinary.OrderBy(e=>Math.Abs(Cost(e)-Math.Min(GauntletBalance.BossCostCeiling(round,1),4000))).ThenBy(e=>e.health).Take(3).ToArray();
            // Species are uniform; native card variants are uniform within species.
            total+=eligible.GroupBy(e=>e.body).Average(g=>g.Average(Chance))*GauntletPlan.BossCount(round);
        }
        return total;
    }
    // Default BazaarItemsPerRound.
    private const int ClearItems=3;
    internal static void Run(Action<bool,string> check)
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var enemies=JsonSerializer.Deserialize<Enemy[]>(File.ReadAllText(Path.Combine(root,"scripts/checks/native-economy.json")));
        double bossBase=ChampionBonuses(enemies,true),bossAll=ChampionBonuses(enemies,false);
        check(bossBase>6 && bossBase<8 && bossAll>6 && bossAll<8,"Seven shared champion bonus items is supported by the installed native species model");
        var models=new List<object>();var means=new List<double>();
        const int runs=3000;int scenarios=0;
        foreach(int players in new[]{1,2,3,4})
        foreach(int pace in new[]{1,2,4})
        foreach(float scaling in new[]{3f,3.5f})
        foreach(string strategy in new[]{"quantity","quality","mixed"})
        {
            var random=new Random(3701);double purchased=0;
            for(int run=0;run<runs;run++)
            {
                long wallet=GauntletBalance.StartingGold;int items=0;
                // There are nine shopping breaks before the tenth-round boss.
                for(int round=1;round<10;round++)
                {
                    int stage=(round-1)/2;double rawMinutes=round*pace;
                    double minutes=Math.Floor(rawMinutes*GauntletBalance.ClockMultiplier(scaling,GauntletBalance.TyphoonScaling));
                    double difficulty=(.7+.3*players+.0506*scaling*Math.Pow(players,.2)*minutes)*Math.Pow(1.15,stage);
                    double merchant=stage==0?.7+.3*players+.046*scaling*Math.Pow(players,.2)*Math.Floor(rawMinutes):difficulty;
                    int chest=(int)(25*Math.Pow(merchant,1.25));
                    int equipment=(int)(50*Math.Pow(difficulty,1.25));
                    int[] drones={ (int)(40*Math.Pow(difficulty,1.25)),(int)(40*Math.Pow(difficulty,1.25)),(int)(60*Math.Pow(difficulty,1.25)) };
                    var tiers=Enumerable.Range(0,6).Select(_=>GauntletShopPricing.RollTier(random.NextDouble(),1,1,1)).ToArray();
                    var prices=tiers.Select(t=>GauntletShopPricing.Price(chest,t)).ToArray();
                    wallet+=GauntletShopPricing.ClearReward(prices,new[]{equipment},drones,ClearItems);
                    // Reserve the actual extra allowance for equipment/drones.
                    wallet-=GauntletShopPricing.ClearReward(Array.Empty<int>(),new[]{equipment},drones,ClearItems);
                    bool cheap=strategy=="quantity" || strategy=="mixed" && random.NextDouble()<.5;
                    foreach(int tier in cheap?new[]{0,1,2}:new[]{2,1,0})
                    {
                        if(!tiers.Contains(tier))continue;
                        int cost=GauntletShopPricing.Price(chest,tier);
                        for(int purchase=0;purchase<GauntletShopPlan.ItemLimit(tier);purchase++)
                            if(wallet>=cost){wallet-=cost;items++;}
                    }
                }
                purchased+=items;
            }
            double shop=purchased/runs,horde=0;for(int round=1;round<=10;round++)horde+=GauntletDropBudget.Expected(round,players,1.6f);
            double low=GauntletStarterPolicy.Items+horde/players+9+shop+Math.Min(bossBase,bossAll)/players;
            double high=GauntletStarterPolicy.Items+horde/players+9+shop+Math.Max(bossBase,bossAll)/players;
            check(low>=66 && high<=78,"Ordinary runs stay near the seventy-three-item goal");
            if(strategy=="mixed")check(low>=70 && high<=75,"Mixed normal purchasing stays near seventy-three items per player, including starters");
            models.Add(new{players,minutesPerRound=pace,selectedDifficultyScaling=scaling,strategy,runs,starterItemsPerPlayer=GauntletStarterPolicy.Items,shopItemsPerPlayer=shop,hordeItemsPerPlayer=horde/players,bossClearItemsPerPlayer=9,sharedChampionBonusItems=new[]{bossBase,bossAll},totalItemsPerPlayer=new[]{low,high}});
            means.Add((low+high)/2);scenarios++;
        }
        var output=new{target=73,checkpoint="Before round-ten Mithrix, after starters, all ten hordes and nine shopping breaks",assumptions="Three starter items; horde loot reduced by the same percentage as horde population and credits. Default income, starting gold, rarity, tier caps and multiplier; all available content; 1/2/4 combat minutes per round; Monsoon/Typhoon prices; equipment/drone allowance reserved; mixed/quantity/quality buying; equal shared-loot distribution. Lunar coin purchases, independent item effects, native special death bags, modded loot sources and future post-victory shopping excluded. No inventory cap.",meanTotalItemsPerPlayer=means.Average(),minimumScenarioMean=means.Min(),maximumScenarioMean=means.Max(),runs=runs*scenarios,models};
        Directory.CreateDirectory(Path.Combine(root,".build/release-031"));
        File.WriteAllText(Path.Combine(root,".build/release-031/item-run-model.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Full-run items: {scenarios*runs:N0} purchase simulations; {means.Min():F1}-{means.Max():F1} items/player, mean {means.Average():F1}; nine shops, boss items, actual production prices/limits and solo/co-op budgets included.");
    }
}
