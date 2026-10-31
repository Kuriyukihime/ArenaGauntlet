using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace BazaarGauntlet
{
    internal static class FortressArchitecture
    {
        private static FortressArchitectureData library;
        private static Material[] surfaces;
        private static Mesh[] meshes;
        private static bool prepared;
        internal static Material Surface(string name)
        {
            Prepare();
            for(int i=0;i<library.Surfaces.Length;i++)if(library.Surfaces[i].Name==name)return surfaces[i];
            throw new ArgumentException("Unknown castle surface: "+name);
        }
        private static void Prepare()
        {
            if(prepared)return;
            var shader=FortressAssets.Load<Shader>("HGStandard_shader");
            if(!shader)throw new InvalidOperationException("Native castle shader unavailable.");
            var assembly=Assembly.GetExecutingAssembly();
            using(var stream=assembly.GetManifestResourceStream("BazaarGauntlet.assets.castle.meshbin"))
                library=FortressArchitectureData.Read(stream??throw new FileNotFoundException("Castle architecture asset."));
            surfaces=new Material[library.Surfaces.Length];meshes=new Mesh[library.Parts.Length];
            for(int i=0;i<surfaces.Length;i++)
            {
                var source=library.Surfaces[i];var color=new Color(source.R,source.G,source.B);
                var material=new Material(shader){name="Castle "+source.Name,color=color,enableInstancing=true};
                using(var stream=assembly.GetManifestResourceStream("BazaarGauntlet.assets.castle_"+source.Name+".png"))
                {
                    if(stream==null)throw new FileNotFoundException("Castle surface "+source.Name);
                    var bytes=new byte[stream.Length];int read=0;
                    while(read<bytes.Length){int size=stream.Read(bytes,read,bytes.Length-read);if(size==0)throw new EndOfStreamException();read+=size;}
                    var texture=new Texture2D(2,2,TextureFormat.RGBA32,true){name="Castle "+source.Name,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=4};
                    if(!texture.LoadImage(bytes,true))throw new InvalidDataException("Castle texture "+source.Name);
                    if(material.HasProperty("_MainTex"))material.SetTexture("_MainTex",texture);
                }
                foreach(string keyword in new[]{"LIMBREMOVAL","NORMALMAP","_NORMALMAP","FLOWMAP","SPLATMAP","FRESNEL_EMISSION","CUTOUT","FORCE_SPEC"})material.DisableKeyword(keyword);
                if(material.HasProperty("_NormalStrength"))material.SetFloat("_NormalStrength",0);
                if(material.HasProperty("_LimbRemovalOn"))material.SetFloat("_LimbRemovalOn",0);
                if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",source.Smoothness);
                if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",source.Smoothness);
                if(material.HasProperty("_SpecularStrength"))material.SetFloat("_SpecularStrength",source.Name=="brass"?.35f:.12f);
                if(material.HasProperty("_SpecularExponent"))material.SetFloat("_SpecularExponent",28);
                if(material.HasProperty("_EmTex"))material.SetTexture("_EmTex",Texture2D.whiteTexture);
                if(material.HasProperty("_EmColor"))material.SetColor("_EmColor",color);
                if(material.HasProperty("_EmPower"))material.SetFloat("_EmPower",source.Emission);
                surfaces[i]=material;
            }
            prepared=true;
        }
        private static Mesh Mesh(int index)
        {
            if(meshes[index])return meshes[index];
            var part=library.Parts[index];int count=part.Vertices.Length/8;
            var positions=new Vector3[count];var normals=new Vector3[count];var uv=new Vector2[count];
            for(int i=0;i<count;i++)
            {int k=i*8;positions[i]=new Vector3(part.Vertices[k],part.Vertices[k+1],part.Vertices[k+2]);normals[i]=new Vector3(part.Vertices[k+3],part.Vertices[k+4],part.Vertices[k+5]);uv[i]=new Vector2(part.Vertices[k+6],part.Vertices[k+7]);}
            var mesh=new Mesh{name=part.Name,indexFormat=IndexFormat.UInt32,vertices=positions,normals=normals,uv=uv,triangles=part.Indices};
            mesh.RecalculateBounds();meshes[index]=mesh;return mesh;
        }
        internal static GameObject Build(FortressController fortress)
        {
            Prepare();var root=new GameObject("Gauntlet tiered citadel");root.transform.position=fortress.Center;root.transform.rotation=Quaternion.Euler(0,fortress.State.Yaw,0);
            try
            {
                var groups=new Dictionary<int,List<CombineInstance>>();
                for(int index=0;index<library.Parts.Length;index++)
                {
                    var part=library.Parts[index];if(!part.Visible(fortress.State))continue;
                    // Body geometry is batched across the entire castle. Only
                    // aiming heads and the reactor require independent meshes.
                    int group=part.Attachment==0?-1:part.Attachment==2?-2:part.Module;
                    int key=(group+2)*surfaces.Length+part.Surface;
                    if(!groups.TryGetValue(key,out var list))groups[key]=list=new List<CombineInstance>();
                    var offset=FortressController.Offset(part.Module)+Vector3.up*fortress.State.Modules[part.Module].GroundOffset;
                    Quaternion rotation=Quaternion.Euler(0,part.Module>=1 && part.Module<=4?(part.Module-1)*90:0,0);
                    list.Add(new CombineInstance{mesh=Mesh(index),transform=part.Attachment==0?Matrix4x4.TRS(offset,rotation,Vector3.one):Matrix4x4.identity});
                }
                var attachments=new Dictionary<int,Transform>();var lifetime=root.AddComponent<FortressMeshLifetime>();
                foreach(var pair in groups)
                {
                    int group=pair.Key/surfaces.Length-2,material=pair.Key%surfaces.Length;Transform parent=root.transform;
                    if(group!=-1)
                    {
                        if(!attachments.TryGetValue(group,out parent))
                        {
                            parent=new GameObject(group==-2?"Living reactor":"Aiming tower "+group).transform;parent.SetParent(root.transform,false);
                            int module=group==-2?0:group;
                            parent.localPosition=FortressController.Offset(module)+Vector3.up*(fortress.State.Modules[module].GroundOffset+(group==-2?4.5f:3.65f));
                            if(group==-2)parent.gameObject.AddComponent<FortressReactorMotion>().Fortress=fortress;
                            else parent.gameObject.AddComponent<FortressAimingHead>().Module=group;
                            attachments[group]=parent;
                        }
                    }
                    var mesh=new Mesh{name="Castle batched "+group+" "+library.Surfaces[material].Name,indexFormat=IndexFormat.UInt32};
                    mesh.CombineMeshes(pair.Value.ToArray(),true,true);mesh.RecalculateBounds();lifetime.Meshes.Add(mesh);
                    var child=new GameObject(mesh.name,typeof(MeshFilter),typeof(MeshRenderer));child.transform.SetParent(parent,false);
                    child.layer=RoR2.LayerIndex.defaultLayer.intVal;
                    child.GetComponent<MeshFilter>().sharedMesh=mesh;child.GetComponent<MeshRenderer>().sharedMaterial=surfaces[material];
                    if(group==-1 && library.Surfaces[material].Name!="energy")
                    {
                        AddHullCollision(child,mesh);
                    }
                }
                return root;
            }
            catch {UnityEngine.Object.Destroy(root);throw;}
        }
        private static void AddHullCollision(GameObject rendered,Mesh mesh)
        {
            // Cameras exclude the hull-only layer. Keep it off every renderer:
            // a child with only a collider shares the exact rendered geometry.
            var collision=new GameObject("Castle hull collision",typeof(MeshCollider));
            collision.transform.SetParent(rendered.transform,false);
            collision.layer=RoR2.LayerIndex.collideWithCharacterHullOnly.intVal;
            collision.GetComponent<MeshCollider>().sharedMesh=mesh;
        }
    }
    internal sealed class FortressReactorMotion:MonoBehaviour
    {
        internal FortressController Fortress;private Vector3 rest;private Renderer glow;private MaterialPropertyBlock block;
        private void Start()
        {
            rest=transform.localPosition;block=new MaterialPropertyBlock();
            foreach(var renderer in GetComponentsInChildren<Renderer>())if(renderer.sharedMaterial && renderer.sharedMaterial.name=="Castle energy"){glow=renderer;break;}
        }
        private void Update()
        {
            float seconds=Time.time;transform.localPosition=rest+Vector3.up*(Mathf.Sin(seconds*1.4f)*.09f);
            transform.localRotation=Quaternion.Euler(0,seconds*10,0);
            if(glow && Fortress)
            {
                var core=Fortress.State.Modules[0];float health=core.Maximum>0?core.Health/core.Maximum:1;
                float danger=Mathf.Clamp01((.4f-health)/.4f);
                block.SetColor("_EmColor",Color.Lerp(new Color(.18f,.76f,1),new Color(1,.35f,.12f),danger));
                block.SetFloat("_EmPower",1.55f+(.16f+danger*.35f)*Mathf.Sin(seconds*(1.8f+danger*4)));
                glow.SetPropertyBlock(block);
            }
        }
    }
}
