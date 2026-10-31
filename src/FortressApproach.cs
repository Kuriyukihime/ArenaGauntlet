using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.Navigation;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    // Scene-local addition. Never changes the installed map or its asset graph.
    internal sealed class FortressApproach:MonoBehaviour
    {
        internal static readonly Vector3[] Route={new Vector3(82,-20,-70),new Vector3(65,-20,-67),new Vector3(-14,0,-58),new Vector3(-21.416f,0,-56.825f),new Vector3(-28.107f,0,-53.416f),new Vector3(-33.416f,0,-48.107f),new Vector3(-36.825f,0,-41.416f),new Vector3(-38,0,-34),new Vector3(-38,0,-26.8f)};
        internal const float Width=26;
        private GameObject root;
        private NodeGraph graph;
        private NodeGraph.Node[] originalNodes;
        private NodeGraph.Link[] originalLinks;
        private bool attempted;
        internal static void Ensure(FortressController fortress)
        {
            var approach=fortress.GetComponent<FortressApproach>()??fortress.gameObject.AddComponent<FortressApproach>();
            if(approach.attempted || !fortress.InCastle)return;
            approach.attempted=true;
            if(fortress.State.Map!="ancientloft")return;
            try {approach.Build(fortress);}
            catch(Exception error){approach.RestoreGraph();GauntletPlugin.Log.LogError("Main causeway unavailable; castle spawning continues. "+error);}
        }
        private void Build(FortressController fortress)
        {
            root=new GameObject("Castle main causeway");root.transform.position=fortress.Center;
            root.transform.rotation=Quaternion.Euler(0,fortress.State.Yaw,0);
            var lifetime=root.AddComponent<FortressMeshLifetime>();
            foreach(var entry in FortressCausewayMesh.Build(Route,Width))
            {
                var mesh=entry.Mesh;lifetime.Meshes.Add(mesh);
                var child=new GameObject(mesh.name,typeof(MeshFilter),typeof(MeshRenderer));child.transform.SetParent(root.transform,false);
                child.GetComponent<MeshFilter>().sharedMesh=mesh;
                // Render and collision follow one continuous deck. The original
                // entrance stays beside the bypass, never underneath its slabs.
                if(entry.Name=="stone"){child.layer=LayerIndex.world.intVal;child.AddComponent<MeshCollider>().sharedMesh=mesh;}
                try {child.GetComponent<MeshRenderer>().sharedMaterial=FortressArchitecture.Surface(entry.Name);}
                catch(Exception error){GauntletPlugin.Log.LogWarning("Causeway material unavailable; collision and navigation remain active. "+error);}
            }
            Physics.SyncTransforms();
            if(NetworkServer.active)ExtendNavigation();
        }
        private void ExtendNavigation()
        {
            graph=SceneInfo.instance.GetNodeGraph(MapNodeGroup.GraphType.Ground);
            originalNodes=NativeAccess.Get<NodeGraph.Node[]>(graph,"nodes");originalLinks=NativeAccess.Get<NodeGraph.Link[]>(graph,"links");
            var nodes=new List<NodeGraph.Node>(originalNodes);var links=new List<NodeGraph.Link>(originalLinks);
            var endpointNodes=new List<int>();var previous=new int[0];
            void Link(int from,int to,int mask)
            {
                Vector3 delta=nodes[to].position-nodes[from].position;float flat=new Vector2(delta.x,delta.z).magnitude;
                links.Add(new NodeGraph.Link{nodeIndexA=new NodeGraph.NodeIndex(from),nodeIndexB=new NodeGraph.NodeIndex(to),
                    distanceScore=delta.magnitude,maxSlope=Mathf.Atan2(Mathf.Abs(delta.y),Mathf.Max(.01f,flat))*Mathf.Rad2Deg,hullMask=mask});
            }
            void Both(int a,int b,int mask){Link(a,b,mask);Link(b,a,mask);}
            for(int segment=0;segment<Route.Length-1;segment++)
            {
                Vector3 a=Route[segment],b=Route[segment+1];Vector3 side=Vector3.Cross(Vector3.up,b-a).normalized;
                int rows=Mathf.CeilToInt(Vector3.Distance(a,b)/4);
                for(int row=0;row<=rows;row++)
                {
                    var current=new int[3];
                    for(int lane=0;lane<3;lane++)
                    {
                        current[lane]=nodes.Count;Vector3 local=Vector3.Lerp(a,b,(float)row/rows)+side*(lane-1)*5;
                        nodes.Add(new NodeGraph.Node{position=root.transform.TransformPoint(local)+Vector3.up*.06f,flags=NodeFlags.None});
                        if(segment==0 && row==0 || segment==Route.Length-2 && row==rows)endpointNodes.Add(current[lane]);
                        if(lane>0)Both(current[lane-1],current[lane],7);
                    }
                    if(previous.Length>0)for(int lane=0;lane<3;lane++)
                        for(int other=0;other<3;other++)if(Math.Abs(lane-other)<=1)Both(previous[lane],current[other],7);
                    previous=current;
                }
            }
            foreach(int end in endpointNodes)for(int i=0;i<originalNodes.Length;i++)
            {
                Vector3 a=nodes[end].position,b=nodes[i].position,delta=b-a;
                if(Mathf.Abs(delta.y)>1.2f || delta.sqrMagnitude>18*18 || originalNodes[i].gateIndex!=0)continue;
                int mask=7&~(int)originalNodes[i].forbiddenHulls;if(mask==0)continue;
                if(Physics.Linecast(a+Vector3.up,b+Vector3.up,LayerIndex.world.mask))continue;
                Both(end,i,mask);
            }
            var ordered=links.OrderBy(link=>link.nodeIndexA.nodeIndex).ToArray();int cursor=0;
            for(int i=0;i<nodes.Count;i++)
            {
                var node=nodes[i];int first=cursor;while(cursor<ordered.Length && ordered[cursor].nodeIndexA.nodeIndex==i)cursor++;
                node.linkListIndex=new NodeGraph.LinkListIndex{index=first,size=(uint)(cursor-first)};
                var visible=new SerializableBitArray(nodes.Count);
                if(i<originalNodes.Length && node.lineOfSightMask!=null)
                    for(int bit=0;bit<Math.Min(node.lineOfSightMask.length,originalNodes.Length);bit++)visible[bit]=node.lineOfSightMask[bit];
                visible[i]=true;node.lineOfSightMask=visible;nodes[i]=node;
            }
            foreach(var link in ordered)if(link.nodeIndexA.nodeIndex>=originalNodes.Length || link.nodeIndexB.nodeIndex>=originalNodes.Length)
                nodes[link.nodeIndexA.nodeIndex].lineOfSightMask[link.nodeIndexB.nodeIndex]=true;
            NativeAccess.Set(graph,"nodes",nodes.ToArray());NativeAccess.Set(graph,"links",ordered);NativeAccess.Call(graph,"Awake");
            for(int hull=0;hull<3;hull++)
            {
                int mask=1<<hull;var edges=Enumerable.Range(0,ordered.Length).Where(index=>(ordered[index].hullMask&mask)!=0 && (ordered[index].jumpHullMask&mask)==0 && graph.IsLinkSuitableForHull(new NodeGraph.LinkIndex{linkIndex=index},(HullClassification)hull))
                    .Select(index=>(ordered[index].nodeIndexA.nodeIndex,ordered[index].nodeIndexB.nodeIndex));
                var goal=graph.FindClosestNode(root.transform.position,(HullClassification)hull);
                var reachable=GauntletRoutePlan.Reaches(nodes.Count,goal.nodeIndex,edges);
                if(!reachable[originalNodes.Length])throw new InvalidOperationException("Causeway does not connect the lower landing to the keep for "+(HullClassification)hull);
            }
            GauntletPlugin.Log.LogInfo($"Main causeway: {Width:0}m wide, {nodes.Count-originalNodes.Length} navigation nodes; original ground graph preserved for cleanup.");
        }
        private void RestoreGraph()
        {
            if(graph && originalNodes!=null)
            {NativeAccess.Set(graph,"nodes",originalNodes);NativeAccess.Set(graph,"links",originalLinks);NativeAccess.Call(graph,"Awake");}
            graph=null;originalNodes=null;originalLinks=null;
        }
        private void OnDestroy()
        {
            RestoreGraph();
            if(root)Destroy(root);
        }
    }
}
