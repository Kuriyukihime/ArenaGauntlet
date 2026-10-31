using System;
using System.IO;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2.Networking;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal static class GauntletNetwork
    {
        internal const short StateMessage=18468,CompanyMessage=18469,EffectMessage=18470;
        private sealed class Receiver
        {
            internal readonly NetworkClient Client;
            internal Receiver(NetworkClient client){Client=client;}
            internal void State(NetworkMessage packet)=>Receive<GauntletSnapshot>(Client,packet);
            internal void Companies(NetworkMessage packet)=>Receive<GauntletCommandSnapshot>(Client,packet);
            internal void Effect(NetworkMessage packet)=>Receive<FortressEffect>(Client,packet);
        }
        internal static void Install()=>NetworkManagerSystem.onStartClientGlobal+=RegisterClient;
        private static void RegisterClient(NetworkClient client)
        {
            foreach(short id in new[]{StateMessage,CompanyMessage,EffectMessage})
                if(client.handlers.TryGetValue(id,out var handler) && !(handler.Target is Receiver receiver && receiver.Client==client))
                    throw new InvalidOperationException("ArenaGauntlet message ID is already registered: "+id);
            var target=new Receiver(client);
            client.RegisterHandler(StateMessage,target.State);
            client.RegisterHandler(CompanyMessage,target.Companies);
            client.RegisterHandler(EffectMessage,target.Effect);
        }
        private static void Receive<T>(NetworkClient client,NetworkMessage packet) where T:MessageBase,INetMessage,new()
        {
            // These handlers exist on clients only. Unlike the generic R2API
            // relay, the server has no handler that forwards a client's snapshot.
            if(NetworkServer.active || packet?.conn==null || packet.conn!=client.connection || packet.reader==null ||
                packet.reader.Length>GauntletNetworkPolicy.MaxPacketBytes)return;
            T message;
            try{message=packet.ReadMessage<T>();}
            catch(Exception error) when(error is IOException || error is IndexOutOfRangeException || error is ArgumentException || error is OverflowException || error is FormatException)
            {return;}
            message.OnReceived();
        }
        internal static void Send(short id,MessageBase message)
        {if(NetworkServer.active)NetworkServer.SendToAll(id,message);}
        internal static void Broadcast(GauntletController owner)
        {
            if (!NetworkServer.active || !owner || owner.Run.netId == NetworkInstanceId.Invalid) return;
            Send(StateMessage,new GauntletSnapshot
            {
                RunId = owner.Run.netId, Seed = owner.RunKey,
                Sequence = ++owner.SentSnapshot, ArenaRadius = owner.ArenaRadius,
                BossId = owner.BossMaster ? owner.BossMaster.netId : NetworkInstanceId.Invalid,
                EnemyLevel = owner.Run.ambientLevelFloor, Round = owner.Flow.Round,
                Cycle = owner.Flow.Cycle, Phase = (byte)owner.Flow.Phase,
                MinimumScaling = owner.MinimumScaling, Spawned = owner.Spawned, Defeated = owner.Defeated, Quota = owner.SpawnQuota,
                // Clients drop a snapshot whose theme exceeds MaxTheme; a long modded boss name must not freeze their HUD.
                Theme = owner.ThemeName != null && owner.ThemeName.Length > GauntletNetworkPolicy.MaxTheme ? owner.ThemeName.Substring(0, GauntletNetworkPolicy.MaxTheme) : owner.ThemeName,
                Fortress = UnityEngine.JsonUtility.ToJson(owner.Fortress.State), AllowLoop=owner.AllowLoop
            });
        }
    }
    public sealed class GauntletSnapshot : MessageBase,INetMessage
    {
        internal NetworkInstanceId RunId;
        internal NetworkInstanceId BossId;
        internal ulong Seed;
        internal ulong Sequence;
        internal int EnemyLevel, Round, Cycle;
        internal byte Phase;
        internal float MinimumScaling;
        internal float ArenaRadius;
        internal int Spawned, Quota, Defeated;
        internal string Theme;
        internal string Fortress;
        internal bool AllowLoop;
        // Every state a host can legitimately send must pass, or the client's HUD freezes.
        internal bool Plausible() => EnemyLevel >= 1 && Round >= 1 && Round <= GauntletPlan.RoundsPerCycle && Cycle >= 1 && Phase <= (byte)GauntletPhase.Lost &&
            !float.IsNaN(MinimumScaling) && MinimumScaling >= 0 && MinimumScaling <= 10 &&
            !float.IsNaN(ArenaRadius) && ArenaRadius >= 30 && ArenaRadius <= 160 && Spawned >= 0 && Spawned <= Quota && Quota >= 0 && Quota <= 500 &&
            Defeated >= 0 && Defeated <= Spawned && Theme != null && Theme.Length <= GauntletNetworkPolicy.MaxTheme;
        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(RunId); writer.Write(Seed);writer.Write(Sequence);writer.Write(ArenaRadius); writer.Write(EnemyLevel);
            writer.Write(BossId);
            writer.Write(Round); writer.Write(Cycle); writer.Write(Phase);
            writer.Write(MinimumScaling);writer.Write(Spawned);writer.Write(Defeated);writer.Write(Quota);writer.Write(Theme ?? "");
            writer.Write(Fortress??"");writer.Write(AllowLoop);
        }
        public override void Deserialize(NetworkReader reader)
        {
            RunId = reader.ReadNetworkId(); Seed = reader.ReadUInt64();Sequence = reader.ReadUInt64();ArenaRadius = reader.ReadSingle(); EnemyLevel = reader.ReadInt32();
            BossId = reader.ReadNetworkId();
            Round = reader.ReadInt32(); Cycle = reader.ReadInt32(); Phase = reader.ReadByte();
            MinimumScaling = reader.ReadSingle();Spawned = reader.ReadInt32();Defeated = reader.ReadInt32();Quota = reader.ReadInt32();Theme = reader.ReadString();
            Fortress=reader.ReadString();AllowLoop=reader.ReadBoolean();
        }
        public void OnReceived()
        {
            if (NetworkServer.active) return;
            var owner = GauntletController.Current;
            if (!owner || owner.Run.netId != RunId || owner.RunKey != Seed || Sequence <= owner.ReceivedSnapshot || !Plausible()) return;
            owner.ReceivedSnapshot = Sequence;owner.ArenaRadius = ArenaRadius;
            var boss = ClientScene.FindLocalObject(BossId);
            owner.BossMaster = boss ? boss.GetComponent<RoR2.CharacterMaster>() : null;
            owner.RemoteEnemyLevel = EnemyLevel; owner.RemoteRound = Round;
            owner.RemoteCycle = Cycle; owner.RemotePhase = (GauntletPhase)Phase;
            owner.MinimumScaling = MinimumScaling;owner.Spawned = Spawned;owner.Defeated = Defeated;owner.SpawnQuota = Quota;owner.ThemeName = Theme;owner.AllowLoop=AllowLoop;
            FortressNetwork.Apply(owner.Fortress,Fortress);
            // A late-joining client's SceneInfo callback may precede its run
            // object. The next host snapshot must also be able to build the map.
            FortressBattlefield.Ensure(owner);
            owner.UpdateLevelFloor();
            var ward = NativeAccess.Get<RoR2.InfiniteTowerSafeWardController>(owner.Run, "safeWardController");
            if (ward)
                foreach (var machine in ward.GetComponents<RoR2.EntityStateMachine>())
                {
                    if (machine.state is EntityStates.InfiniteTowerSafeWard.Active active) active.radius = ArenaRadius;
                    if (machine.state is EntityStates.InfiniteTowerSafeWard.AwaitingActivation waiting) waiting.radius = ArenaRadius;
                }
        }
    }
}
