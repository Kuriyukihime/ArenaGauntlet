using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using BazaarGauntlet;

internal static class HordeAudit
{
    // 0.39 lowered the per-player horde share from the previous 37 items.
    private static double Target(int players)=>((GauntletDropBudget.ItemsPerPlayerAtMithrix-GauntletDropBudget.ExpectedShopItemsPerPlayer-GauntletDropBudget.ClearItemsBeforeMithrix)*(double)players-7)/(37d*players-7);
    internal static void Run(Action<bool,string> check)
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        using var zip=ZipFile.OpenRead(Path.Combine(root,"dist/ArenaGauntlet-0.30.1.zip"));
        using var memory=new MemoryStream();
        using(var dll=zip.GetEntry("BepInEx/plugins/ArenaGauntlet/ArenaGauntlet.dll").Open())dll.CopyTo(memory);
        var assembly=Assembly.Load(memory.ToArray());
        var balance=assembly.GetType("BazaarGauntlet.GauntletBalance");
        var drops=assembly.GetType("BazaarGauntlet.GauntletDropBudget");
        object Old(Type type,string method,params object[] args)=>type.GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);
        foreach(int cycle in new[]{1,2,10,100})
        for(int players=1;players<=16;players++)
        for(int round=1;round<=10;round++)
        {
            int previous=(int)Old(balance,"HordeSize",round,cycle,players);
            int current=GauntletBalance.HordeSize(round,cycle,players);
            double ratio=(double)current/previous;
            check(previous==GauntletBalance.HordeRewardSlots(round,cycle,players),"The reward reference retains the actual previous population");
            check(current<=previous && current>0 && (round>4 || current==previous),"Only later hordes shrink, including co-op and continued cycles");
            foreach(int ceiling in new[]{4,24,80,160})
                check(GauntletBalance.ConcurrentLimit(round,cycle,players,ceiling)==(int)Old(balance,"ConcurrentLimit",round,cycle,players,ceiling),"User's live cap and its opening progression stay unchanged");
            double initial=(float)Old(balance,"InitialCredits",round,players)*ratio;
            check(Math.Abs(GauntletBalance.InitialCredits(round,players,cycle)-initial)<Math.Max(1e-4,initial*1e-6),"Starting credits shrink by exactly the population percentage");
            foreach(float difficulty in new[]{1f,3f,3.5f,8f})
            {
                double expected=(float)Old(balance,"CreditRate",round,players,difficulty)*ratio;
                check(Math.Abs(GauntletBalance.CreditRate(round,players,difficulty,cycle)-expected)<Math.Max(1e-4,expected*1e-6),"Ongoing director income uses the same population percentage");
            }
            foreach(float multiplier in new[]{0f,.8f,1.6f,3.2f})
                check(Math.Abs(GauntletDropBudget.Expected(round,players,multiplier,cycle)-(double)Old(drops,"Expected",round,players,multiplier)*ratio*Target(players))<1e-8,"Horde loot uses the same percentage as population and credits, under the lower 0.39 item target");
            var rewards=new GauntletHordeRewards(previous,current);
            ulong xp=0;for(int i=0;i<current;i++)xp+=rewards.Experience(7);
            check(xp==(ulong)previous*7,"A constant-value horde retains its former XP, including fractional carry");
            var renown=new GauntletHordeRewards(previous,current);var actual=new long[players];var baseValue=new long[players];
            for(int i=0;i<current;i++)
            {
                int player=i%players;int amount=1+i%5;baseValue[player]+=amount;
                actual[player]+=renown.Renown((ulong)player,amount);
            }
            for(int player=0;player<players;player++)
                check(actual[player]==(long)((BigInteger)baseValue[player]*previous/current),"Renown carry belongs to each owner and matches exact integer arithmetic");
        }
        for(int players=1;players<=4;players++)
            check(GauntletBalance.HordeSize(10,1,players)==40*(players+1),"First-cycle high end is 80/120/160/200, with the established party scaling");
        foreach(var ratio in new[]{(118,80),(500,1),(1,1)})
        {
            var rewards=new GauntletHordeRewards(ratio.Item1,ratio.Item2);
            check(rewards.Experience(uint.MaxValue)==uint.MaxValue && rewards.Renown(1,long.MaxValue)==long.MaxValue,"Extreme rewards saturate rather than wrapping");
            check(rewards.Renown(1,0)==0 && rewards.Renown(1,-1)==0,"Zero or invalid renown cannot create an award");
        }
        var rows=Enumerable.Range(1,10).Select(round=>new{round,players=Enumerable.Range(1,4).Select(players=>new{players,
            quota=GauntletBalance.HordeSize(round,1,players),alive=GauntletBalance.ConcurrentLimit(round,1,players,80),
            previousQuota=GauntletBalance.HordeRewardSlots(round,1,players),ratio=GauntletBalance.HordePopulationRatio(round,1,players),
            hordeItems=GauntletDropBudget.Expected(round,players,1.6f)}).ToArray()}).ToArray();
        Directory.CreateDirectory(Path.Combine(root,".build/release-031"));
        File.WriteAllText(Path.Combine(root,".build/release-031/horde-model.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Hordes: 80/120/160/200 final quota; unchanged live caps; population, starting/ongoing credits and loot ratios verified against 0.30.1; XP/renown carry preserved.");
    }
}
