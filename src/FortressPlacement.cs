using RoR2;
using UnityEngine;

namespace BazaarGauntlet
{
    internal static class FortressPlacement
    {
        internal const string SceneName="golemplains";
        internal static Vector3? Choose(GauntletController owner,Vector3? fallback)
        {
            if(owner.Fortress.State.Placed)return owner.Fortress.Center;
            if(!SceneInfo.instance || !SceneInfo.instance.sceneDef || SceneInfo.instance.sceneDef.cachedName!=SceneName)return fallback;
            FortressBattlefield.Ensure(owner);
            return FortressBattlefield.Origin;
        }
    }
}
