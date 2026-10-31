using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using RoR2;
using UnityEngine;
using Path=System.IO.Path;

namespace BazaarGauntlet
{
    internal static class GauntletMetaStore
    {
        private static readonly Dictionary<string,GauntletCareer> careers=new Dictionary<string,GauntletCareer>();
        private static string Key(LocalUser user)=>user?.userProfile?.fileName;
        private static string FileName(string key)
        {
            using(var hash=SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","")+".json";
        }
        // Game-wide, outside every mod-manager profile, so Glory survives updates, reinstalls and switching profiles.
        private static string PathFor(string key)=>Path.Combine(Application.persistentDataPath,"ArenaGauntlet","careers",FileName(key));
        // Up to 0.36 the save lived in each profile's BepInEx/config. Check this profile and its sibling profiles.
        private static IEnumerable<string> LegacyPaths(string key)
        {
            string name=FileName(key);
            yield return Path.Combine(Paths.ConfigPath,"ArenaGauntlet","careers",name);
            var profiles=Directory.GetParent(Paths.BepInExRootPath)?.Parent;
            if(profiles==null || !profiles.Exists || !profiles.Name.Equals("profiles",StringComparison.OrdinalIgnoreCase))yield break;
            DirectoryInfo[] siblings;
            try {siblings=profiles.GetDirectories();}catch(Exception){yield break;}
            foreach(var profile in siblings)yield return Path.Combine(profile.FullName,"BepInEx","config","ArenaGauntlet","careers",name);
        }
        private static GauntletCareer Read(string path)
        {
            var read=GauntletMetaPolicy.Normalize(JsonUtility.FromJson<GauntletCareer>(File.ReadAllText(path,Encoding.UTF8)));
            if(!GauntletMetaPolicy.Valid(read))throw new FormatException("Invalid career data.");
            return read;
        }
        private static GauntletCareer TryRead(string path){try {return Read(path);}catch(Exception){return null;}}
        internal static GauntletCareer For(LocalUser user)
        {
            string key=Key(user);if(string.IsNullOrEmpty(key))return null;
            if(careers.TryGetValue(key,out var known))return known;
            var value=new GauntletCareer();string path=PathFor(key);
            if(File.Exists(path))
            {
                try {value=Read(path);}
                catch(Exception error)
                {
                    // Preserve the original and refuse writes instead of replacing a damaged save with an empty career.
                    GauntletPlugin.Log.LogError("Arena career could not be loaded; original save preserved: "+error.Message);return null;
                }
            }
            else
            {
                // One-time import: the old save with the most Glory earned wins. Old files are left in place as backups.
                var legacy=LegacyPaths(key).Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).Select(TryRead).Where(c=>c!=null)
                    .OrderByDescending(c=>c.Earned).FirstOrDefault();
                if(legacy!=null)
                {
                    careers[key]=legacy;
                    if(Save(user))GauntletPlugin.Log.LogInfo($"Arena career imported from a previous version ({legacy.Earned} Glory earned).");
                    return legacy;
                }
            }
            careers[key]=value;return value;
        }
        // Run receipts are kept so a late award from an earlier run cannot be counted twice.
        internal static bool Reset(LocalUser user)
        {
            var old=For(user);string key=Key(user);if(old==null || string.IsNullOrEmpty(key))return false;
            careers[key]=new GauntletCareer{Receipts=old.Receipts,Received=old.Received};
            if(Save(user))return true;
            careers[key]=old;return false;
        }
        internal static bool Save(LocalUser user)
        {
            var c=For(user);string key=Key(user);if(c==null || string.IsNullOrEmpty(key))return false;
            try
            {
                string path=PathFor(key);Directory.CreateDirectory(Path.GetDirectoryName(path));
                string pending=path+".tmp";File.WriteAllText(pending,JsonUtility.ToJson(c,true),new UTF8Encoding(false));
                if(File.Exists(path))File.Replace(pending,path,path+".bak");else File.Move(pending,path);
                return true;
            }
            catch(Exception error){GauntletPlugin.Log.LogError("Arena career save failed: "+error.Message);return false;}
        }
    }
}
