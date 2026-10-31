using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace BazaarGauntlet
{
    // Treasury catalog: assets/arena_cosmetics.json, written by the art pipeline. Absent means no cosmetics yet.
    // A skin overrides surface tint/smoothness and may ship arena_<skin>_<surface>[_normal].png; decor lives in
    // assets/arena_decor.meshbin as parts named Decor<Id>-<surface>.
    [Serializable]
    internal sealed class GauntletCosmeticCatalog
    {
        public GauntletCosmetic[] Skins=Array.Empty<GauntletCosmetic>(),Decor=Array.Empty<GauntletCosmetic>();
    }
    [Serializable]
    internal sealed class GauntletCosmetic
    {
        public string Id,Name;
        public GauntletCosmeticSurface[] Surfaces=Array.Empty<GauntletCosmeticSurface>();
    }
    [Serializable]
    internal sealed class GauntletCosmeticSurface
    {
        public string Name="";
        public float R=1,G=1,B=1,Smoothness=0;
    }
    internal static class GauntletCosmetics
    {
        private static GauntletCosmeticCatalog catalog;
        internal static GauntletCosmeticCatalog Catalog=>catalog??=Load();
        internal static GauntletCosmetic Skin(string id)=>string.IsNullOrEmpty(id)?null:Catalog.Skins.FirstOrDefault(s=>s.Id==id);
        private static GauntletCosmeticCatalog Load()
        {
            var empty=new GauntletCosmeticCatalog();
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("BazaarGauntlet.assets.arena_cosmetics.json"))
            {
                if(stream==null)return empty;
                try
                {
                    var read=JsonUtility.FromJson<GauntletCosmeticCatalog>(new StreamReader(stream).ReadToEnd());
                    var all=(read?.Skins??Array.Empty<GauntletCosmetic>()).Concat(read?.Decor??Array.Empty<GauntletCosmetic>()).ToArray();
                    // Skins and decor share one owned list, so every id must be unique across both.
                    if(read==null || all.Any(c=>c==null || !GauntletMetaPolicy.ShortId(c.Id) || string.IsNullOrEmpty(c.Name) || c.Surfaces==null) ||
                        all.Select(c=>c.Id).Distinct().Count()!=all.Length)throw new InvalidDataException("Invalid Treasury catalog.");
                    return read;
                }
                catch(Exception error){GauntletPlugin.Log.LogError("Treasury cosmetics disabled: "+error.Message);return empty;}
            }
        }
    }
}
