using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using BazaarGauntlet;

internal static class CompanyDamageAudit
{
    private sealed class Enemy
    {
        public string card{get;set;} public string body{get;set;}
        public int cost{get;set;} public double health{get;set;} public double damage{get;set;}
        public bool champion{get;set;} public bool forbidden{get;set;} public bool released{get;set;}
        public string[] states{get;set;}
    }
    // Effective coefficients include native attack counts/cooldowns, expected
    // firing/contact time and projectile accuracy. Jailer includes stored bursts.
    // These are explicit hypothetical assumptions, not measured Unity DPS.
    private static readonly double[] leaderRate={1.65,1.3,.975,1.5,4.68,1.2,1.45,3.36,.1,2.45};
    private static readonly double[] squadRate={.727,.667,.4145,.4375,1.05,.8,.8,.4875,.4375,3.36};
    private static readonly int[] levels={2,6,11,14,17,20,24,28,32,36};
    private static int[] Loadout(int slots,int[] options,Random rng)=>options.OrderBy(_=>rng.Next()).Take(slots).ToArray();
    internal static void Run(Action<bool,string> check)
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var enemies=JsonSerializer.Deserialize<Enemy[]>(File.ReadAllText(Path.Combine(root,"scripts/checks/native-economy.json")));
        var native=enemies.GroupBy(r=>r.card).ToDictionary(g=>g.Key,g=>g.First());
        var roster=GauntletCommandRoster.Commanders;
        for(int commander=0;commander<roster.Length;commander++)
        foreach(bool leader in new[]{true,false})
        {
            int profile=GauntletCompanyDamagePolicy.ProfileFor(commander,leader);
            check((profile>0)==(roster[commander].Tier>=3),"Opening companies stay intact; all six higher-tier leaders and squads have explicit role profiles");
            foreach(int slots in new[]{1,2,3})
            {
                int code=GauntletCompanyDamagePolicy.Encode(profile,slots);
                check(profile==GauntletCompanyDamagePolicy.Profile(code),"Every profile survives native count encoding");
                if(profile>0)check(GauntletCompanyDamagePolicy.Slots(code)==slots,"Party allowance survives the same replicated count");
                else check(GauntletCompanyDamagePolicy.Multiplier(code)==1,"Lower-tier units never gain a hidden damage change in co-op");
            }
        }
        foreach(int code in new[]{-1,0,40,int.MaxValue})
            check(GauntletCompanyDamagePolicy.Multiplier(code)==1,"Missing or malformed codes do not affect unrelated units");
        for(int profile=1;profile<=GauntletCompanyDamagePolicy.Profiles;profile++)
        {
            int code=GauntletCompanyDamagePolicy.Encode(profile,3);
            foreach(int slots in new[]{2,1,3,2,3})
            {
                code=GauntletCompanyDamagePolicy.Encode(GauntletCompanyDamagePolicy.Profile(code),slots);
                check(GauntletCompanyDamagePolicy.Profile(code)==profile && GauntletCompanyDamagePolicy.Slots(code)==slots,"Join/disconnect/reconnect recodes the same profile without accumulating bonuses");
            }
        }
        double Damage(int commander,bool leader,int level,bool after,int slots)
        {
            var def=roster[commander];string key=(leader?def.LeaderCard:def.SquadCard).Replace("_asset","");
            double damage=native[key].damage*(1+.2*(level-1))*(1+.1*GauntletCommandRoster.DamageStacks(def.Tier,leader));
            return damage*(after?GauntletCompanyDamagePolicy.Multiplier(GauntletCompanyDamagePolicy.Encode(GauntletCompanyDamagePolicy.ProfileFor(commander,leader),slots)):1);
        }
        double Dps(int commander,int level,bool after,int slots)
        {
            double rate=commander==8 && after?.5:leaderRate[commander];
            double result=Damage(commander,true,level,after,slots)*rate+roster[commander].SquadSize*Damage(commander,false,level,after,slots)*squadRate[commander];
            if(commander==6)
                result+=3*15*(1+.2*(level-1))*.8*(after?GauntletCompanyDamagePolicy.Multiplier(GauntletCompanyDamagePolicy.Encode(GauntletCompanyDamagePolicy.SolusProbe,slots)):1);
            return result;
        }
        var excluded=new[]{"ally","friendly","drone","turret","dummy","invades","infestor","parasite","healingcore","shopkeeper","throne","phantom","glass"};
        var pool=enemies.Where(e=>e.released && GauntletBossPolicy.Standalone(e.card,e.body,e.states) && !excluded.Any(s=>(e.card+e.body).Contains(s,StringComparison.OrdinalIgnoreCase))).ToArray();
        double BossHp(int round,int level,int players)
        {
            bool Great(Enemy e)=>new[]{"Brother","ScavLunar","MiniVoidRaidCrabPhase3"}.Any(s=>(e.card+e.body).Contains(s,StringComparison.OrdinalIgnoreCase));
            var eligible=round==5?pool.Where(e=>e.card.Contains("ScavLunar")):
                round==10?pool.Where(e=>e.body=="BrotherBody"):
                pool.Where(e=>e.champion && !e.forbidden && !Great(e) && GauntletBalance.BossCost(e.cost,(float)e.health)>=GauntletBalance.BossCostFloor(round,1) &&
                    GauntletBalance.BossCost(e.cost,(float)e.health)<=GauntletBalance.BossCostCeiling(round,1) && (round>1 || GauntletBalance.OpeningBoss((float)e.health,(float)e.damage)));
            return eligible.GroupBy(e=>e.body).Average(g=>g.Average(e=>e.health))*(1+.3*(level-1))*GauntletPlan.BossCount(round)*(round==5?1.5:round==10?2:1)*players;
        }
        var rows=new List<object>();var rng=new Random(32032);double minimum=1,maximum=0;
        for(int round=4;round<=10;round++)
        foreach(int players in new[]{1,2,3,4})
        {
            int level=levels[round-1],slots=GauntletNetworkPolicy.CompanyCapacity(players,3);
            int[] available=Enumerable.Range(0,roster.Length).Where(i=>GauntletCommandRoster.Unlocked(roster[i].Tier,round,1)).ToArray();
            double[] before=available.Select(i=>Dps(i,level,false,slots)).ToArray(),after=available.Select(i=>Dps(i,level,true,slots)).ToArray();
            double playerDps=12*(1+.2*(level-1))*6*(1+.3*(round-1))*players;
            double hp=BossHp(round,level,players),peak=0,mean=0,beforeMean=0;int models=0;
            for(int sample=0;sample<2000;sample++)
            {
                var party=Enumerable.Range(0,players).Select(_=>Loadout(slots,available,rng)).ToArray();
                if(sample==0)party=Enumerable.Range(0,players).Select(_=>available.OrderByDescending(i=>Dps(i,level,true,slots)).Take(slots).ToArray()).ToArray();
                // Evaluate upgraded armies, not three deliberately retained tier-I units.
                int highest=available.Max(i=>roster[i].Tier);
                if(!party.SelectMany(p=>p).Any(i=>roster[i].Tier==highest))continue;
                bool banner=party.Any(p=>p.Contains(8));
                double current=party.Sum(p=>p.Sum(i=>Dps(i,level,true,slots)))*(banner?1.12:1);
                double previous=party.Sum(p=>p.Sum(i=>Dps(i,level,false,slots)))*(banner?1.12:1);
                double survivor=playerDps*(banner?1.225:1),share=current/(current+survivor),oldShare=previous/(previous+survivor);
                peak=Math.Max(peak,share);mean+=share;beforeMean+=oldShare;models++;
            }
            mean/=models;beforeMean/=models;
            check(peak<.62,"Even focused, fully hired upgraded armies stay near the intended half-damage range under the stated benchmark");
            int[] reference=round>=8?(slots==3?new[]{6,8,9}:slots==2?new[]{6,9}:new[]{9}):round>=6?(slots==3?new[]{4,6,7}:slots==2?new[]{6,7}:new[]{6}):(slots==3?new[]{3,4,5}:slots==2?new[]{4,5}:new[]{4});
            bool referenceBanner=reference.Contains(8);
            double referenceArmy=reference.Sum(i=>Dps(i,level,true,slots))*players*(referenceBanner?1.12:1);
            double referenceBefore=reference.Sum(i=>Dps(i,level,false,slots))*players*(referenceBanner?1.12:1);
            double referencePlayer=playerDps*(referenceBanner?1.225:1);
            double referenceShare=referenceArmy/(referenceArmy+referencePlayer);
            check(referenceShare>.45 && referenceShare<.58,"Representative upgraded armies contribute about half rather than replacing active players");
            minimum=Math.Min(minimum,mean);maximum=Math.Max(maximum,peak);
            rows.Add(new{round,level,players,slots,bossEncounterHp=hp,referencePlayerDps=playerDps,
                averageArmyShareBefore=beforeMean,averageArmyShareAfter=mean,focusedArmyShareAfter=peak,
                referenceComposition=reference.Select(i=>roster[i].Name).ToArray(),referenceArmyDps=referenceArmy,referencePlayerDpsAfterSupport=referencePlayer,
                referenceArmyShare=referenceShare,referenceArmyShareBefore=referenceBefore/(referenceBefore+referencePlayer),referenceArmyBossDamage=hp*referenceShare,
                referenceFightSeconds=hp/(playerDps/(1-mean)*(100d/120)),
                companies=available.Select(i=>new{roster[i].Name,roster[i].Tier,role=Role(i),
                    leaderDamageBefore=Damage(i,true,level,false,slots),leaderDamageAfter=Damage(i,true,level,true,slots),
                    minionDamageBefore=Damage(i,false,level,false,slots),minionDamageAfter=Damage(i,false,level,true,slots),
                    companyDpsBefore=Dps(i,level,false,slots),companyDpsAfter=Dps(i,level,true,slots)}).ToArray()});
        }
        check(Dps(4,20,true,3)>Dps(5,20,true,3) && Dps(6,20,true,3)>Dps(7,20,true,3) && Dps(9,20,true,3)>Dps(8,20,true,3),"Tank/control/buffer options have less direct damage than their tier's gunline/damage option");
        check(Dps(8,20,true,3)>Dps(8,20,false,3),"Allowing a healthy hired Titan to laser compensates its reduced per-hit damage");
        var output=new{target="About half of party boss damage from a fully hired army; no runtime damage quota or cap",assumptions=new{
            levels,playerBaseDamage=12,playerAttackRotationCoefficientPerSecond=6,playerGearMultiplier="1 + 0.3 * (round - 1)",
            leaderRate,squadRate,solusExtraProbes=3,titanHealthyLaserAfter=true,bannerPlayerMultiplier=1.225,bannerArmyMultiplier=1.12,
            notes="Hypothetical accuracy/contact/cooldown coefficients; Jailer includes stored volleys. Native boss species/cost bands, level health growth, multiplayer HP and major boosts are used. Health-gated rock/missile/super attacks, elite/scavenger item modifiers, void executions and external mods can vary real damage. Armor shred benefits both sides of the comparison. No live DPS claim."},
            minimumAverageArmyShare=minimum,maximumFocusedArmyShare=maximum,rows};
        Directory.CreateDirectory(Path.Combine(root,".build/release-032"));
        File.WriteAllText(Path.Combine(root,".build/release-032/company-balance-model.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Company model: native boss HP at unlock/later rounds, role/utility budgets and full armies for 1-4 players; average minimum {minimum:P1}, focused maximum {maximum:P1}; hypothetical assumptions recorded.");
    }
    private static string Role(int id)=>id switch{4=>"ranged gunline / weaken",5=>"armored tank / armor support",6=>"ranged swarm",7=>"durable horde control",8=>"tank / party buffer",9=>"durable ranged siege",_=>"opening company"};
}
