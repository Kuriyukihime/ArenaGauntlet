using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace BazaarGauntlet
{
    // Per-arena ownership: only original embedded textures enter ownedTextures.
    // Native grass is borrowed and must survive scene teardown and retries.
    internal static class ArenaMaterials
    {
        // A Treasury skin overrides tint/smoothness and textures per surface; anything it leaves out keeps the default.
        internal static Material Create(FortressArchitectureData.Surface surface,List<Texture2D> ownedTextures,GauntletCosmetic skin=null)
        {
            var shader=FortressAssets.Load<Shader>("HGStandard_shader");
            if(!shader)throw new InvalidOperationException("Native arena shader unavailable.");
            var look=skin?.Surfaces.FirstOrDefault(s=>s!=null && s.Name==surface.Name);
            var material=new Material(shader){name="ArenaGauntlet "+surface.Name,
                color=look!=null?new Color(look.R,look.G,look.B):new Color(surface.R,surface.G,surface.B),enableInstancing=true};
            try
            {
                bool grass=surface.Name=="grass";
                string own=skin==null?"":skin.Id+"_"+surface.Name;
                var albedo=grass?FortressAssets.Load<Texture2D>("texGPGrassTerrain_png"):Read(Embedded(own,false)?own:surface.Name,false,ownedTextures);
                var normal=grass?FortressAssets.Load<Texture2D>("texGPGrassTerrainNormal_png"):Read(Embedded(own,true)?own:surface.Name,true,ownedTextures);
                if(!albedo || !normal)throw new InvalidDataException("Arena surface is incomplete: "+surface.Name);
                material.SetTexture("_MainTex",albedo);material.SetTexture("_NormalTex",normal);
                foreach(string keyword in new[]{"LIMBREMOVAL","FLOWMAP","SPLATMAP","FRESNEL_EMISSION","CUTOUT","FORCE_SPEC"})material.DisableKeyword(keyword);
                material.EnableKeyword("NORMALMAP");material.EnableKeyword("USE_VERTEX_COLORS");
                material.SetFloat("_ColorsOn",1);material.SetFloat("_NormalStrength",grass?.2f:.45f);
                material.SetFloat("_Smoothness",look?.Smoothness??surface.Smoothness);
                material.SetFloat("_SpecularStrength",grass?.02f:.10f);material.SetFloat("_SpecularExponent",24);
                material.SetFloat("_EmPower",0);material.SetFloat("_LimbRemovalOn",0);
                return material;
            }
            catch {UnityEngine.Object.Destroy(material);throw;}
        }
        private static string Resource(string surface,bool normal)=>"BazaarGauntlet.assets.arena_"+surface+(normal?"_normal":"")+".png";
        private static bool Embedded(string surface,bool normal)=>Assembly.GetExecutingAssembly().GetManifestResourceInfo(Resource(surface,normal))!=null;
        private static Texture2D Read(string surface,bool normal,List<Texture2D> ownedTextures)
        {
            string name=Resource(surface,normal);
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if(stream==null)throw new FileNotFoundException("Arena surface "+name);
                var bytes=new byte[stream.Length];int offset=0;
                while(offset<bytes.Length){int count=stream.Read(bytes,offset,bytes.Length-offset);if(count==0)throw new EndOfStreamException();offset+=count;}
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,normal){name=name,
                    wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=8};
                ownedTextures.Add(texture);
                if(!texture.LoadImage(bytes,true))throw new InvalidDataException("Arena texture "+name);
                return texture;
            }
        }
    }
}
