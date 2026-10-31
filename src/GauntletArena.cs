using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.Navigation;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal static class GauntletArena
    {
        internal static void Install()
        {
            FortressBattlefield.Install();
            SceneDirector.onPreGeneratePlayerSpawnPointsServer += ChooseSpawnGenerator;
            On.RoR2.InfiniteTowerRun.OnDestroy += DestroyRun;
            On.RoR2.InfiniteTowerRun.SpawnSafeWard+=PlaceWard;
        }
        private static void PlaceWard(On.RoR2.InfiniteTowerRun.orig_SpawnSafeWard original,InfiniteTowerRun run,InteractableSpawnCard card,DirectorPlacementRule placement)
        {
            var owner=GauntletMode.IsActive(run)?run.GetComponent<GauntletController>():null;
            if(owner && owner.Fortress.PlannedAnchor.HasValue)
                placement=new DirectorPlacementRule{placementMode=DirectorPlacementRule.PlacementMode.Direct,position=owner.Fortress.PlannedAnchor.Value};
            original(run,card,placement);
        }

        internal static Vector3? Prepare(GauntletController owner)
        {
            CleanUp(owner);
            FortressBattlefield.Ensure(owner);
            var anchor = SpawnPoint.readOnlyInstancesList.FirstOrDefault(point => point);
            Vector3? fallback = anchor ? anchor.transform.position : (Vector3?)null;
            fallback=FortressPlacement.Choose(owner,fallback);owner.Fortress.PlannedAnchor=fallback;
            ClearSceneSpawnPoints();
            return fallback;
        }

        internal static void Ready(GauntletController owner, Vector3? fallback)
        {
            StopFog(owner.Run);
            var ward = NativeAccess.Get<InfiniteTowerSafeWardController>(owner.Run, "safeWardController");
            if (!ward && fallback.HasValue)
            {
                // Reuse the native setup (activation callback, holdout and
                // networking) at a known player anchor if random placement fails.
                var card = NativeAccess.Get<InteractableSpawnCard>(owner.Run, "initialSafeWardCard");
                if (card)
                    NativeAccess.Call(owner.Run, "SpawnSafeWard", card, new DirectorPlacementRule
                    { placementMode = DirectorPlacementRule.PlacementMode.Direct, position = fallback.Value });
                ward = NativeAccess.Get<InfiniteTowerSafeWardController>(owner.Run, "safeWardController");
            }
            owner.ArenaWard = ward;
            owner.ArenaReady = ward;
            if (!ward)
                GauntletPlugin.Log.LogError("No combat ward could be placed. Damaging fog remains disabled; restart to choose another arena.");
        }

        private static void ChooseSpawnGenerator(SceneDirector scene, ref Action generationMethod)
        {
            var owner = GauntletController.Current;
            if (!owner || !NetworkServer.active || GauntletRounds.IsBazaar || !owner.ArenaWard) return;
            generationMethod = () => PlacePlayerSpawns(owner);
        }

        private static void ClearSceneSpawnPoints()
        {
            foreach (var point in SpawnPoint.readOnlyInstancesList.ToArray())
            {
                if (!point) continue;
                // Destroy is deferred. Disable first so OnDisable removes it
                // from the native static list before any player consumes it.
                point.enabled = false;
                // Scene anchors may share a GameObject with map components.
                UnityEngine.Object.Destroy(point);
            }
        }

        private static void PlacePlayerSpawns(GauntletController owner)
        {
            if (!owner || owner.ShuttingDown || !owner.ArenaWard) return;
            ClearSceneSpawnPoints();
            Vector3 center = owner.ArenaWard.transform.position;
            var positions = new List<Vector3>();
            var graph = SceneInfo.instance ? SceneInfo.instance.GetNodeGraph(MapNodeGroup.GraphType.Ground) : null;
            float radius = Mathf.Min(12f, owner.ArenaRadius * .4f);
            if (graph != null)
                foreach (var node in graph.FindNodesInRangeWithFlagConditions(center, 6f, radius,
                    HullMask.Human, NodeFlags.None, NodeFlags.NoCharacterSpawn, preventOverhead: false))
                    if (graph.GetNodePosition(node, out var position) && !Physics.CheckCapsule(position+Vector3.up*.7f,position+Vector3.up*2,.6f,LayerIndex.world.mask|LayerIndex.CommonMasks.characterBodiesOrDefault)) positions.Add(position);
            if (positions.Count == 0)
            {
                var fallback=center+Vector3.forward*7;
                if(Physics.Raycast(fallback+Vector3.up*20,Vector3.down,out var ground,40,LayerIndex.world.mask))fallback=ground.point;
                positions.Add(fallback);
            }
            foreach (var position in positions.OrderBy(position => (position - center).sqrMagnitude).Take(32))
            {
                SpawnPoint.AddSpawnPoint(position, Util.QuaternionSafeLookRotation(position - center));
                var point = SpawnPoint.readOnlyInstancesList.LastOrDefault();
                if (point) point.gameObject.AddComponent<GauntletSpawnOwner>().Owner = owner;
            }
            GauntletPlugin.Log.LogInfo($"Arena ready: {positions.Count} nearby ground nodes; {SpawnPoint.readOnlyInstancesList.Count} player spawns within {radius:0}m of ward. Void fog disabled.");
        }

        private static void StopFog(InfiniteTowerRun run)
        {
            var fog = NativeAccess.Get<FogDamageController>(run, "fogDamageController");
            if (!fog) return;
            fog.SetActive(false);
            if (NetworkServer.active) NetworkServer.Destroy(fog.gameObject);
            else UnityEngine.Object.Destroy(fog.gameObject);
            NativeAccess.Set(run, "fogDamageController", null);
        }

        internal static void CleanUp(GauntletController owner)
        {
            owner.ArenaReady = false;owner.Encounter.Reset();owner.RetryWaveAt=-1;
            if (owner.ActiveBudget) owner.ActiveBudget.Retire();
            owner.ActiveBudget = null;
            if (owner.Run) StopFog(owner.Run);
            if (owner.ArenaWard)
            {
                if (NetworkServer.active) NetworkServer.Destroy(owner.ArenaWard.gameObject);
                else UnityEngine.Object.Destroy(owner.ArenaWard.gameObject);
                owner.ArenaWard = null;
            }
            foreach (var point in SpawnPoint.readOnlyInstancesList.ToArray())
            {
                var tag = point ? point.GetComponent<GauntletSpawnOwner>() : null;
                if (!tag || tag.Owner != owner) continue;
                point.enabled = false;
                UnityEngine.Object.Destroy(point.gameObject);
            }
        }

        private static void DestroyRun(On.RoR2.InfiniteTowerRun.orig_OnDestroy original, InfiniteTowerRun run)
        {
            var owner = GauntletMode.IsActive(run) ? run.GetComponent<GauntletController>() : null;
            if (owner)
            {
                owner.ShuttingDown = true;
                owner.Fortress.Detach();
                foreach(var field in owner.GetComponents<FortressBattlefield>())field.Release();
                CleanUp(owner);
                GauntletPlugin.Log.LogInfo("Gauntlet run teardown: retired arena spawns, ward and fog before restart.");
            }
            original(run);
        }
    }

    internal sealed class GauntletSpawnOwner : MonoBehaviour
    {
        internal GauntletController Owner;
    }
}
