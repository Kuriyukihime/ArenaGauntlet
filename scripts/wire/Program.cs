using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BazaarGauntlet;
using UnityEngine;
using UnityEngine.Networking;

internal static partial class Program
{
    private static int checks;
    private static readonly Assembly Plugin=typeof(GauntletSnapshot).Assembly;
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    private static object Get(object value,string name)=>value.GetType().GetField(name,Fields).GetValue(value);
    private static void Set(object value,string name,object field)=>value.GetType().GetField(name,Fields).SetValue(value,field);
    private static MessageBase New(string name)=>(MessageBase)Activator.CreateInstance(Plugin.GetType("BazaarGauntlet."+name),true);
    private static void Check(bool valid,string message){checks++;if(!valid)throw new Exception(message);}
    private static byte[] Bytes(MessageBase value){var writer=new NetworkWriter();value.Serialize(writer);return writer.ToArray();}
    private static MessageBase Copy(MessageBase value)
    {
        var bytes=Bytes(value);var copy=(MessageBase)Activator.CreateInstance(value.GetType(),true);var reader=new NetworkReader(bytes);copy.Deserialize(reader);
        Check(reader.Position==reader.Length,"Every field is consumed in the installed UNet wire format");
        Check(Bytes(copy).SequenceEqual(bytes),"Installed writer/reader round trip preserves every byte");return copy;
    }
    private static void Invalid(byte[] bytes)
    {
        try{New("GauntletCommandSnapshot").Deserialize(new NetworkReader(bytes));throw new Exception("Invalid company packet was accepted");}
        catch(FormatException){checks++;}
        catch(IndexOutOfRangeException){checks++;}
        catch(System.IO.EndOfStreamException){checks++;}
    }
    private static byte[] Header(byte count)
    {
        var writer=new NetworkWriter();writer.Write(new NetworkInstanceId(123));writer.Write(42UL);writer.Write(1UL);writer.Write((byte)3);writer.Write(count);return writer.ToArray();
    }
    public static void Main()
    {
        foreach(uint run in new uint[]{1,255,65536,uint.MaxValue})
        foreach(ulong seed in new ulong[]{0,42,ulong.MaxValue})
        {
            var request=New("GauntletCommandRequest");Set(request,"RunId",new NetworkInstanceId(run));Set(request,"User",new NetworkInstanceId(run-1));Set(request,"Seed",seed);Set(request,"Dismiss",true);Set(request,"Commander",(byte)9);
            var copy=Copy(request);Check(((NetworkInstanceId)Get(copy,"RunId")).Value==run && (ulong)Get(copy,"Seed")==seed,"Current run ID and seed survive client-to-host serialization");
            Check(Bytes(request).Length<=64,"Request remains within host's packet bound");
        }
        var snapshot=New("GauntletSnapshot");
        foreach(string name in new[]{"RunId","BossId"})Set(snapshot,name,new NetworkInstanceId(uint.MaxValue));
        Set(snapshot,"Seed",ulong.MaxValue);Set(snapshot,"Sequence",ulong.MaxValue);Set(snapshot,"EnemyLevel",99);Set(snapshot,"Round",10);Set(snapshot,"Cycle",5);Set(snapshot,"Phase",(byte)3);Set(snapshot,"ArenaRadius",140f);Set(snapshot,"MinimumScaling",3.5f);Set(snapshot,"Spawned",500);Set(snapshot,"Quota",500);Set(snapshot,"Defeated",499);Set(snapshot,"Theme","Mixed assault — native DLC");Set(snapshot,"Fortress","{\"Revision\":12,\"Preparing\":false}");Set(snapshot,"AllowLoop",true);Copy(snapshot);
        var stateType=Plugin.GetType("BazaarGauntlet.FortressState");var state=Activator.CreateInstance(stateType,true);
        Set(state,"ShopPlayers",Enumerable.Range(0,64).Select(index=>uint.MaxValue-(uint)index).ToArray());
        Set(state,"ShopBought",Enumerable.Repeat((1<<25)-1,64).ToArray());
        string shopState=System.Text.Json.JsonSerializer.Serialize(state,stateType,new System.Text.Json.JsonSerializerOptions{IncludeFields=true});
        Set(snapshot,"Fortress",shopState);var shopCopy=Copy(snapshot);
        Check((string)Get(shopCopy,"Fortress")==shopState && shopState.Length<24000 && Bytes(snapshot).Length<32768,"Expanded 25-slot purchase masks for 64 players retain every bit within existing host packet bounds");
        var effect=New("FortressEffect");Set(effect,"Run",new NetworkInstanceId(5));Set(effect,"Seed",123UL);Set(effect,"From",new Vector3(1,2,3));Set(effect,"To",new Vector3(4,5,6));Set(effect,"Radius",20f);Copy(effect);
        var companies=New("GauntletCommandSnapshot");Set(companies,"RunId",new NetworkInstanceId(123));Set(companies,"Seed",42UL);Set(companies,"Sequence",1UL);Set(companies,"Capacity",(byte)3);
        var views=(IList)Get(companies,"Views");
        for(int i=0;i<16;i++)
        {
            var view=Activator.CreateInstance(Plugin.GetType("BazaarGauntlet.GauntletCompanies+View"),true);Set(view,"Player",(ulong)i+1);Set(view,"Renown",long.MaxValue);Set(view,"Capacity",i%4+1);
            for(int j=0;j<10;j++)
            {
                var company=Activator.CreateInstance(Plugin.GetType("BazaarGauntlet.GauntletCompanies+CompanyView"),true);Set(company,"Commander",j);Set(company,"Fallen",j%2==0);Set(company,"Units",Enumerable.Repeat(new NetworkInstanceId(uint.MaxValue),12).ToArray());((IList)Get(view,"Companies")).Add(company);
            }
            views.Add(view);
        }
        var full=Bytes(companies);Copy(companies);Check(full.Length<32768,"Maximum company state fits the host-only transport packet bound");
        Invalid(Header(17));
        var tooManyCompanies=Header(1).Concat(new byte[16]).Concat(new byte[]{4,13}).ToArray();Invalid(tooManyCompanies);
        var tooManyUnits=Header(1).Concat(new byte[16]).Concat(new byte[]{4,1,0,0,13}).ToArray();Invalid(tooManyUnits);
        for(int size=0;size<=4096;size+=256)
        {
            var career=New("GauntletMetaPacket");Set(career,"Scope",new NetworkInstanceId(22));Set(career,"User",new NetworkInstanceId(4));Set(career,"Seed",123UL);Set(career,"Json",new string('x',size));
            var careerCopy=Copy(career);Check((string)Get(careerCopy,"Json")==new string('x',size),"Career and hazard payloads preserve run identity and bounded content through installed UNet");
        }
        var oversized=New("GauntletMetaPacket");Set(oversized,"Json",new string('x',4097));
        try{Copy(oversized);throw new Exception("Oversized career accepted");}catch(FormatException){checks++;}
        int maxLoot=(int)Plugin.GetType("BazaarGauntlet.GauntletLootSnapshot").GetField("MaxEntries",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var lootIds=Enumerable.Range(1,maxLoot).Select(i=>new NetworkInstanceId((uint)i*100003u)).ToArray();
        var loot=New("GauntletLootSnapshot");Set(loot,"Run",new NetworkInstanceId(123));Set(loot,"Seed",42UL);Set(loot,"Sequence",7UL);
        Set(loot,"Objects",lootIds);Set(loot,"Owners",lootIds.Reverse().ToArray());
        var lootCopy=Copy(loot);
        Check(((NetworkInstanceId[])Get(lootCopy,"Objects")).SequenceEqual(lootIds) && ((NetworkInstanceId[])Get(lootCopy,"Owners")).SequenceEqual(lootIds.Reverse()) && (ulong)Get(lootCopy,"Sequence")==7,"A full loot ownership list keeps every drop/owner pair through installed UNet");
        Check(Bytes(loot).Length<32768,"A full loot ownership list fits the host packet bound");
        var lootBytes=Bytes(loot);
        foreach(int cut in new[]{0,4,12,20,22,lootBytes.Length-1})
        {
            try{New("GauntletLootSnapshot").Deserialize(new NetworkReader(lootBytes.Take(cut).ToArray()));throw new Exception("Truncated loot ownership accepted");}
            catch(IndexOutOfRangeException){checks++;}catch(System.IO.EndOfStreamException){checks++;}
        }
        Set(loot,"Objects",lootIds.Append(new NetworkInstanceId(1)).ToArray());Set(loot,"Owners",lootIds.Append(new NetworkInstanceId(1)).ToArray());
        try{Copy(loot);throw new Exception("Oversized loot ownership accepted");}catch(FormatException){checks++;}
        var small=New("GauntletCommandSnapshot");Set(small,"RunId",new NetworkInstanceId(123));var payload=Bytes(small);
        for(int i=0;i<payload.Length;i++)Invalid(payload.Take(i).ToArray());
        // Exercise the production origin/size/decoding boundary using native
        // connection objects without starting a transport or a Unity scene.
        var client=(NetworkClient)RuntimeHelpers.GetUninitializedObject(typeof(NetworkClient));
        var host=(NetworkConnection)RuntimeHelpers.GetUninitializedObject(typeof(NetworkConnection));
        var stranger=(NetworkConnection)RuntimeHelpers.GetUninitializedObject(typeof(NetworkConnection));
        typeof(NetworkClient).GetField("m_Connection",Fields).SetValue(client,host);
        var boundary=Plugin.GetType("BazaarGauntlet.GauntletNetwork").GetMethod("Receive",BindingFlags.NonPublic|BindingFlags.Static).MakeGenericMethod(typeof(Probe));
        void Receive(NetworkConnection sender,byte[] data)=>boundary.Invoke(null,new object[]{client,new NetworkMessage{conn=sender,reader=new NetworkReader(data)}});
        Probe.Parsed=Probe.Received=0;
        Receive(stranger,new byte[]{1,0,0,0});Check(Probe.Parsed==0 && Probe.Received==0,"A non-host connection cannot parse or apply a snapshot");
        Receive(host,new byte[32769]);Check(Probe.Parsed==0,"An oversized host packet is rejected before parsing");
        Receive(host,new byte[]{1,0,0});Check(Probe.Received==0,"Truncated host packet is dropped without invoking application code");
        Probe.Parsed=Probe.Received=0;Receive(host,new byte[]{1,0,0,0});Check(Probe.Parsed==1 && Probe.Received==1,"Valid host data reaches the production client receiver exactly once");
        var active=typeof(NetworkServer).GetField("s_Active",BindingFlags.Static|BindingFlags.NonPublic);
        try{active.SetValue(null,true);Receive(host,new byte[]{1,0,0,0});Check(Probe.Received==1,"The server never applies an incoming client snapshot");}
        finally{active.SetValue(null,false);}
        Simulate();
        Console.WriteLine($"PASS: {checks} actual installed-UNet round-trip, packet-size, restart identity and malformed-count/truncation checks. No Unity runtime or game launched.");
    }
    public sealed class Probe:MessageBase,R2API.Networking.Interfaces.INetMessage
    {
        public static int Parsed,Received;
        public override void Deserialize(NetworkReader reader){Parsed++;reader.ReadInt32();}
        public void OnReceived(){Received++;}
    }
}
