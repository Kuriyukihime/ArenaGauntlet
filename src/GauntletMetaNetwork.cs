using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal sealed class GauntletMetaPacket:MessageBase
    {
        internal NetworkInstanceId Scope,User;internal ulong Seed;internal string Json;
        public override void Serialize(NetworkWriter writer){writer.Write(Scope);writer.Write(User);writer.Write(Seed);writer.Write(Json??"");}
        public override void Deserialize(NetworkReader reader){Scope=reader.ReadNetworkId();User=reader.ReadNetworkId();Seed=reader.ReadUInt64();Json=reader.ReadString();if(Json.Length>4096)throw new FormatException("Oversized career packet.");}
    }
    internal static class GauntletMetaNetwork
    {
        internal const short RequestId=18471,StatusId=18472;
        internal static readonly Dictionary<NetworkUserId,GauntletCareer> Lobby=new Dictionary<NetworkUserId,GauntletCareer>();
        private static readonly Dictionary<NetworkConnection,GauntletRequestBudget> budgets=new Dictionary<NetworkConnection,GauntletRequestBudget>();
        private static uint lobbyId;
        internal static int LobbyAscension,RemoteAscension;
        private static float nextSubmit;
        private sealed class Receiver
        {
            internal NetworkClient Client;
            internal void Read(NetworkMessage packet)
            {
                if(NetworkServer.active || packet?.conn==null || packet.conn!=Client.connection || packet.reader==null || packet.reader.Length>8192)return;
                try{Apply(packet.ReadMessage<GauntletMetaPacket>());}catch(Exception e) when(Malformed(e)){}
            }
        }
        private static bool Malformed(Exception e)=>e is IOException || e is ArgumentException || e is IndexOutOfRangeException || e is FormatException || e is OverflowException;
        internal static bool Selecting=>!Run.instance && PreGameController.instance &&
            GauntletMode.IsActive(GameModeCatalog.GetGameModePrefabComponent(PreGameController.instance.gameModeIndex));
        internal static bool IsHost(LocalUser local)=>NetworkServer.active && local!=null && local==LocalUserManager.GetFirstLocalUser();
        internal static void Install()
        {
            NetworkManagerSystem.onStartServerGlobal+=RegisterServer;
            NetworkManagerSystem.onStartClientGlobal+=client=>
            {
                if(client.handlers.TryGetValue(StatusId,out var handler) && !(handler.Target is Receiver receiver && receiver.Client==client))
                    throw new InvalidOperationException("Arena career status ID collision.");
                client.RegisterHandler(StatusId,new Receiver{Client=client}.Read);
            };
        }
        private static void RegisterServer()
        {
            if(NetworkServer.handlers.TryGetValue(RequestId,out var handler) && handler!=(NetworkMessageDelegate)ReadRequest)
                throw new InvalidOperationException("Arena career request ID collision.");
            NetworkServer.RegisterHandler(RequestId,ReadRequest);Lobby.Clear();budgets.Clear();lobbyId=0;LobbyAscension=0;
        }
        internal static void Tick()
        {
            if(Application.isBatchMode || Time.unscaledTime<nextSubmit)return;nextSubmit=Time.unscaledTime+1;
            if(!Selecting && !GauntletController.Current)return;
            foreach(var user in LocalUserManager.readOnlyLocalUsersList)Submit(user);
        }
        internal static void Submit(LocalUser local)
        {
            var user=local?.currentNetworkUser;var c=GauntletMetaStore.For(local);
            if(!user || c==null || (!Selecting && !GauntletController.Current))return;
            var owner=GauntletController.Current;
            var packet=new GauntletMetaPacket{User=user.netId,Scope=owner?owner.Run.netId:PreGameController.instance.netId,Seed=owner?owner.RunKey:0};
            // Receipts stay local. Only the career ranks, earned budget and chosen names are submitted.
            // Cosmetics stay local too: the host shows its own Treasury choices to everyone.
            var claim=JsonUtility.FromJson<GauntletCareer>(JsonUtility.ToJson(c));claim.Receipts=Array.Empty<string>();claim.Received=Array.Empty<int>();
            claim.Cosmetics=Array.Empty<string>();claim.Decor=Array.Empty<string>();claim.Skin="";packet.Json=JsonUtility.ToJson(claim);
            if(NetworkServer.active)
            {
                Accept(user,packet);
                if(Selecting && IsHost(local)){LobbyAscension=c.Ascension;RemoteAscension=LobbyAscension;}
            }
            else ClientScene.readyConnection?.Send(RequestId,packet);
        }
        private static void ReadRequest(NetworkMessage message)
        {
            if(!NetworkServer.active || message?.conn==null || !message.conn.isReady || message.reader==null || message.reader.Length>8192)return;
            foreach(var gone in budgets.Keys.Where(c=>!NetworkServer.connections.Contains(c)).ToArray())budgets.Remove(gone);
            if(!budgets.TryGetValue(message.conn,out var budget))budgets[message.conn]=budget=new GauntletRequestBudget();
            if(!budget.Take(Time.unscaledTime))return;
            try
            {
                var packet=message.ReadMessage<GauntletMetaPacket>();
                var user=NetworkUser.readOnlyInstancesList.FirstOrDefault(u=>u && u.netId==packet.User && u.connectionToClient==message.conn);
                if(user)Accept(user,packet);
            }
            catch(Exception e) when(Malformed(e)){}
        }
        private static void Accept(NetworkUser user,GauntletMetaPacket packet)
        {
            if(!NetworkServer.active || !user || !user.isParticipating || packet.Json==null || packet.Json.Length>4096)return;
            var c=GauntletMetaPolicy.Normalize(JsonUtility.FromJson<GauntletCareer>(packet.Json));if(!GauntletMetaPolicy.Valid(c))return;
            var owner=GauntletController.Current;
            if(owner)
            {
                if(!GauntletNetworkPolicy.MatchesRun(owner.Run.netId.Value,owner.RunKey,packet.Scope.Value,packet.Seed) || owner.ShuttingDown || owner.Run.isGameOverServer)return;
                owner.Meta.Capture(user,c);return;
            }
            if(!Selecting || packet.Scope!=PreGameController.instance.netId || packet.Seed!=0)return;
            if(lobbyId!=packet.Scope.Value){Lobby.Clear();lobbyId=packet.Scope.Value;LobbyAscension=0;}
            Lobby[user.id]=c;
            var reply=new GauntletMetaPacket{Scope=packet.Scope,User=user.netId,Json=JsonUtility.ToJson(new GauntletMetaStatus{Ascension=LobbyAscension,Clear=-1})};
            if(user.localUser!=null)Apply(reply);else user.connectionToClient?.Send(StatusId,reply);
        }
        internal static void Status(NetworkUser user,GauntletMetaStatus state,NetworkInstanceId scope,ulong seed)
        {
            var packet=new GauntletMetaPacket{User=user.netId,Scope=scope,Seed=seed,Json=JsonUtility.ToJson(state)};
            if(user.localUser!=null)Apply(packet);else user.connectionToClient?.Send(StatusId,packet);
        }
        private static void Apply(GauntletMetaPacket packet)
        {
            var local=LocalUserManager.readOnlyLocalUsersList.FirstOrDefault(u=>u.currentNetworkUser && u.currentNetworkUser.netId==packet.User);
            if(local==null)return;
            var state=JsonUtility.FromJson<GauntletMetaStatus>(packet.Json);
            if(state==null || state.Ascension<0 || state.Ascension>6)return;
            var owner=GauntletController.Current;
            if(!owner)
            {if(Selecting && PreGameController.instance.netId==packet.Scope && packet.Seed==0)RemoteAscension=state.Ascension;return;}
            if(!GauntletNetworkPolicy.MatchesRun(owner.Run.netId.Value,owner.RunKey,packet.Scope.Value,packet.Seed) || state.User!=packet.User.Value ||
                state.Capacity<1 || state.Capacity>4 || state.Patronage<0 || state.Patronage>2 || state.Glory<0 || state.Glory>100000 || state.Clear< -1 || state.Clear>6 ||
                string.IsNullOrEmpty(state.Receipt) || state.Receipt.Length>96)return;
            owner.Meta.Ascension=state.Ascension;owner.Meta.Remote[state.Player]=state;
            // Unknown ids are ignored by the catalog, so only the shape is checked here.
            if(state.Skin!=null && state.Skin.Length<=24 && state.Decor!=null && state.Decor.Length<=GauntletMetaPolicy.MaxCosmetics && state.Decor.All(GauntletMetaPolicy.ShortId))
                owner.Meta.Show(state.Skin,state.Decor);
            var c=GauntletMetaStore.For(local);
            if(c!=null)
            {
                int old=Array.IndexOf(c.Receipts??Array.Empty<string>(),state.Receipt);
                bool changed=old<0 || c.Received[old]<state.Glory || c.HighestClear<state.Clear;
                bool banked=changed && GauntletMetaPolicy.Receipt(c,state.Receipt,state.Glory,state.Clear);
                // Labors pay once per career; repeats in later status packets change nothing.
                foreach(string labor in (state.Labors??Array.Empty<string>()).Take(GauntletMetaPolicy.Labors.Length))banked|=GauntletMetaPolicy.CompleteLabor(c,labor);
                if(banked)GauntletMetaStore.Save(local);
            }
        }
    }
}
