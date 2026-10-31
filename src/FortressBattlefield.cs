using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RoR2;
using RoR2.Navigation;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace BazaarGauntlet
{
    // A mode-local battlefield, using Titanic Plains' sky/lighting as a backdrop.
    // Native scenery and graph instances are restored when the mode tears down.
    internal sealed class FortressBattlefield:MonoBehaviour
    {
        internal static readonly Vector3 Origin=new Vector3(87.54f,-115f,-213.626f);
        private readonly List<(Renderer Renderer,bool Enabled)> hiddenRenderers=new List<(Renderer,bool)>();
        private readonly List<(Collider Collider,bool Enabled)> hiddenColliders=new List<(Collider,bool)>();
        private readonly List<(MapZone Zone,bool Enabled)> hiddenZones=new List<(MapZone,bool)>();
        private readonly HashSet<int> zoneIDs=new HashSet<int>();
        private readonly HashSet<int> rendererIDs=new HashSet<int>(),colliderIDs=new HashSet<int>();
        private readonly HashSet<int> nativeRendererIDs=new HashSet<int>(),nativeColliderIDs=new HashSet<int>();
        private readonly List<(NodeGraph Graph,NodeGraph.Node[] Nodes,NodeGraph.Link[] Links)> graphs=new List<(NodeGraph,NodeGraph.Node[],NodeGraph.Link[])>();
        private readonly List<(MeshRenderer Renderer,string Surface)> surfaces=new List<(MeshRenderer,string)>();
        private GameObject root;
        private FortressArchitectureData.Surface[] arenaSurfaces;
        private readonly Dictionary<string,Material> arenaMaterials=new Dictionary<string,Material>();
        private readonly List<Texture2D> ownedTextures=new List<Texture2D>();
        private int sceneHandle;
        private readonly Dictionary<CharacterBody,float> stranded=new Dictionary<CharacterBody,float>();
        private float nextRescue;
        // Treasury floor/decor objects built for the host's current skin/decor choice, after the colosseum's own parts.
        private readonly List<GameObject> cosmetics=new List<GameObject>();
        private FortressArchitectureData.Surface[] baseSurfaces;
        private int baseParts;
        private string appliedSkin="";
        private string[] appliedDecor;
        private bool built,materialsReady,released;
        private float rescanUntil,nextRescan,nextMaterialTry;
        internal static void Install(){On.RoR2.SceneInfo.OnEnable+=SceneReady;On.RoR2.SceneInfo.OnDisable+=SceneLeaving;}
        private static void SceneLeaving(On.RoR2.SceneInfo.orig_OnDisable original,SceneInfo info)
        {
            var owner=GauntletController.Current;
            if(owner)foreach(var field in owner.GetComponents<FortressBattlefield>())
                if(field.sceneHandle==info.gameObject.scene.handle){field.Release();Destroy(field);}
            original(info);
        }
        private static void SceneReady(On.RoR2.SceneInfo.orig_OnEnable original,SceneInfo info)
        {
            original(info);
            var owner=GauntletController.Current;
            if(owner && info.sceneDef && info.sceneDef.cachedName==FortressPlacement.SceneName)Ensure(owner);
        }
        internal static FortressBattlefield Ensure(GauntletController owner)
        {
            if(!owner || owner.ShuttingDown || !SceneInfo.instance || !SceneInfo.instance.sceneDef || SceneInfo.instance.sceneDef.cachedName!=FortressPlacement.SceneName)return null;
            var field=owner.GetComponents<FortressBattlefield>().FirstOrDefault(candidate=>!candidate.released)??owner.gameObject.AddComponent<FortressBattlefield>();
            if(field.built)return field;
            try {field.Build();field.built=true;return field;}
            catch(Exception error){field.Release();Destroy(field);GauntletPlugin.Log.LogError("Battlefield construction failed; no incomplete arena is accepted. "+error);throw;}
        }
        private bool NativeScene(Component component)=>component && component.gameObject.scene==SceneInfo.instance.gameObject.scene &&
            (!root || !component.transform.IsChildOf(root.transform)) && !component.GetComponentInParent<NetworkIdentity>() && !component.GetComponentInParent<CharacterBody>();
        private void ClearNativeScenery()
        {
            var bounds=new Bounds(Origin+Vector3.up*98,new Vector3(510,204,510));
            Physics.SyncTransforms();
            foreach(var zone in UnityEngine.Object.FindObjectsOfType<MapZone>(true))
                if(NativeScene(zone))
                {
                    foreach(var collider in zone.GetComponents<Collider>())Hide(collider);
                    if(zoneIDs.Add(zone.GetInstanceID()))hiddenZones.Add((zone,zone.enabled));zone.enabled=false;
                }
            foreach(var collider in UnityEngine.Object.FindObjectsOfType<Collider>(true))
                if(NativeScene(collider) && collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger &&
                    bounds.Intersects(collider.bounds))Hide(collider);
            foreach(var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
                if(NativeScene(renderer) && nativeRendererIDs.Contains(renderer.GetInstanceID()) && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                    (renderer.gameObject.layer==LayerIndex.world.intVal || renderer.transform.root.name.StartsWith("HOLDER:",StringComparison.Ordinal) || renderer.transform.root.name.StartsWith("FOLIAGE:",StringComparison.Ordinal)))
                {
                    if(rendererIDs.Add(renderer.GetInstanceID()))hiddenRenderers.Add((renderer,renderer.enabled));
                    renderer.enabled=false;
                }
        }
        private void Hide(Collider collider)
        {
            if(!collider || !collider.enabled || !nativeColliderIDs.Contains(collider.GetInstanceID()))return;
            if(colliderIDs.Add(collider.GetInstanceID()))hiddenColliders.Add((collider,collider.enabled));
            collider.enabled=false;
        }
        private void Build()
        {
            // Capture map instances before creating any of our floor, castle,
            // weapon or shop objects. Later rescans cannot hide mod gameplay.
            foreach(var collider in UnityEngine.Object.FindObjectsOfType<Collider>(true))if(NativeScene(collider))nativeColliderIDs.Add(collider.GetInstanceID());
            foreach(var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>(true))if(NativeScene(renderer))nativeRendererIDs.Add(renderer.GetInstanceID());
            sceneHandle=SceneInfo.instance.gameObject.scene.handle;
            root=new GameObject("ArenaGauntlet Hellenic arena");root.transform.position=Origin;
            SceneManager.MoveGameObjectToScene(root,SceneInfo.instance.gameObject.scene);
            ClearNativeScenery();BuildGeometry();BuildBoundary();Physics.SyncTransforms();
            int collisionMask=LayerIndex.world.mask|LayerIndex.CommonMasks.allCharacterCollisions;
            var unexpected=Physics.OverlapBox(Origin+Vector3.up*41,new Vector3(175,40,175),Quaternion.identity,collisionMask,QueryTriggerInteraction.Ignore)
                .Where(collider=>collider && !collider.transform.IsChildOf(root.transform) && !collider.GetComponentInParent<NetworkIdentity>() && !collider.GetComponentInParent<CharacterBody>()).ToArray();
            if(unexpected.Length>0)throw new InvalidOperationException("Native scenery still blocks the battlefield: "+string.Join(", ",unexpected.Select(collider=>collider.name).Distinct()));
            if(NetworkServer.active)
            {
                ReplaceGraph(SceneInfo.instance.groundNodes,FortressBattlefieldGrid.Ground());
                ReplaceGraph(SceneInfo.instance.airNodes,FortressBattlefieldGrid.Air());
            }
            rescanUntil=Time.unscaledTime+8;nextRescan=0;
            GauntletPlugin.Log.LogInfo($"Hellenic stone arena: 358m diameter fighting floor on Titanic Plains, origin {Origin}; native obstacles cleared locally. Ground and aerial approaches use the same flat surface.");
        }
        private void BuildGeometry()
        {
            var lifetime=root.AddComponent<FortressMeshLifetime>();
            var floor=new GameObject("Continuous arena collision",typeof(BoxCollider));floor.layer=LayerIndex.world.intVal;floor.transform.SetParent(root.transform,false);
            floor.transform.localPosition=Vector3.down*6;floor.GetComponent<BoxCollider>().size=new Vector3(500,12,500);
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("BazaarGauntlet.assets.colosseum.meshbin"))
            {
                var art=FortressArchitectureData.Read(stream??throw new InvalidOperationException("Colosseum art is missing."));
                arenaSurfaces=baseSurfaces=art.Surfaces;
                foreach(var part in art.Parts)
                {
                    var mesh=BuildMesh(part,lifetime);Render(mesh,art.Surfaces[part.Surface].Name);
                    // Paving, inlays and grass share the uninterrupted floor
                    // collider. Decorative seams cannot snag players or bosses.
                    if(!part.Name.StartsWith("Courtyard",StringComparison.Ordinal) && !part.Name.StartsWith("MerchantShutters",StringComparison.Ordinal))
                    {
                        var hull=new GameObject("Colosseum solid architecture",typeof(MeshCollider));hull.layer=LayerIndex.world.intVal;hull.transform.SetParent(root.transform,false);hull.GetComponent<MeshCollider>().sharedMesh=mesh;
                    }
                }
            }
            baseParts=surfaces.Count;
            GauntletShopVisuals.Build(root);
        }
        // Treasury: the selected skin's matching floor (arena_<skin>_floor.meshbin) and each shown decoration set
        // (arena_decor.meshbin parts named Decor<Id>-<surface>). Visual only and flush or outside the fighting floor,
        // so no colliders. Only selected art is built; materials rebuild through the retry path.
        private void ApplyCosmetics()
        {
            var lifetime=root.GetComponent<FortressMeshLifetime>();
            foreach(var go in cosmetics)if(go){Destroy(go.GetComponent<MeshFilter>().sharedMesh);Destroy(go);}
            cosmetics.Clear();surfaces.RemoveRange(baseParts,surfaces.Count-baseParts);arenaSurfaces=baseSurfaces;
            void Add(FortressArchitectureData art,Func<string,bool> shown)
            {
                if(art==null)return;
                arenaSurfaces=arenaSurfaces.Concat(art.Surfaces.Where(added=>arenaSurfaces.All(known=>known.Name!=added.Name))).ToArray();
                foreach(var part in art.Parts.Where(part=>shown(part.Name)))cosmetics.Add(Render(BuildMesh(part,lifetime),art.Surfaces[part.Surface].Name));
            }
            if(GauntletCosmetics.Skin(appliedSkin)!=null)Add(ReadCosmetic("arena_"+appliedSkin+"_floor"),name=>true);
            if(appliedDecor.Length>0)Add(ReadCosmetic("arena_decor"),name=>appliedDecor.Any(id=>name.StartsWith("Decor"+id+"-",StringComparison.Ordinal)));
            foreach(var entry in surfaces)entry.Renderer.enabled=false;
            ReleaseMaterials();nextMaterialTry=0;
        }
        // A missing or bad cosmetic file never blocks the arena.
        private static FortressArchitectureData ReadCosmetic(string name)
        {
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("BazaarGauntlet.assets."+name+".meshbin"))
            {
                if(stream==null)return null;
                try {return FortressArchitectureData.Read(stream);}
                catch(Exception error){GauntletPlugin.Log.LogError("Arena cosmetic unavailable ("+name+"): "+error.Message);return null;}
            }
        }
        private static Mesh BuildMesh(FortressArchitectureData.Part part,FortressMeshLifetime lifetime)
        {
            int count=part.Vertices.Length/8;var p=new Vector3[count];var n=new Vector3[count];var tex=new Vector2[count];
            for(int i=0;i<count;i++){int k=i*8;p[i]=new Vector3(part.Vertices[k],part.Vertices[k+1],part.Vertices[k+2]);n[i]=new Vector3(part.Vertices[k+3],part.Vertices[k+4],part.Vertices[k+5]);tex[i]=new Vector2(part.Vertices[k+6],part.Vertices[k+7]);}
            var mesh=new Mesh{name=part.Name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,vertices=p,normals=n,uv=tex,triangles=part.Indices};
            mesh.colors32=Enumerable.Repeat(new Color32(255,255,255,255),count).ToArray();
            mesh.RecalculateBounds();mesh.RecalculateTangents();lifetime.Meshes.Add(mesh);return mesh;
        }
        private GameObject Render(Mesh mesh,string surface)
        {
            var go=new GameObject(mesh.name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);go.layer=LayerIndex.world.intVal;
            go.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.GetComponent<MeshRenderer>();renderer.enabled=false;surfaces.Add((renderer,surface));
            return go;
        }
        private void BuildBoundary()
        {
            var go=new GameObject("Battlefield return boundary",typeof(BoxCollider));go.layer=LayerIndex.triggerZone.intVal;go.transform.SetParent(root.transform,false);
            var box=go.GetComponent<BoxCollider>();box.isTrigger=true;box.center=new Vector3(0,190,0);box.size=new Vector3(520,420,520);
            var destination=new GameObject("Safe castle return").transform;destination.SetParent(root.transform,false);destination.localPosition=new Vector3(6,3,6);
            // A kill-on-exit zone can instantly kill burrowing bosses underneath
            // the field. Return allies; recover ordinary stranded enemies below.
            var zone=go.AddComponent<MapZone>();zone.zoneType=MapZone.ZoneType.KickOutPlayers;zone.triggerType=MapZone.TriggerType.TriggerExit;
            zone.explicitDestination=destination;zone.useExactExplicitPositionAndRotation=true;
        }
        private void ReplaceGraph(NodeGraph graph,FortressBattlefieldGrid layout)
        {
            if(!graph)throw new InvalidOperationException("Native battlefield navigation graph missing.");
            graphs.Add((graph,NativeAccess.Get<NodeGraph.Node[]>(graph,"nodes"),NativeAccess.Get<NodeGraph.Link[]>(graph,"links")));
            var nodes=new NodeGraph.Node[layout.Count];var links=new List<NodeGraph.Link>();
            for(int i=0;i<nodes.Length;i++)
            {
                var p=layout.Position(i);int first=links.Count;var visible=new SerializableBitArray(nodes.Length);visible[i]=true;
                foreach(int neighbor in layout.Neighbors(i))
                {
                    var other=layout.Position(neighbor);float dx=other.X-p.X,dy=other.Y-p.Y,dz=other.Z-p.Z;
                    links.Add(new NodeGraph.Link{nodeIndexA=new NodeGraph.NodeIndex(i),nodeIndexB=new NodeGraph.NodeIndex(neighbor),hullMask=7,distanceScore=Mathf.Sqrt(dx*dx+dy*dy+dz*dz),maxSlope=0});visible[neighbor]=true;
                }
                nodes[i]=new NodeGraph.Node{position=Origin+new Vector3(p.X,p.Y,p.Z),flags=layout.Edge(i)?NodeFlags.NoCharacterSpawn:NodeFlags.None,
                    lineOfSightMask=visible,linkListIndex=new NodeGraph.LinkListIndex{index=first,size=(uint)(links.Count-first)}};
            }
            NativeAccess.Set(graph,"nodes",nodes);NativeAccess.Set(graph,"links",links.ToArray());NativeAccess.Call(graph,"Awake");
        }
        private void ConfigureMaterials()
        {
            if(arenaMaterials.Count==0)
            {
                var skin=GauntletCosmetics.Skin(appliedSkin);
                try {foreach(var surface in arenaSurfaces)arenaMaterials.Add(surface.Name,ArenaMaterials.Create(surface,ownedTextures,skin));}
                catch {ReleaseMaterials();throw;}
            }
            foreach(var entry in surfaces){entry.Renderer.sharedMaterial=arenaMaterials[entry.Surface];entry.Renderer.enabled=true;}
            materialsReady=true;
        }
        private void ReleaseMaterials()
        {
            foreach(var material in arenaMaterials.Values)if(material)Destroy(material);arenaMaterials.Clear();
            foreach(var texture in ownedTextures)if(texture)Destroy(texture);ownedTextures.Clear();materialsReady=false;
        }
        private void LateUpdate()
        {
            if(!built || released || !SceneInfo.instance)return;
            var owner=GetComponent<GauntletController>();
            // Clients learn the host's choices from career status, possibly after the arena is built.
            if(owner && owner.Meta && (owner.Meta.Skin!=appliedSkin || owner.Meta.Decor!=appliedDecor))
            {appliedSkin=owner.Meta.Skin;appliedDecor=owner.Meta.Decor;ApplyCosmetics();}
            if(!materialsReady && Time.unscaledTime>=nextMaterialTry)
            {
                nextMaterialTry=Time.unscaledTime+3;
                try {ConfigureMaterials();}catch(Exception error){GauntletPlugin.Log.LogWarning("Battlefield appearance will retry; terrain collision/navigation remain active. "+error);}
            }
            if(Time.unscaledTime<rescanUntil && Time.unscaledTime>=nextRescan)
            {nextRescan=Time.unscaledTime+1;ClearNativeScenery();}
            foreach(var entry in hiddenRenderers)if(entry.Renderer && entry.Renderer.enabled)entry.Renderer.enabled=false;
            foreach(var entry in hiddenColliders)if(entry.Collider && entry.Collider.enabled)entry.Collider.enabled=false;
        }
        private void FixedUpdate()
        {
            if(!NetworkServer.active || !built || released || Time.fixedTime<nextRescue)return;
            nextRescue=Time.fixedTime+1;
            var owner=GetComponent<GauntletController>();if(!owner || owner.ShuttingDown || !owner.Fortress.IsCombat){stranded.Clear();return;}
            var present=new HashSet<CharacterBody>();
            foreach(var member in TeamComponent.GetTeamMembers(TeamIndex.Monster))
            {
                var body=member?member.body:null;
                if(!body || !body.master || !body.master.GetComponent<GauntletAggression>() || !body.healthComponent || !body.healthComponent.alive)continue;
                // Native worms intentionally travel under terrain; phase states
                // with hidden invincibility also retain their scripted movement.
                if(body.name.IndexOf("Worm",StringComparison.OrdinalIgnoreCase)>=0 || body.HasBuff(RoR2Content.Buffs.HiddenInvincibility))continue;
                Vector3 local=body.corePosition-Origin;
                if(new Vector2(local.x,local.z).sqrMagnitude<=174*174 && local.y>=-24)continue;
                present.Add(body);
                if(!stranded.TryGetValue(body,out float since)){stranded[body]=Time.fixedTime;continue;}
                if(Time.fixedTime-since<3)continue;
                local.y=0;if(local.sqrMagnitude<55*55)local=local.sqrMagnitude>.1f?local.normalized*66:Vector3.forward*66;
                if(local.magnitude>150)local=local.normalized*150;
                var graph=SceneInfo.instance.GetNodeGraph(body.isFlying?MapNodeGroup.GraphType.Air:MapNodeGroup.GraphType.Ground);
                var node=graph.FindClosestNode(Origin+local,body.hullClassification);
                if(graph.GetNodePosition(node,out var position))
                    TeleportHelper.TeleportBody(new TeleportHelper.TeleportBodyArgs{body=body,targetPosition=position+Vector3.up});
                stranded.Remove(body);
            }
            foreach(var body in stranded.Keys.Where(body=>!body || !present.Contains(body)).ToArray())stranded.Remove(body);
        }
        internal void RefreshScenery(){if(built && !released)ClearNativeScenery();}
        internal void Release()
        {
            if(released)return;released=true;
            foreach(var saved in graphs)if(saved.Graph){NativeAccess.Set(saved.Graph,"nodes",saved.Nodes);NativeAccess.Set(saved.Graph,"links",saved.Links);NativeAccess.Call(saved.Graph,"Awake");}
            graphs.Clear();
            foreach(var entry in hiddenRenderers)if(entry.Renderer)entry.Renderer.enabled=entry.Enabled;
            foreach(var entry in hiddenColliders)if(entry.Collider)entry.Collider.enabled=entry.Enabled;
            foreach(var entry in hiddenZones)if(entry.Zone)entry.Zone.enabled=entry.Enabled;
            hiddenRenderers.Clear();hiddenColliders.Clear();hiddenZones.Clear();surfaces.Clear();cosmetics.Clear();stranded.Clear();
            if(root)Destroy(root);ReleaseMaterials();
        }
        private void OnDestroy(){Release();}
    }
}
