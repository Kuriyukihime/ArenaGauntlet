using System;
using System.Linq;
using RoR2;
using UnityEngine;
namespace BazaarGauntlet
{
    // Read-only legacy snapshot schema. No menu or client control requests remain.
    internal static class FortressNetwork
    {
        internal static void Apply(FortressController fortress,string json)
        {
            if(string.IsNullOrEmpty(json)||json.Length>24000)return;
            try
            {
                var state=JsonUtility.FromJson<FortressState>(json);
                if(state==null || state.Revision<fortress.State.Revision || state.Ranks==null || state.Ranks.Length!=(int)FortressUpgrade.Count || state.Modules==null || state.Modules.Length!=FortressTree.ModuleCount ||
                    state.ShopPlayers==null || state.ShopBought==null || state.ShopPlayers.Length>64 || state.ShopPlayers.Length!=state.ShopBought.Length || state.ShopBought.Any(mask=>!GauntletShopPlan.ValidMask(mask)) ||
                    float.IsNaN(state.X)||float.IsInfinity(state.X)||float.IsNaN(state.Y)||float.IsInfinity(state.Y)||float.IsNaN(state.Z)||float.IsInfinity(state.Z))return;
                for(int i=0;i<state.Ranks.Length;i++)if(state.Ranks[i]<0 || state.Ranks[i]>FortressTree.Nodes[i].MaxRank)return;
                for(int i=0;i<state.Modules.Length;i++)if(state.Modules[i]==null || float.IsNaN(state.Modules[i].Health)||float.IsNaN(state.Modules[i].Maximum)||
                    float.IsInfinity(state.Modules[i].Health)||float.IsInfinity(state.Modules[i].Maximum)||float.IsNaN(state.Modules[i].GroundOffset)||Mathf.Abs(state.Modules[i].GroundOffset)>500 ||state.Modules[i].Health<0 ||state.Modules[i].Maximum<0)return;
                fortress.State=state;
            }
            catch(ArgumentException){GauntletPlugin.Log.LogWarning("Ignored malformed fortress snapshot.");}
        }
    }
}
