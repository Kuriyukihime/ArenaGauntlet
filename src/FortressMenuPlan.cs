using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarGauntlet
{
    internal static class FortressMenuPlan
    {
        internal static readonly string[] Branches={"Defense","Weapons","Market","Workshop","Sanctum","Drones"};
        internal static string Summary(string branch)
        {
            switch(branch)
            {
                case "Defense":return "Keep, armor & ramparts";
                case "Weapons":return "Repeaters, artillery & frost";
                case "Market":return "Item supplies & choices";
                case "Workshop":return "Scrapping, printers & forge";
                case "Sanctum":return "Lunar services & garrison";
                case "Drones":return "Hangar & drone services";
                default:return "Castle upgrades";
            }
        }
        // Traverse parents before children without changing gameplay prerequisites.
        internal static FortressNode[] Ordered(string branch)
        {
            var nodes=FortressTree.Nodes.Where(node=>node.Branch==branch).ToArray();
            var result=new List<FortressNode>();
            Action<FortressNode> visit=null;
            visit=node=>
            {
                if(result.Contains(node))return;
                result.Add(node);
                foreach(var child in nodes.Where(value=>value.Parent==node.Id))visit(child);
            };
            foreach(var node in nodes.Where(value=>!nodes.Any(parent=>parent.Id==value.Parent)))visit(node);
            foreach(var node in nodes)visit(node);
            return result.ToArray();
        }
        internal static int Depth(FortressNode node)
        {
            int depth=0;var parent=FortressTree.Node(node.Parent);
            while(parent!=null && parent.Branch==node.Branch && depth<FortressTree.Nodes.Length)
            {depth++;parent=FortressTree.Node(parent.Parent);}
            return depth;
        }
        internal static int[] Damaged(FortressState state)=>Enumerable.Range(0,FortressTree.ModuleCount)
            .Where(state.NeedsRepair).OrderBy(index=>index==0?0:state.Modules[index].Destroyed?1:2)
            .ThenBy(index=>state.Modules[index].Maximum>0?state.Modules[index].Health/state.Modules[index].Maximum:0).ToArray();
    }
}
