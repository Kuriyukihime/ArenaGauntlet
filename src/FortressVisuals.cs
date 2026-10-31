using System;
using System.Collections.Generic;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using Object=UnityEngine.Object;

namespace BazaarGauntlet
{
    internal static class FortressVisuals
    {
        private static Material light;
        private static void Prepare()
        {
            if(light)return;
            var shader=FortressAssets.Load<Shader>("HGStandard_shader");
            if(!shader)throw new InvalidOperationException("The native castle effect shader is unavailable.");
            var color=new Color(.18f,.76f,1);
            var material=new Material(shader){name="Castle weapon glow",color=color};
            if(material.HasProperty("_MainTex"))material.SetTexture("_MainTex",Texture2D.whiteTexture);
            if(material.HasProperty("_EmTex"))material.SetTexture("_EmTex",Texture2D.whiteTexture);
            if(material.HasProperty("_EmColor"))material.SetColor("_EmColor",color);
            if(material.HasProperty("_EmPower"))material.SetFloat("_EmPower",1.6f);
            light=material;
        }
        internal static GameObject Build(FortressController fortress)=>FortressArchitecture.Build(fortress);
        internal static void Tracer(Vector3 from,Vector3 to)
        {
            Prepare(); var root=new GameObject("Fortress repeater tracer"); var line=root.AddComponent<LineRenderer>();
            root.transform.position=from;FortressAimingHead.Aim(from,to,false);
            string sound=EntityStates.EngiTurret.EngiTurretWeapon.FireGauss.attackSoundString;if(!string.IsNullOrEmpty(sound))Util.PlaySound(sound,root);
            line.sharedMaterial=light;line.generateLightingData=true; line.positionCount=2; line.SetPosition(0,from);line.SetPosition(1,to);line.startWidth=.1f;line.endWidth=.035f;
            Object.Destroy(line,.13f);Object.Destroy(root,2);
            if(NetworkServer.active)GauntletNetwork.Send(GauntletNetwork.EffectMessage,new FortressEffect{Run=GauntletController.Current.Run.netId,Seed=GauntletController.Current.RunKey,From=from,To=to});
        }
        internal static void Pulse(Vector3 point,float radius)
        {
            Prepare();FortressAimingHead.Aim(point,point,true);
            var explosion=FortressAssets.Load<GameObject>("OmniExplosionVFX_prefab");
            if(explosion)EffectManager.SpawnEffect(explosion,new EffectData{origin=point,scale=radius},false);
            if(NetworkServer.active)GauntletNetwork.Send(GauntletNetwork.EffectMessage,new FortressEffect{Run=GauntletController.Current.Run.netId,Seed=GauntletController.Current.RunKey,From=point,To=point,Radius=radius});
        }
    }
    internal sealed class FortressMeshLifetime:MonoBehaviour
    {
        internal readonly List<Mesh> Meshes=new List<Mesh>();
        private void OnDestroy(){foreach(var mesh in Meshes) if(mesh) Destroy(mesh);}
    }
    internal sealed class FortressAimingHead:MonoBehaviour
    {
        private static readonly List<FortressAimingHead> heads=new List<FortressAimingHead>();
        internal int Module;private Quaternion desired;private Vector3 anchor;private float recoil;
        private void Start(){anchor=transform.localPosition;desired=transform.rotation;heads.Add(this);}
        internal static void Aim(Vector3 from,Vector3 to,bool mortar)
        {
            FortressAimingHead nearest=null;float range=25;
            foreach(var head in heads)
            {
                if(!head || mortar!=(head.Module==7))continue;
                float distance=mortar?0:(head.transform.position-from).sqrMagnitude;
                if(distance<range){range=distance;nearest=head;}
            }
            if(!nearest)return;
            var direction=to-nearest.transform.position;direction.y=0;
            if(direction.sqrMagnitude>.01f)nearest.desired=Quaternion.LookRotation(direction.normalized,Vector3.up);
            nearest.recoil=.18f;
        }
        private void Update()
        {
            transform.rotation=Quaternion.Slerp(transform.rotation,desired,Time.deltaTime*20);
            recoil=Mathf.MoveTowards(recoil,0,Time.deltaTime*1.2f);
            transform.localPosition=anchor-transform.parent.InverseTransformDirection(transform.forward)*recoil;
        }
        private void OnDestroy(){heads.Remove(this);}
    }
    public sealed class FortressEffect:MessageBase,INetMessage
    {
        internal NetworkInstanceId Run;internal ulong Seed;internal Vector3 From,To;internal float Radius;
        public override void Serialize(NetworkWriter writer){writer.Write(Run);writer.Write(Seed);writer.Write(From);writer.Write(To);writer.Write(Radius);}
        public override void Deserialize(NetworkReader reader){Run=reader.ReadNetworkId();Seed=reader.ReadUInt64();From=reader.ReadVector3();To=reader.ReadVector3();Radius=reader.ReadSingle();}
        public void OnReceived()
        {
            var owner=GauntletController.Current;if(NetworkServer.active || !owner || owner.Run.netId!=Run || owner.RunKey!=Seed || float.IsNaN(Radius) || Radius<0 || Radius>20 ||
                !GauntletNetworkPolicy.Finite(From.x) || !GauntletNetworkPolicy.Finite(From.y) || !GauntletNetworkPolicy.Finite(From.z) ||
                !GauntletNetworkPolicy.Finite(To.x) || !GauntletNetworkPolicy.Finite(To.y) || !GauntletNetworkPolicy.Finite(To.z))return;
            if(Radius>0)FortressVisuals.Pulse(From,Radius);else FortressVisuals.Tracer(From,To);
        }
    }
}
