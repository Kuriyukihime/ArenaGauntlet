using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace BazaarGauntlet
{
    internal sealed class GauntletShopVisuals:MonoBehaviour
    {
        private readonly List<Transform> shutters=new List<Transform>();
        private readonly List<Light> lights=new List<Light>();
        private float lift;
        internal static void Build(GameObject root)
        {
            var visuals=root.AddComponent<GauntletShopVisuals>();
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
                if(renderer.name.StartsWith("MerchantShutters",System.StringComparison.Ordinal))visuals.shutters.Add(renderer.transform);
            // Only one shutter batch needs collision. Both stone surfaces move
            // together, on every client, from authoritative round state.
            if(visuals.shutters.Count>0)
                for(int i=0;i<5;i++)
                {
                    var collider=new GameObject("Merchant shutter collision",typeof(BoxCollider));collider.layer=LayerIndex.world.intVal;
                    collider.transform.SetParent(visuals.shutters[0],false);collider.transform.localPosition=new Vector3(-24+i*12,3.2f,-174.7f);
                    collider.GetComponent<BoxCollider>().size=new Vector3(9,6.3f,.25f);
                }
            Color[] colors={new Color(.94f,.78f,.42f),new Color(.42f,.72f,1),new Color(1,.65f,.3f),new Color(.6f,1,.7f),new Color(.95f,.55f,.65f)};
            for(int i=0;i<5;i++)
            {
                var lamp=new GameObject("Merchant alcove lamp",typeof(Light));lamp.transform.SetParent(root.transform,false);lamp.transform.localPosition=new Vector3(-24+i*12,5,-180);
                var light=lamp.GetComponent<Light>();light.type=LightType.Point;light.color=colors[i];light.range=13;light.intensity=1.3f;light.shadows=LightShadows.None;visuals.lights.Add(light);
            }
            var glow=new GameObject("Ready brazier glow",typeof(Light));glow.transform.SetParent(root.transform,false);glow.transform.localPosition=new Vector3(0,2.8f,-171);
            var flame=glow.GetComponent<Light>();flame.type=LightType.Point;flame.color=new Color(1,.53f,.16f);flame.range=10;flame.intensity=2;flame.shadows=LightShadows.None;
        }
        private void Update()
        {
            var owner=GauntletController.Current;
            var phase=UnityEngine.Networking.NetworkServer.active?owner?.Flow.Phase:owner?.RemotePhase;
            bool open=phase==GauntletPhase.AwaitingExit;
            lift=Mathf.MoveTowards(lift,open?-7.5f:0,Time.unscaledDeltaTime*5);
            foreach(var shutter in shutters)if(shutter)shutter.localPosition=Vector3.up*lift;
            foreach(var light in lights)if(light)light.intensity=open?1.3f:.15f;
        }
    }
}
