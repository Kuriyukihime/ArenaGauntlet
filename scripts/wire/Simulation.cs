using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BazaarGauntlet;
using UnityEngine;
using UnityEngine.Networking;

// Network simulation: production messages as real installed-UNet bytes, sent over seeded
// hostile links and checked with the production decoders, identity, order and range rules.
internal static partial class Program
{
    private const BindingFlags Statics=BindingFlags.Static|BindingFlags.NonPublic;
    private static T Call<T>(string type,string method,params object[] args)=>(T)Plugin.GetType("BazaarGauntlet."+type).GetMethod(method,Statics).Invoke(null,args);
    private const int MaxTheme=GauntletNetworkPolicy.MaxTheme;
    // The exception types every production receiver catches; anything else escapes the UNet handler.
    private static bool Expected(Exception e)=>e is IOException || e is IndexOutOfRangeException || e is ArgumentException || e is OverflowException || e is FormatException;
    private static MessageBase Decode(string type,byte[] data)
    {
        var message=New(type);
        try{message.Deserialize(new NetworkReader(data));return message;}
        catch(Exception e) when(Expected(e)){return null;}
        catch(Exception e){throw new Exception($"{type} decoding threw {e.GetType().Name}, which no production receiver catches: {e.Message}");}
    }
    private static bool Plausible(MessageBase snapshot)=>(bool)snapshot.GetType().GetMethod("Plausible",Fields).Invoke(snapshot,null);
    private static MessageBase Snapshot(uint run,ulong key,ulong sequence,int round,int cycle,int phase,int quota,int spawned,int defeated,
        int level=1,float radius=140,float scaling=3.5f,string theme="Mixed assault")
    {
        var s=New("GauntletSnapshot");
        Set(s,"RunId",new NetworkInstanceId(run));Set(s,"BossId",NetworkInstanceId.Invalid);Set(s,"Seed",key);Set(s,"Sequence",sequence);
        Set(s,"EnemyLevel",level);Set(s,"Round",round);Set(s,"Cycle",cycle);Set(s,"Phase",(byte)phase);Set(s,"MinimumScaling",scaling);Set(s,"ArenaRadius",radius);
        Set(s,"Quota",quota);Set(s,"Spawned",spawned);Set(s,"Defeated",defeated);Set(s,"Theme",theme);Set(s,"Fortress","{}");return s;
    }
    private static int HordeQuota(int round,int cycle,int players)=>Call<int>("GauntletBalance","HordeSize",round,cycle,players);
    private static int BossQuota(int round,int ascension)=>Call<int>("GauntletMetaPolicy","Bosses",Call<int>("GauntletPlan","BossCount",round),
        Call<object>("GauntletPlan","BossAt",round).ToString()=="Ordinary",ascension);
    private static void Simulate()
    {
        int states=HostStates();
        double snapshotGap=Enumerable.Range(1,20).Max(seed=>Stream(seed,"GauntletSnapshot","RunId",(run,key,n)=>Progression(run,key,n,1+seed%16),Plausible));
        double lootGap=Enumerable.Range(1,20).Max(seed=>Stream(seed,"GauntletLootSnapshot","Run",Loot,_=>true));
        LateJoin();
        int timings=Handshake();
        var (parsed,splitGap)=Flood();
        int fuzzed=Fuzz();
        Console.WriteLine($"Simulation: {states} legitimate host states pass the client range check. 20 seeded 10-minute runs per stream over a link losing 30%, duplicating 10%, " +
            $"reordering up to 3s and damaging 3%: no stale, cross-run or foreign state applied; clients converge; longest gap {snapshotGap:0.0}s (state), {lootGap:0.0}s (loot). " +
            $"Late joiners sync within one heartbeat. {timings} career-handshake timings keep every career that lands within {GauntletNetworkPolicy.CareerGrace}s of loading. " +
            $"A 2,000/s flood gets {parsed} requests parsed in 60s; four split-screen players behind a stalled link still get through every {splitGap:0.0}s. " +
            $"{fuzzed} fuzzed packets raise only the exceptions the receivers catch.");
    }
    // Every state a host can reach must pass the client's range check after a real round trip.
    // One that fails freezes that client's HUD for as long as it lasts (the 0.35.0 class of bug).
    private static int HostStates()
    {
        int states=0;
        void Pass(MessageBase s,string what){states++;Check(Plausible(Decode("GauntletSnapshot",Bytes(s))),"Client rejects a legitimate host state: "+what);}
        IEnumerable<(int,int)> Progress(int quota)=>new[]{(0,0),(quota/2,quota/4),(quota,Math.Max(0,quota-1)),(quota,quota)};
        foreach(int cycle in new[]{1,2,3,5,10,50,1000})
        for(int round=1;round<=10;round++)
        {
            for(int players=1;players<=16;players++)
            {
                int quota=HordeQuota(round,cycle,players);
                foreach(var (spawned,defeated) in Progress(quota))Pass(Snapshot(1,1,1,round,cycle,2,quota,spawned,defeated),$"horde round {round}, cycle {cycle}, {players} players, {defeated}/{spawned} of {quota}");
            }
            for(int ascension=0;ascension<=6;ascension++)
            {
                int quota=BossQuota(round,ascension);
                foreach(var (spawned,defeated) in Progress(quota))Pass(Snapshot(1,1,1,round,cycle,3,quota,spawned,defeated),$"boss round {round}, Ascension {ascension}, {defeated}/{spawned} of {quota}");
            }
        }
        // Every phase, the config's arena radius and scaling limits, the clamped theme length and a long run's enemy level.
        for(int phase=0;phase<=9;phase++)
        foreach(float radius in new[]{40f,160f})foreach(float scaling in new[]{0f,10f})foreach(int theme in new[]{0,MaxTheme})
            Pass(Snapshot(1,1,1,1,1,phase,0,0,0,int.MaxValue,radius,scaling,new string('é',theme)),$"phase {phase}, radius {radius}, scaling {scaling}, theme {theme}");
        Check(!Plausible(Snapshot(1,1,1,1,1,2,0,0,0,theme:new string('x',MaxTheme+1))),"A theme over the limit is still rejected, which is why the host clamps it before sending");
        return states;
    }
    // Host state as a pure function of its sequence: hordes fill then fall, a champion, the exit, the Bazaar, the next round.
    private static MessageBase Progression(uint run,ulong key,ulong n,int players)
    {
        ulong m=n%4000;int round=(int)(m/40%10)+1,cycle=(int)(m/400)+1,step=(int)(m%40);
        if(step<20){int quota=HordeQuota(round,cycle,players);return Snapshot(run,key,n,round,cycle,2,quota,quota*Math.Min(step,10)/10,quota*step/20);}
        int bosses=BossQuota(round,6);
        return step<30?Snapshot(run,key,n,round,cycle,3,bosses,bosses,bosses*(step-20)/10):Snapshot(run,key,n,round,cycle,step<35?4:7,bosses,bosses,bosses);
    }
    private static MessageBase Loot(uint run,ulong key,ulong n)
    {
        var loot=New("GauntletLootSnapshot");Set(loot,"Run",new NetworkInstanceId(run));Set(loot,"Seed",key);Set(loot,"Sequence",n);
        var ids=Enumerable.Range(0,(int)(n%9)).Select(i=>new NetworkInstanceId((uint)(n%1000)*16+(uint)i)).ToArray();
        Set(loot,"Objects",ids);Set(loot,"Owners",ids.Select(id=>new NetworkInstanceId(id.Value%4+1)).ToArray());return loot;
    }
    private sealed class Link
    {
        private readonly System.Random random;private readonly double loss,duplicate,delay,damage;
        private readonly List<(double At,int Order,byte[] Data)> flight=new List<(double,int,byte[])>();private int order;
        internal bool Clean;
        internal Link(int seed,double loss,double duplicate,double delay,double damage){random=new System.Random(seed);this.loss=loss;this.duplicate=duplicate;this.delay=delay;this.damage=damage;}
        internal void Send(double now,byte[] data)
        {
            if(!Clean && random.NextDouble()<loss)return;
            for(int copies=!Clean && random.NextDouble()<duplicate?2:1;copies>0;copies--)
                flight.Add((now+(Clean?0:random.NextDouble()*delay),order++,!Clean && random.NextDouble()<damage?Damage(data):data));
        }
        // A cut-off fragment or unrelated bytes. Bit flips inside a packet are left to UDP checksums.
        private byte[] Damage(byte[] data){if(random.Next(2)==0)return data.Take(random.Next(data.Length)).ToArray();var junk=new byte[random.Next(1,64)];random.NextBytes(junk);return junk;}
        internal List<byte[]> Deliver(double now)
        {
            var due=flight.Where(p=>p.At<=now).OrderBy(p=>p.At).ThenBy(p=>p.Order).Select(p=>p.Data).ToList();
            flight.RemoveAll(p=>p.At<=now);return due;
        }
    }
    // A host→client stream for ten minutes at 50Hz: a 1s heartbeat plus event sends, the previous run's stragglers
    // mixed in, a hostile link until the last ten seconds. Returns the longest time the client went without fresh state.
    private static double Stream(int seed,string type,string runField,Func<uint,ulong,ulong,MessageBase> host,Func<MessageBase,bool> plausible)
    {
        const uint run=77;const ulong key=0x5EED0001UL;
        var link=new Link(seed,.3,.1,3,.03);var random=new System.Random(~seed);
        var sent=new Dictionary<ulong,byte[]>();ulong sequence=0,received=0;double fresh=0,stale=0;
        for(int tick=0;tick<=600*50;tick++)
        {
            double now=tick/50.0;link.Clean=now>=590;
            if(tick%50==0 || random.NextDouble()<.01){var bytes=Bytes(host(run,key,++sequence));sent[sequence]=bytes;link.Send(now,bytes);}
            // The same net ID after a server restart with a new run key, or the same key on another net ID, at the highest sequence.
            if(random.NextDouble()<.005)link.Send(now,Bytes(host(run,key+1,ulong.MaxValue)));
            if(random.NextDouble()<.005)link.Send(now,Bytes(host(run+1,key,ulong.MaxValue)));
            foreach(var data in link.Deliver(now))
            {
                var packet=Decode(type,data);if(packet==null)continue;
                ulong s=(ulong)Get(packet,"Sequence");
                // The production receivers' identity and order guard, then the message's own range check.
                if(!GauntletNetworkPolicy.MatchesRun(run,key,((NetworkInstanceId)Get(packet,runField)).Value,(ulong)Get(packet,"Seed")) || s<=received || !plausible(packet))continue;
                Check(sent.TryGetValue(s,out var original) && original.SequenceEqual(data),type+": a client applies only state its own host sent this run");
                received=s;stale=Math.Max(stale,now-fresh);fresh=now;
            }
        }
        Check(received==sequence,type+": once the link recovers the client holds the host's latest state");
        return stale;
    }
    // A joining or reconnecting client starts from sequence 0, so the next heartbeat brings it up to date.
    private static void LateJoin()
    {
        foreach(double join in new[]{.01,37.3,59.99,119.5})
        {
            ulong received=0;double synced=-1;
            for(ulong second=0;second<=130;second++)
            {
                var bytes=Bytes(Progression(5,9,second+1,4));if(second<join)continue;
                var packet=Decode("GauntletSnapshot",bytes);ulong s=(ulong)Get(packet,"Sequence");
                if(s>received && Plausible(packet)){received=s;if(synced<0)synced=second;}
            }
            Check(synced>=join && synced-join<=1,$"A client joining at {join}s receives the host's state within one heartbeat");
        }
    }
    // Client→host career handshake. A career that reaches the host within CareerGrace of the player finishing loading is kept.
    private static int Handshake()
    {
        int cases=0;
        foreach(double load in new[]{0,3,8.5,30,90,240})
        foreach(double arrival in new[]{.2,2,5,7.5,double.PositiveInfinity})
        foreach(bool current in new[]{true,false})
        {
            float since=float.NaN;bool kept=false;double decided=-1;
            for(int tick=0;tick<=300*50;tick++)
            {
                float now=tick/50f;
                if(now>=load+arrival){kept=true;decided=now;break;}
                // `current` is this release's rule. Otherwise the timer starts at the first check while the player still loads, as in 1.0.1.
                if(GauntletNetworkPolicy.CareerOverdue(ref since,current && now<load,now)){decided=now;break;}
            }
            cases++;
            if(!current){if(load>=8.5 && !double.IsInfinity(arrival))Check(!kept,"1.0.1's timer replaced a slow loader's career, so this simulation catches that regression");}
            else if(double.IsInfinity(arrival))Check(!kept && Math.Abs(decided-(load+GauntletNetworkPolicy.CareerGrace))<=.03,$"A player loading for {load}s who never sends a career gets a fresh one {GauntletNetworkPolicy.CareerGrace}s after loading");
            else Check(kept,$"A player loading for {load}s whose career lands {arrival}s later keeps it");
        }
        return cases;
    }
    private static (int,double) Flood()
    {
        var flood=new GauntletRequestBudget();int parsed=0;
        for(int i=0;i<120000;i++)if(flood.Take(i/2000.0))parsed++;
        Check(parsed<=4+8*60+1,"A client sending 2,000 requests a second gets at most the burst plus eight a second parsed");
        // Four split-screen players share one connection and submit once a second; a stalled link delivers up to 2s of them at once.
        var random=new System.Random(9);var shared=new GauntletRequestBudget();var last=new double[4];double worst=0;
        foreach(var (at,user) in Enumerable.Range(0,300*4).Select(i=>(At:i/4+random.NextDouble()*2,User:i%4)).OrderBy(a=>a.At).ToList())
            if(shared.Take(at)){worst=Math.Max(worst,at-last[user]);last[user]=at;}
        Check(worst<GauntletNetworkPolicy.CareerGrace,"Split-screen players behind a stalled link still get careers through within the grace period");
        return (parsed,worst);
    }
    // Truncated, extended, bit-flipped and random packets for every message type, in both directions.
    private static int Fuzz()
    {
        var request=New("GauntletCommandRequest");Set(request,"RunId",new NetworkInstanceId(77));Set(request,"User",new NetworkInstanceId(3));Set(request,"Seed",9UL);Set(request,"Commander",(byte)4);
        var career=New("GauntletMetaPacket");Set(career,"Scope",new NetworkInstanceId(77));Set(career,"User",new NetworkInstanceId(3));Set(career,"Seed",9UL);Set(career,"Json","{\"Earned\":59,\"Armory\":1,\"Labors\":[\"initiate\"]}");
        var effect=New("FortressEffect");Set(effect,"Run",new NetworkInstanceId(77));Set(effect,"Seed",9UL);Set(effect,"From",new Vector3(1,2,3));Set(effect,"To",new Vector3(4,5,6));Set(effect,"Radius",6f);
        var companies=New("GauntletCommandSnapshot");Set(companies,"RunId",new NetworkInstanceId(77));Set(companies,"Seed",9UL);Set(companies,"Sequence",3UL);Set(companies,"Capacity",(byte)2);
        for(int i=0;i<2;i++)
        {
            var view=Activator.CreateInstance(Plugin.GetType("BazaarGauntlet.GauntletCompanies+View"),true);Set(view,"Player",(ulong)i+1);Set(view,"Renown",500L);Set(view,"Capacity",2);
            for(int j=0;j<2;j++)
            {
                var company=Activator.CreateInstance(Plugin.GetType("BazaarGauntlet.GauntletCompanies+CompanyView"),true);Set(company,"Commander",j);Set(company,"Units",Enumerable.Range(1,3).Select(u=>new NetworkInstanceId((uint)u)).ToArray());
                ((System.Collections.IList)Get(view,"Companies")).Add(company);
            }
            ((System.Collections.IList)Get(companies,"Views")).Add(view);
        }
        var samples=new[]{("GauntletSnapshot",Progression(77,9,25,4)),("GauntletLootSnapshot",Loot(77,9,8)),("GauntletCommandSnapshot",companies),
            ("GauntletCommandRequest",request),("GauntletMetaPacket",career),("FortressEffect",effect)};
        var random=new System.Random(4321);int packets=0;
        foreach(var (type,message) in samples)
        {
            var valid=Bytes(message);
            for(int i=0;i<20000;i++)
            {
                byte[] data;
                switch(i%4)
                {
                    case 0:data=new byte[random.Next(0,300)];random.NextBytes(data);break;
                    case 1:data=valid.Take(random.Next(valid.Length+1)).ToArray();break;
                    case 2:data=(byte[])valid.Clone();for(int flips=random.Next(1,4);flips>0;flips--)data[random.Next(data.Length)]^=(byte)random.Next(1,256);break;
                    default:data=valid.Concat(Enumerable.Range(0,random.Next(1,40)).Select(_=>(byte)random.Next(256))).ToArray();break;
                }
                packets++;Decode(type,data);
            }
        }
        return packets;
    }
}
