using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RoR2;
using UnityEngine;
using UnityEngine.Rendering;

namespace BazaarGauntlet
{
    // Local-only company insignia. Hired units wear native enemy bodies, so a
    // war standard marks each commander and a small sigil marks its squad.
    internal sealed class GauntletInsignia:MonoBehaviour
    {
        private static FortressArchitectureData library;
        private static Mesh[] meshes;
        private static readonly Dictionary<int,Material> banners=new Dictionary<int,Material>(),glows=new Dictionary<int,Material>();
        private static bool failed;
        internal int Commander;internal bool Leader;
        private Transform visual;private float spin;
        internal static void Ensure(CharacterBody body,int commander,bool leader)
        {
            if(failed || !body)return;
            var existing=body.GetComponent<GauntletInsignia>();
            if(existing && existing.Commander==commander && existing.Leader==leader)return;
            if(existing)Destroy(existing);
            var badge=body.gameObject.AddComponent<GauntletInsignia>();badge.Commander=commander;badge.Leader=leader;
        }
        private static void Prepare()
        {
            if(library!=null)return;
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("BazaarGauntlet.assets.commanders.meshbin"))
                library=FortressArchitectureData.Read(stream??throw new FileNotFoundException("Commander insignia asset."));
            meshes=new Mesh[library.Parts.Length];
            for(int index=0;index<meshes.Length;index++)
            {
                var part=library.Parts[index];int count=part.Vertices.Length/8;
                var positions=new Vector3[count];var normals=new Vector3[count];var uv=new Vector2[count];
                for(int i=0;i<count;i++)
                {int k=i*8;positions[i]=new Vector3(part.Vertices[k],part.Vertices[k+1],part.Vertices[k+2]);normals[i]=new Vector3(part.Vertices[k+3],part.Vertices[k+4],part.Vertices[k+5]);uv[i]=new Vector2(part.Vertices[k+6],part.Vertices[k+7]);}
                meshes[index]=new Mesh{name="Insignia "+part.Name,indexFormat=IndexFormat.UInt32,vertices=positions,normals=normals,uv=uv,triangles=part.Indices};
                meshes[index].RecalculateBounds();
            }
        }
        private static Material Tinted(Dictionary<int,Material> cache,int commander,string surface)
        {
            if(cache.TryGetValue(commander,out var material))return material;
            var def=GauntletCommandRoster.Commanders[commander];var dye=new Color(def.R,def.G,def.B);
            // Pale glowing chevrons stay legible against the darker dyed cloth.
            var color=surface=="energy"?Color.Lerp(dye,Color.white,.55f):dye*.82f;color.a=1;
            material=new Material(FortressArchitecture.Surface(surface)){name="Company "+surface+" "+def.Name,color=color};
            if(material.HasProperty("_EmColor"))material.SetColor("_EmColor",surface=="energy"?color:Color.black);
            // Banner cloth is plain dyed fabric, not the masonry ivory texture.
            if(surface=="ivory" && material.HasProperty("_MainTex"))material.SetTexture("_MainTex",Texture2D.whiteTexture);
            return cache[commander]=material;
        }
        private float Top(CharacterBody body)
        {
            var model=body.modelLocator?body.modelLocator.modelTransform:null;
            Bounds? bounds=null;
            if(model)
                foreach(var renderer in model.GetComponentsInChildren<Renderer>())
                    if(renderer && renderer.enabled && !(renderer is ParticleSystemRenderer))
                    {if(bounds.HasValue){var value=bounds.Value;value.Encapsulate(renderer.bounds);bounds=value;}else bounds=renderer.bounds;}
            float top=bounds.HasValue?bounds.Value.max.y-body.transform.position.y:body.radius*2;
            return Mathf.Clamp(float.IsNaN(top)?body.radius*2:top,.5f,40);
        }
        private void Start()
        {
            var body=GetComponent<CharacterBody>();
            try
            {
                Prepare();
                var def=GauntletCommandRoster.Commanders[Commander];
                float scale=Leader?Mathf.Clamp(body.radius*.75f,.9f,3.2f):Mathf.Clamp(body.radius*.55f,.55f,1.6f);
                var root=new GameObject(Leader?"Company war standard":"Company sigil");visual=root.transform;
                visual.SetParent(body.transform,false);visual.localPosition=Vector3.up*(Top(body)+.35f*scale);visual.localScale=Vector3.one*scale;
                for(int index=0;index<library.Parts.Length;index++)
                {
                    string name=library.Parts[index].Name;
                    bool shown=Leader?name.StartsWith("standard-")||name.StartsWith("banner-")||name.StartsWith("rank"+def.Tier+"-"):name.StartsWith("sigil-");
                    if(!shown)continue;
                    string surface=library.Surfaces[library.Parts[index].Surface].Name;
                    // Cloth and glow carry the company color; brass stays brass.
                    var material=surface=="ivory"?Tinted(banners,Commander,"ivory"):surface=="energy"?Tinted(glows,Commander,"energy"):FortressArchitecture.Surface(surface);
                    var child=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));child.transform.SetParent(visual,false);
                    child.layer=LayerIndex.defaultLayer.intVal;
                    child.GetComponent<MeshFilter>().sharedMesh=meshes[index];
                    var renderer=child.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
                }
            }
            catch(Exception error)
            {
                // Cosmetics never interrupt combat; stop retrying a broken asset.
                failed=true;enabled=false;GauntletPlugin.Log.LogWarning("Company insignia unavailable; allied units fight without banners. "+error);
            }
        }
        private void LateUpdate()
        {
            if(!visual)return;
            spin+=Time.deltaTime*(Leader?24:60);
            visual.rotation=Quaternion.Euler(0,spin,0);
        }
        private void OnDestroy(){if(visual)Destroy(visual.gameObject);}
    }
}
