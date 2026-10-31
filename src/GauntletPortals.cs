using System;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
namespace BazaarGauntlet
{
    internal static class GauntletPortals
    {
        private static GameObject portal;
        private static float nextAttempt;
        internal static bool Owns(SceneExitController exit)=>portal && exit && exit.gameObject==portal;
        internal static void Ensure(GauntletController owner)
        {
            if(!NetworkServer.active || !owner || owner.ShuttingDown || owner.Flow.Phase!=GauntletPhase.AwaitingExit || !owner.Fortress.InCastle || owner.Fortress.Leaving || portal || Time.unscaledTime<nextAttempt)return;
            nextAttempt=Time.unscaledTime+3;
            GameObject candidate=null;
            try
            {
                var prefab=FortressAssets.Load<GameObject>("PortalShop_prefab");
                if(!prefab)throw new InvalidOperationException("Native blue portal is unavailable.");
                candidate=UnityEngine.Object.Instantiate(prefab,FortressBattlefield.Origin+new Vector3(0,.25f,12),Quaternion.Euler(0,180,0));
                var exit=candidate.GetComponent<SceneExitController>();
                if(!exit){UnityEngine.Object.Destroy(candidate);throw new InvalidOperationException("Blue portal has no scene exit.");}
                exit.useRunNextStageScene=false;exit.isAlternatePath=false;exit.destinationScene=SceneCatalog.FindSceneDef("bazaar");exit.doNotIncrementStageNumber=true;
                if(!exit.destinationScene){UnityEngine.Object.Destroy(candidate);throw new InvalidOperationException("Bazaar scene is unavailable.");}
                NetworkServer.Spawn(candidate);portal=candidate;
            }
            catch(Exception error){if(candidate)NetworkServer.Destroy(candidate);GauntletPlugin.Log.LogWarning("Blue portal will retry: "+error);}
        }
        internal static void Clear(){if(portal && NetworkServer.active)NetworkServer.Destroy(portal);portal=null;nextAttempt=0;}
        internal static void Announce(string message)=>Chat.SendBroadcastChat(new Chat.SimpleChatMessage{baseToken=message});
    }
}
