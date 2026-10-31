using System;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    [Serializable]
    internal sealed class GauntletHazardState
    {
        public int Sequence;
        public float Remaining;
        public Vector3[] Centers=Array.Empty<Vector3>();
    }
    internal sealed class GauntletAscension:MonoBehaviour
    {
        internal const short MessageId=18473;
        private GauntletController owner;
        private GauntletHazardState state=new GauntletHazardState();
        private GameObject warning;
        private Material material;
        private float nextStrike,detonate,nextSend,localExpires;
        private int visualSequence=-1;
        internal static void Apply(GauntletController owner,CharacterMaster master,bool boss)
        {
            if(!NetworkServer.active || !owner || !master || !master.inventory)return;
            int rank=owner.Meta.Ascension;
            int hp=GauntletMetaPolicy.HealthStacks(rank,boss),damage=GauntletMetaPolicy.DamageStacks(rank);
            if(hp>0)master.inventory.GiveItemPermanent(RoR2Content.Items.BoostHp,hp);
            if(damage>0)master.inventory.GiveItemPermanent(RoR2Content.Items.BoostDamage,damage);
        }
        internal static void Install()=>RoR2.Networking.NetworkManagerSystem.onStartClientGlobal+=RegisterClient;
        private static void RegisterClient(NetworkClient client)
        {
            if(client.handlers.TryGetValue(MessageId,out var handler) && !(handler.Target is Receiver receiver && receiver.Client==client))
                throw new InvalidOperationException("Arena hazard ID collision.");
            client.RegisterHandler(MessageId,new Receiver{Client=client}.Read);
        }
        private sealed class Receiver
        {
            internal NetworkClient Client;
            internal void Read(NetworkMessage message)
            {
                if(NetworkServer.active || message?.conn==null || message.conn!=Client.connection || message.reader==null || message.reader.Length>4096)return;
                try
                {
                    var packet=message.ReadMessage<GauntletMetaPacket>();var owner=GauntletController.Current;
                    if(!owner || !GauntletNetworkPolicy.MatchesRun(owner.Run.netId.Value,owner.RunKey,packet.Scope.Value,packet.Seed))return;
                    var received=JsonUtility.FromJson<GauntletHazardState>(packet.Json);
                    if(received==null || received.Sequence<owner.Ascension.state.Sequence || received.Centers==null || received.Centers.Length>3 ||
                        !GauntletNetworkPolicy.Finite(received.Remaining) || received.Remaining<0 || received.Remaining>3 ||
                        received.Centers.Any(p=>!GauntletNetworkPolicy.Finite(p.x) || !GauntletNetworkPolicy.Finite(p.y) || !GauntletNetworkPolicy.Finite(p.z) || (p-FortressBattlefield.Origin).sqrMagnitude>250*250))return;
                    owner.Ascension.state=received;owner.Ascension.localExpires=Time.unscaledTime+received.Remaining;
                }
                catch(Exception e) when(e is ArgumentException || e is System.IO.IOException || e is FormatException || e is IndexOutOfRangeException || e is OverflowException){}
            }
        }
        private void Awake(){owner=GetComponent<GauntletController>();}
        private void FixedUpdate()
        {
            if(!NetworkServer.active || !owner)return;
            bool combat=owner.Fortress.IsCombat && !owner.ShuttingDown && !owner.Run.isGameOverServer;
            int rank=owner.Meta.Ascension;
            if(!combat || rank<2)
            {nextStrike=Time.fixedTime+12;if(state.Centers.Length>0){state=new GauntletHazardState{Sequence=state.Sequence+1};Broadcast();}return;}
            if(state.Centers.Length>0)
            {
                if(Time.fixedTime>=detonate)
                {
                    foreach(var center in state.Centers)
                    {
                        new BlastAttack{position=center,radius=7,baseDamage=20*(1+.2f*(owner.Run.ambientLevelFloor-1)),baseForce=800,
                            teamIndex=TeamIndex.Monster,procCoefficient=0,falloffModel=BlastAttack.FalloffModel.None,
                            attackerFiltering=AttackerFiltering.NeverHitSelf,damageColorIndex=DamageColorIndex.Item}.Fire();
                        var effect=FortressAssets.Load<GameObject>("LightningStrikeImpact_prefab");if(effect)EffectManager.SpawnEffect(effect,new EffectData{origin=center,scale=7},true);
                    }
                    state=new GauntletHazardState{Sequence=state.Sequence+1};Broadcast();
                }
                else if(Time.unscaledTime>=nextSend){nextSend=Time.unscaledTime+.25f;Broadcast();}
                return;
            }
            if(Time.fixedTime<nextStrike)return;
            var targets=PlayerCharacterMasterController.instances.Where(p=>p && p.isConnected && p.master).Select(p=>p.master.GetBody())
                .Where(b=>b && b.healthComponent && b.healthComponent.alive).ToArray();
            if(targets.Length==0)return;
            int count=rank>=6?3:rank>=4?2:1;
            state=new GauntletHazardState{Sequence=state.Sequence+1,Remaining=2.5f,Centers=Enumerable.Range(0,count).Select(i=>
            {
                var target=targets[(i+state.Sequence)%targets.Length];var p=target.footPosition;
                if(i>=targets.Length)p+=new Vector3((i%2==0?1:-1)*10,0,0);
                p.y=FortressBattlefield.Origin.y+.25f;return p;
            }).ToArray()};
            detonate=Time.fixedTime+2.5f;nextStrike=Time.fixedTime+(rank>=6?9:rank>=4?12:rank>=3?15:18);Broadcast();
        }
        private void Broadcast()
        {
            state.Remaining=state.Centers.Length>0?Mathf.Clamp(detonate-Time.fixedTime,0,2.5f):0;localExpires=Time.unscaledTime+state.Remaining;
            NetworkServer.SendToAll(MessageId,new GauntletMetaPacket{Scope=owner.Run.netId,Seed=owner.RunKey,Json=JsonUtility.ToJson(state)});
        }
        private void Update()
        {
            if(Application.isBatchMode)return;
            if(visualSequence!=state.Sequence)
            {
                visualSequence=state.Sequence;if(warning)Destroy(warning);warning=null;
                if(state.Centers.Length>0)
                {
                    warning=new GameObject("Arena strike telegraphs");
                    if(!material)
                    {
                        material=new Material(FortressArchitecture.Surface("energy")){color=new Color(1,.3f,.08f)};
                        if(material.HasProperty("_EmColor"))material.SetColor("_EmColor",new Color(1,.3f,.08f));
                    }
                    foreach(var center in state.Centers)
                    {
                        var go=new GameObject("Move out of the marked circle");go.transform.SetParent(warning.transform,false);
                        var ring=go.AddComponent<LineRenderer>();ring.sharedMaterial=material;ring.useWorldSpace=true;ring.loop=true;
                        ring.startWidth=ring.endWidth=.35f;ring.positionCount=64;ring.startColor=ring.endColor=new Color(1,.3f,.08f);
                        for(int i=0;i<64;i++){float a=i*Mathf.PI*2/64;ring.SetPosition(i,center+new Vector3(Mathf.Cos(a)*7,0,Mathf.Sin(a)*7));}
                    }
                }
            }
            if(warning)warning.SetActive(Time.unscaledTime<=localExpires && owner && !owner.ShuttingDown && owner.Fortress.IsCombat);
        }
        private void OnDestroy(){if(warning)Destroy(warning);if(material)Destroy(material);}
    }
}
