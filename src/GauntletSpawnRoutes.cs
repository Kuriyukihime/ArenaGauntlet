using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.Navigation;
using UnityEngine;

namespace BazaarGauntlet
{
    internal sealed class GauntletSpawnRoutes
    {
        private readonly GauntletController owner;
        private readonly Dictionary<HullClassification,bool[]> reachable=new Dictionary<HullClassification,bool[]>();
        private readonly HashSet<string> warned=new HashSet<string>();
        private NodeGraph graph;
        private int nodeCount;
        internal GauntletSpawnRoutes(GauntletController owner){this.owner=owner;}
        internal bool Place(SpawnCard card,out DirectorPlacementRule placement)
        {
            placement=null;if(!card || !SceneInfo.instance)return false;
            Vector3 center=owner.Fortress.Center;float maximum=owner.ArenaRadius,minimum=Mathf.Min(80,Mathf.Max(30,maximum-10));
            if(card.nodeGraphType!=MapNodeGroup.GraphType.Ground)
            {
                placement=new DirectorPlacementRule{placementMode=DirectorPlacementRule.PlacementMode.Approximate,position=center,minDistance=minimum,maxDistance=maximum};
                return true;
            }
            var current=SceneInfo.instance.GetNodeGraph(MapNodeGroup.GraphType.Ground);if(!current)return false;
            if(current!=graph || current.GetNodeCount()!=nodeCount){graph=current;nodeCount=graph.GetNodeCount();reachable.Clear();}
            if(!reachable.TryGetValue(card.hullSize,out var reaches))
            {
                var edges=new List<(int From,int To)>();
                foreach(var node in graph.GetActiveNodesForHullMask((HullMask)(1<<(int)card.hullSize)))
                    foreach(var link in graph.GetActiveNodeLinks(node))
                        if(graph.IsLinkSuitableForHull(link,card.hullSize) && !graph.GetLinkNeedsJump(link,(HullMask)(1<<(int)card.hullSize)))
                            edges.Add((node.nodeIndex,graph.GetLinkEndNode(link).nodeIndex));
                var goal=graph.FindClosestNode(center,card.hullSize);
                reaches=GauntletRoutePlan.Reaches(nodeCount,goal.nodeIndex,edges);reachable[card.hullSize]=reaches;
            }
            var choices=graph.FindNodesInRangeWithFlagConditions(center,minimum,maximum,(HullMask)(1<<(int)card.hullSize),card.requiredFlags,card.forbiddenFlags,false)
                .Where(node=>node.nodeIndex>=0 && node.nodeIndex<reaches.Length && reaches[node.nodeIndex]).ToList();
            // Prefer the lower approaches, so ground enemies ascend to the keep.
            var lower=choices.Where(node=>graph.GetNodePosition(node,out var point) && point.y<center.y-8).ToList();
            if(lower.Count>0)choices=lower;
            var players=PlayerCharacterMasterController.instances.Where(player=>player && player.isConnected && player.master)
                .Select(player=>player.master.GetBody()).Where(body=>body && body.healthComponent && body.healthComponent.alive).ToArray();
            var safe=choices.Where(node=>graph.GetNodePosition(node,out var point) && players.All(player=>(player.corePosition-point).sqrMagnitude>=30*30)).ToList();
            if(safe.Count>0)choices=safe;
            if(choices.Count==0)
            {
                if(warned.Add(card.name))GauntletPlugin.Log.LogWarning("No distant connected approach for "+card.name+" / "+card.hullSize+". Retrying without a close or disconnected spawn.");
                return false;
            }
            var chosen=choices[owner.ArenaRng.RangeInt(0,choices.Count)];graph.GetNodePosition(chosen,out var position);
            // Retain native occupancy/clearance checks and spawn callbacks. A tiny
            // node search cannot wander onto a neighboring disconnected ledge.
            placement=new DirectorPlacementRule{placementMode=DirectorPlacementRule.PlacementMode.Approximate,position=position,minDistance=0,maxDistance=.1f};
            return true;
        }
    }
}
