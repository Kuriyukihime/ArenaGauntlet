using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal sealed class GauntletLootSnapshot:MessageBase
    {
        // shortcut: drops beyond this many live at once stay visible to everyone (the host still refuses non-owners); raise it if parties ever hoard more.
        internal const int MaxEntries=256;
        internal NetworkInstanceId Run;internal ulong Seed,Sequence;
        internal NetworkInstanceId[] Objects=Array.Empty<NetworkInstanceId>(),Owners=Array.Empty<NetworkInstanceId>();
        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(Run);writer.Write(Seed);writer.Write(Sequence);writer.WritePackedUInt32((uint)Objects.Length);
            for(int i=0;i<Objects.Length;i++){writer.Write(Objects[i]);writer.Write(Owners[i]);}
        }
        public override void Deserialize(NetworkReader reader)
        {
            Run=reader.ReadNetworkId();Seed=reader.ReadUInt64();Sequence=reader.ReadUInt64();
            uint count=reader.ReadPackedUInt32();if(count>MaxEntries)throw new FormatException("Oversized loot ownership packet.");
            Objects=new NetworkInstanceId[count];Owners=new NetworkInstanceId[count];
            for(int i=0;i<count;i++){Objects[i]=reader.ReadNetworkId();Owners[i]=reader.ReadNetworkId();}
        }
    }
    // Each free arena drop belongs to one player. The host only grants it to
    // that player and every peer hides drops that belong to someone else.
    // Drops of a departed player become shared again.
    internal sealed class GauntletLootInstancing:MonoBehaviour
    {
        internal const short MessageId=18474;
        // Droplet/pickup network id -> owning player master network id.
        private readonly Dictionary<NetworkInstanceId,NetworkInstanceId> owners=new Dictionary<NetworkInstanceId,NetworkInstanceId>();
        private readonly List<Renderer> renderers=new List<Renderer>();
        private readonly List<Light> lights=new List<Light>();
        private HashSet<Component> hidden=new HashSet<Component>(),stillHidden=new HashSet<Component>();
        private GauntletController owner;
        private ulong sequence;private bool dirty;private float nextSend;private int nextPlayer;
        private static GauntletLootInstancing Active{get{var current=GauntletController.Current;return current?current.Loot:null;}}
        internal static void Install()
        {
            RoR2.Networking.NetworkManagerSystem.onStartClientGlobal+=RegisterClient;
            On.RoR2.PickupDropletController.CreatePickup+=Land;
            On.RoR2.GenericPickupController.AttemptGrant+=Grant;
            On.RoR2.GenericPickupController.GetInteractability+=Interactable;
            On.RoR2.PickupPickerController.GetInteractability+=PickerInteractable;
            On.RoR2.PickupPickerController.OnInteractionBegin+=PickerBegin;
        }
        private static void RegisterClient(NetworkClient client)
        {
            if(client.handlers.TryGetValue(MessageId,out var handler) && !(handler.Target is Receiver receiver && receiver.Client==client))
                throw new InvalidOperationException("Arena loot ownership ID collision.");
            client.RegisterHandler(MessageId,new Receiver{Client=client}.Read);
        }
        private sealed class Receiver
        {
            internal NetworkClient Client;
            internal void Read(NetworkMessage message)
            {
                if(NetworkServer.active || message?.conn==null || message.conn!=Client.connection || message.reader==null || message.reader.Length>GauntletNetworkPolicy.MaxPacketBytes)return;
                try
                {
                    var packet=message.ReadMessage<GauntletLootSnapshot>();var loot=Active;
                    if(!loot || !GauntletNetworkPolicy.MatchesRun(loot.owner.Run.netId.Value,loot.owner.RunKey,packet.Run.Value,packet.Seed) || packet.Sequence<=loot.sequence)return;
                    loot.sequence=packet.Sequence;loot.owners.Clear();
                    for(int i=0;i<packet.Objects.Length;i++)loot.owners[packet.Objects[i]]=packet.Owners[i];
                }
                catch(Exception e) when(e is ArgumentException || e is System.IO.IOException || e is FormatException || e is IndexOutOfRangeException || e is OverflowException){}
            }
        }
        private void Awake(){owner=GetComponent<GauntletController>();}
        internal CharacterMaster NextPlayer()
        {
            var players=PlayerCharacterMasterController.instances.Where(p=>p && p.isConnected && p.master).ToArray();
            return players.Length==0?null:players[nextPlayer++%players.Length].master;
        }
        internal void Drop(UniquePickup pickup,Vector3 position,CharacterMaster player)=>
            Claim(player?player.netId:NetworkInstanceId.Invalid,()=>PickupDropletController.CreatePickupDroplet(pickup,position,Vector3.up*16,false));
        internal NetworkInstanceId OwnerOf(NetworkBehaviour loot)=>owners.TryGetValue(loot.netId,out var player)?player:NetworkInstanceId.Invalid;
        // Native creation returns nothing for droplets or choice pickups, so the
        // new loot objects are found by comparing the native instance lists.
        internal void Claim(NetworkInstanceId player,Action create)
        {
            if(!NetworkServer.active || player==NetworkInstanceId.Invalid){create();return;}
            var known=new HashSet<GameObject>(Loot());
            create();
            foreach(var go in Loot())
            {
                var identity=go.GetComponent<NetworkIdentity>();
                if(known.Contains(go) || !identity || identity.netId.IsEmpty())continue;
                owners[identity.netId]=player;dirty=true;
            }
        }
        private static IEnumerable<GameObject> Loot()=>
            InstanceTracker.GetInstancesList<PickupDropletController>().Where(x=>x).Select(x=>x.gameObject)
            .Concat(InstanceTracker.GetInstancesList<GenericPickupController>().Where(x=>x).Select(x=>x.gameObject))
            .Concat(InstanceTracker.GetInstancesList<PickupPickerController>().Where(x=>x).Select(x=>x.gameObject));
        private static bool Allowed(NetworkBehaviour loot,CharacterMaster master)
        {
            var active=Active;
            return !active || !active.owners.TryGetValue(loot.netId,out var player) || master && master.netId==player;
        }
        private static CharacterMaster Master(Interactor activator)
        {var body=activator?activator.GetComponent<CharacterBody>():null;return body?body.master:null;}
        private static void Land(On.RoR2.PickupDropletController.orig_CreatePickup original,PickupDropletController droplet)
        {
            var active=Active;
            if(active && NetworkServer.active && active.owners.TryGetValue(droplet.netId,out var player))active.Claim(player,()=>original(droplet));
            else original(droplet);
        }
        private static void Grant(On.RoR2.GenericPickupController.orig_AttemptGrant original,GenericPickupController self,CharacterBody body)
        {if(Allowed(self,body?body.master:null))original(self,body);}
        private static Interactability Interactable(On.RoR2.GenericPickupController.orig_GetInteractability original,GenericPickupController self,Interactor activator)
            =>Allowed(self,Master(activator))?original(self,activator):Interactability.Disabled;
        private static Interactability PickerInteractable(On.RoR2.PickupPickerController.orig_GetInteractability original,PickupPickerController self,Interactor activator)
            =>Allowed(self,Master(activator))?original(self,activator):Interactability.Disabled;
        private static void PickerBegin(On.RoR2.PickupPickerController.orig_OnInteractionBegin original,PickupPickerController self,Interactor activator)
        {if(Allowed(self,Master(activator)))original(self,activator);}
        private void LateUpdate()
        {
            if(NetworkServer.active)Publish();
            if(!Application.isBatchMode)Hide();
        }
        private void Publish()
        {
            bool heartbeat=Time.unscaledTime>=nextSend;
            if(heartbeat)
            {
                // Once a second: forget collected loot, release a departed
                // player's drops and resend the list for late joiners.
                nextSend=Time.unscaledTime+1;
                var connected=new HashSet<NetworkInstanceId>(PlayerCharacterMasterController.instances.Where(p=>p && p.isConnected && p.master).Select(p=>p.master.netId));
                foreach(var key in owners.Where(pair=>!Util.FindNetworkObject(pair.Key) || !connected.Contains(pair.Value)).Select(pair=>pair.Key).ToArray())
                {owners.Remove(key);dirty=true;}
            }
            if(!dirty && !(heartbeat && owners.Count>0) || !owner || owner.Run.netId.IsEmpty())return;
            dirty=false;
            var entries=owners.Take(GauntletLootSnapshot.MaxEntries).ToArray();
            NetworkServer.SendToAll(MessageId,new GauntletLootSnapshot{Run=owner.Run.netId,Seed=owner.RunKey,Sequence=++sequence,
                Objects=entries.Select(pair=>pair.Key).ToArray(),Owners=entries.Select(pair=>pair.Value).ToArray()});
        }
        private void Hide()
        {
            // Split-screen players share one view, so any local owner reveals it.
            foreach(var pair in owners)
            {
                if(LocalUserManager.readOnlyLocalUsersList.Any(user=>user.cachedMaster && user.cachedMaster.netId==pair.Value))continue;
                var go=Util.FindNetworkObject(pair.Key);if(!go)continue;
                go.GetComponentsInChildren(true,renderers);go.GetComponentsInChildren(true,lights);
                // Only switch off what is lit now or already ours, so native
                // presentation keeps its own disabled parts when revealed.
                foreach(var item in renderers)if(item.enabled || hidden.Contains(item)){item.enabled=false;stillHidden.Add(item);}
                foreach(var item in lights)if(item.enabled || hidden.Contains(item)){item.enabled=false;stillHidden.Add(item);}
            }
            foreach(var item in hidden)
            {
                if(!item || stillHidden.Contains(item))continue;
                if(item is Renderer renderer)renderer.enabled=true;else if(item is Light light)light.enabled=true;
            }
            (hidden,stillHidden)=(stillHidden,hidden);stillHidden.Clear();
        }
    }
}
