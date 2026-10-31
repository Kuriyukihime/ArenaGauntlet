using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarGauntlet
{
    internal enum CommandAbility : byte { Kindle, Carapace, Hymn, Toll, Hex, Shatter, Galvanic, Collapse, Warcry, Riftstorm }
    internal enum CommandResult : byte { Hired, Dismissed, Unknown, Unavailable, Locked, Unaffordable, AlreadyCommanding, NoCapacity, NotCommanding, Throttled }
    internal sealed class CommanderDef
    {
        internal string Name, LeaderName, SquadName, AbilityName, AbilityText, LeaderCard, SquadCard;
        internal int Tier, Cost, SquadSize;
        internal CommandAbility Ability;
        internal float Radius, Period, Power, R, G, B;
        internal bool Allies => Ability == CommandAbility.Carapace || Ability == CommandAbility.Hymn || Ability == CommandAbility.Warcry;
    }
    // Native enemy bodies keep their own AI and skill drivers; hired units only
    // change team. Field names are RoR2BepInExPack registry keys.
    internal static class GauntletCommandRoster
    {
        internal static readonly CommanderDef[] Commanders =
        {
            new CommanderDef{Name="Cinder Warband",LeaderName="Elder Lemurian",SquadName="Lemurian",LeaderCard="cscLemurianBruiser_asset",SquadCard="cscLemurian_asset",SquadSize=3,Tier=1,Cost=20,
                Ability=CommandAbility.Kindle,AbilityName="Kindle",AbilityText="Sets nearby foes ablaze",Radius=18,Period=6,Power=1,R=1,G=.45f,B=.16f},
            new CommanderDef{Name="Chitin Phalanx",LeaderName="Beetle Guard",SquadName="Beetle",LeaderCard="cscBeetleGuardAlly_asset",SquadCard="cscBeetle_asset",SquadSize=4,Tier=1,Cost=20,
                Ability=CommandAbility.Carapace,AbilityName="Carapace",AbilityText="Barrier for nearby allies",Radius=20,Period=10,Power=.12f,R=.62f,G=.82f,B=.24f},
            new CommanderDef{Name="Wisp Choir",LeaderName="Greater Wisp",SquadName="Lesser Wisp",LeaderCard="cscGreaterWisp_asset",SquadCard="cscLesserWisp_asset",SquadSize=4,Tier=2,Cost=40,
                Ability=CommandAbility.Hymn,AbilityName="Mending Hymn",AbilityText="Heals nearby allies",Radius=24,Period=7,Power=.1f,R=.98f,G=.78f,B=.42f},
            new CommanderDef{Name="Bellward Garrison",LeaderName="Brass Contraption",SquadName="Stone Golem",LeaderCard="cscBell_asset",SquadCard="cscGolem_asset",SquadSize=2,Tier=2,Cost=40,
                Ability=CommandAbility.Toll,AbilityName="Tolling Ward",AbilityText="Slows nearby foes",Radius=22,Period=8,Power=3,R=.85f,G=.66f,B=.3f},
            new CommanderDef{Name="Clay Templars",LeaderName="Clay Templar",SquadName="Clay Apothecary",LeaderCard="cscClayBruiser_asset",SquadCard="cscClayGrenadier_asset",SquadSize=2,Tier=3,Cost=80,
                Ability=CommandAbility.Hex,AbilityName="Tar Hex",AbilityText="Weakens nearby foes",Radius=25,Period=9,Power=4,R=.55f,G=.36f,B=.22f},
            new CommanderDef{Name="Lunar Vanguard",LeaderName="Lunar Chimera Golem",SquadName="Lunar Chimera Exploder",LeaderCard="cscLunarGolem_asset",SquadCard="cscLunarExploder_asset",SquadSize=3,Tier=3,Cost=80,
                Ability=CommandAbility.Shatter,AbilityName="Shatterlight",AbilityText="Strips nearby foes' armor",Radius=25,Period=8,Power=4,R=.4f,G=.62f,B=1},
            new CommanderDef{Name="Solus Directive",LeaderName="Solus Control Unit",SquadName="Solus Probe",LeaderCard="cscRoboBallBoss_asset",SquadCard="cscRoboBallMini_asset",SquadSize=4,Tier=4,Cost=140,
                Ability=CommandAbility.Galvanic,AbilityName="Galvanic Arc",AbilityText="Chain lightning strikes foes",Radius=30,Period=5,Power=2.5f,R=.3f,G=.9f,B=1},
            new CommanderDef{Name="Void Covenant",LeaderName="Void Jailer",SquadName="Void Reaver",LeaderCard="cscVoidJailerAlly_asset",SquadCard="cscNullifierAlly_asset",SquadSize=2,Tier=4,Cost=140,
                Ability=CommandAbility.Collapse,AbilityName="Collapse Field",AbilityText="Roots nearby ordinary foes",Radius=20,Period=10,Power=2.5f,R=.74f,G=.32f,B=1},
            new CommanderDef{Name="Titan Legion",LeaderName="Stone Titan",SquadName="Stone Golem",LeaderCard="cscTitanGolemPlains_asset",SquadCard="cscGolem_asset",SquadSize=3,Tier=5,Cost=220,
                Ability=CommandAbility.Warcry,AbilityName="Warcry",AbilityText="Rallies nearby allies' speed",Radius=30,Period=8,Power=6,R=.92f,G=.86f,B=.72f},
            new CommanderDef{Name="Abyssal Siege",LeaderName="Void Devastator",SquadName="Void Jailer",LeaderCard="cscVoidMegaCrabAlly_asset",SquadCard="cscVoidJailerAlly_asset",SquadSize=2,Tier=5,Cost=220,
                Ability=CommandAbility.Riftstorm,AbilityName="Riftstorm",AbilityText="Blasts nearby foes",Radius=26,Period=6,Power=4,R=.95f,G=.2f,B=.58f},
        };
        internal static int UnlockRound(int tier)=>GauntletPlan.UnlockRound(tier<=1?1:tier==2?3:tier==3?6:tier==4?10:14);
        internal static bool Unlocked(int tier,int round,int cycle)=>cycle>1 || round>=UnlockRound(tier);
        // Ten-percent native stat items; commanders lead from the front.
        internal static int HealthStacks(int tier,bool leader)=>new[]{0,3,6,10,15}[Math.Max(1,Math.Min(5,tier))-1]*(leader?2:1)+(leader?5:0);
        // Preserve the opening tiers, then grow by one stack per tier: a steady
        // +20 percentage points for commanders and +10 for squad members.
        internal static int DamageStacks(int tier,bool leader)=>new[]{0,2,3,4,5}[Math.Max(1,Math.Min(5,tier))-1]*(leader?2:1);
        // Base durability approximates threat without favoring scaled levels.
        internal static long Renown(float baseHealth,bool elite,float multiplier)=>Scale((1+(long)(Math.Max(0,Math.Min(1e6f,baseHealth))/400))*(elite?2:1),multiplier);
        internal static long BossRenown(int round,int cycle,GauntletBossKind kind,float multiplier)=>
            Scale((long)Math.Round((10+2*GauntletPlan.OriginalRound(round)+10L*Math.Max(0,cycle-1))*(kind==GauntletBossKind.Final?3:kind==GauntletBossKind.Great?2:1)),multiplier);
        private static long Scale(long value,float multiplier)=>float.IsNaN(multiplier) || multiplier<=0?0:(long)Math.Round(value*Math.Min(10f,multiplier));
    }
    // Pure server ledger: per-player renown and companies. No Unity types.
    internal sealed class GauntletWarCouncil
    {
        internal sealed class Company { internal int Commander; internal bool Fallen; }
        internal sealed class Account
        {
            internal long Renown, Earned;
            internal double LastRequest = double.NegativeInfinity;
            internal readonly List<Company> Companies = new List<Company>();
            internal int Active => Companies.Count(company => !company.Fallen);
            internal Company Find(int commander) => Companies.FirstOrDefault(company => company.Commander == commander);
        }
        internal const double RequestInterval=.15;
        private readonly Dictionary<ulong, Account> accounts = new Dictionary<ulong, Account>();
        internal ulong Revision { get; private set; }
        internal IEnumerable<KeyValuePair<ulong, Account>> Accounts => accounts;
        internal Account Get(ulong player)
        {
            if (!accounts.TryGetValue(player, out var account)) { accounts[player] = account = new Account(); Revision++; }
            return account;
        }
        internal bool Award(ulong player, long amount)
        {
            if (amount <= 0) return false;
            var account = Get(player);
            account.Renown = account.Renown > long.MaxValue - amount ? long.MaxValue : account.Renown + amount;
            account.Earned = account.Earned > long.MaxValue - amount ? long.MaxValue : account.Earned + amount;
            Revision++; return true;
        }
        private static bool Throttle(Account account, double now)
        {
            if (now - account.LastRequest < RequestInterval) return true;
            account.LastRequest = now; return false;
        }
        internal CommandResult Hire(ulong player, int commander, int round, int cycle, int capacity, bool available, double now)
        {
            if (commander < 0 || commander >= GauntletCommandRoster.Commanders.Length) return CommandResult.Unknown;
            var account = Get(player);
            if (Throttle(account, now)) return CommandResult.Throttled;
            var def = GauntletCommandRoster.Commanders[commander];
            if (!available) return CommandResult.Unavailable;
            if (!GauntletCommandRoster.Unlocked(def.Tier, round, cycle)) return CommandResult.Locked;
            var existing = account.Find(commander);
            if (existing != null && !existing.Fallen) return CommandResult.AlreadyCommanding;
            if (account.Active >= Math.Max(1, capacity)) return CommandResult.NoCapacity;
            if (account.Renown < def.Cost) return CommandResult.Unaffordable;
            account.Renown -= def.Cost;
            // A fallen banner is raised again in its existing roster position.
            if (existing != null) existing.Fallen = false;
            else account.Companies.Add(new Company { Commander = commander });
            Revision++; return CommandResult.Hired;
        }
        internal CommandResult Dismiss(ulong player, int commander, double now)
        {
            if (commander < 0 || commander >= GauntletCommandRoster.Commanders.Length) return CommandResult.Unknown;
            var account = Get(player);
            if (Throttle(account, now)) return CommandResult.Throttled;
            var existing = account.Find(commander);
            if (existing == null) return CommandResult.NotCommanding;
            // Discharged and fallen companies alike leave without a refund.
            account.Companies.Remove(existing);Revision++;
            return CommandResult.Dismissed;
        }
        internal bool Fall(ulong player, int commander)
        {
            if (!accounts.TryGetValue(player, out var account)) return false;
            var existing = account.Find(commander);
            if (existing == null || existing.Fallen) return false;
            existing.Fallen = true; Revision++; return true;
        }
        internal bool Grant(ulong player,int commander,int capacity)
        {
            var account=Get(player);
            if(commander<0 || commander>1 || account.Active>=capacity || account.Find(commander)!=null)return false;
            account.Companies.Add(new Company{Commander=commander});Revision++;return true;
        }
        internal bool Recover(ulong player,int commander)
        {
            var company=Get(player).Find(commander);if(company==null || !company.Fallen)return false;
            company.Fallen=false;Revision++;return true;
        }
        internal List<(ulong Player,int Commander)> EnforceCapacity(int capacity)=>EnforceCapacity(_=>capacity);
        internal List<(ulong Player,int Commander)> EnforceCapacity(Func<ulong,int> capacity)
        {
            var removed=new List<(ulong Player,int Commander)>();
            foreach(var pair in accounts)
                for(int i=pair.Value.Companies.Count-1;pair.Value.Active>Math.Max(1,capacity(pair.Key)) && i>=0;i--)
                {
                    var company=pair.Value.Companies[i];if(company.Fallen)continue;
                    long refund=GauntletCommandRoster.Commanders[company.Commander].Cost;
                    pair.Value.Renown=Math.Min(long.MaxValue-refund,pair.Value.Renown)+refund;
                    pair.Value.Companies.RemoveAt(i);Revision++;removed.Add((pair.Key,company.Commander));
                }
            return removed;
        }
        internal void Reset() { accounts.Clear(); Revision++; }
    }
}
