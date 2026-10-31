using System;

namespace BazaarGauntlet
{
    internal enum FortressUpgrade
    {
        Masonry, Reinforcement, Ramparts, Ballista, Artillery, Frost, Market,
        Workshop, WhitePrinter, GreenPrinter, RedPrinter, Forge, Reforge,
        Sanctum, Cleansing, Order, Recycler, Hangar, DroneUpgrade, DroneCombiner,
        DroneScrapper, Archive, ArchiveGreen, Kitchen, Donation, GoldenVault, Count
    }
    internal sealed class FortressNode
    {
        internal FortressUpgrade Id, Parent;
        internal string Name, Branch, Description;
        internal int MaxRank, Cost, Round, ParentRank, Module;
        internal FortressNode(FortressUpgrade id, string name, string branch, string description, int max, int cost,
            int round, int module, FortressUpgrade parent = FortressUpgrade.Count, int parentRank = 1)
        { Id=id; Name=name; Branch=branch; Description=description; MaxRank=max; Cost=cost; Round=round; Module=module; Parent=parent; ParentRank=parentRank; }
    }
    internal static class FortressTree
    {
        internal const int ModuleCount = 16;
        internal static readonly string[] Modules = { "Heart of the Keep", "North rampart", "East rampart", "South rampart", "West rampart",
            "West ballista", "East ballista", "Siege battery", "Frost beacon", "Market hall", "Forge", "Workshop", "Lunar sanctum", "Drone hangar", "Archive", "Kitchen" };
        internal static readonly FortressNode[] Nodes = {
            new FortressNode(FortressUpgrade.Masonry,"Keep foundations","Defense","+25% structure health per rank. Rank 5 adds a second armored keep tier.",5,60,1,0),
            new FortressNode(FortressUpgrade.Reinforcement,"Ironclad keep","Defense","+10 armor per rank for all structures; visible reinforcing bands.",3,90,2,0,FortressUpgrade.Masonry,1),
            new FortressNode(FortressUpgrade.Ramparts,"Battlements","Defense","+35% rampart health per rank. Reinforce all four defensive approaches.",3,70,1,1),
            new FortressNode(FortressUpgrade.Ballista,"Repeater towers","Weapons","Upgrade tower damage and fire rate. Rank 2 adds the east tower; rank 3 gives both twin shots.",3,100,1,5),
            new FortressNode(FortressUpgrade.Artillery,"Siege battery","Weapons","Build long-range explosive artillery. Further ranks increase blast radius and damage.",3,160,3,7,FortressUpgrade.Ballista,2),
            new FortressNode(FortressUpgrade.Frost,"Frost beacon","Weapons","Slow enemies approaching the keep. Further ranks increase radius and pulse frequency.",3,120,3,8,FortressUpgrade.Ramparts,1),
            new FortressNode(FortressUpgrade.Market,"Market hall","Market","Unlock a restocking item market. Ranks expand it from 3 to 6 to 8 stalls, using your BiggerBazaar rarity settings when available.",3,75,1,9),
            new FortressNode(FortressUpgrade.Workshop,"Scrapping workshop","Workshop","Build a native scrapper. It refreshes each intermission.",1,60,1,11),
            new FortressNode(FortressUpgrade.WhitePrinter,"Common foundry","Workshop","Add a common 3D printer with a new item each round.",1,100,2,11,FortressUpgrade.Workshop),
            new FortressNode(FortressUpgrade.GreenPrinter,"Uncommon foundry","Workshop","Add an uncommon 3D printer.",1,180,4,11,FortressUpgrade.WhitePrinter),
            new FortressNode(FortressUpgrade.RedPrinter,"Legendary foundry","Workshop","Add a legendary printer. Native printer item trades still apply.",1,450,10,11,FortressUpgrade.GreenPrinter),
            new FortressNode(FortressUpgrade.Forge,"Alchemical forge","Workshop","Build the native white-to-green and green-to-red cauldrons.",1,140,3,10,FortressUpgrade.Workshop),
            new FortressNode(FortressUpgrade.Reforge,"Reforge annex","Workshop","Add Bazaar Expansion's green-to-three-whites trade, honoring its configured ingredient count.",1,160,4,10,FortressUpgrade.Forge),
            new FortressNode(FortressUpgrade.Sanctum,"Lunar sanctum","Sanctum","Unlock lunar item stalls, priced in run gold rather than persistent Lunar Coins.",1,150,3,12,FortressUpgrade.Market),
            new FortressNode(FortressUpgrade.Cleansing,"Cleansing pool","Sanctum","Build a native cleansing pool.",1,120,4,12,FortressUpgrade.Sanctum),
            new FortressNode(FortressUpgrade.Order,"Shrine of Order","Sanctum","Build a Shrine of Order. Its currency price is converted to run gold.",1,250,7,12,FortressUpgrade.Cleansing),
            new FortressNode(FortressUpgrade.Recycler,"Market recycler","Market","Unlock one paid market reroll each intermission, through the management menu.",1,140,3,9,FortressUpgrade.Market,2),
            new FortressNode(FortressUpgrade.Hangar,"Drone hangar","Drones","Build a drone shop. Available drone services require the enabled Alloyed Collective expansion.",1,120,2,13,FortressUpgrade.Market),
            new FortressNode(FortressUpgrade.DroneUpgrade,"Drone assembly","Drones","Add the native Drone Upgrade Station.",1,200,5,13,FortressUpgrade.Hangar),
            new FortressNode(FortressUpgrade.DroneCombiner,"Drone fusion","Drones","Add the native Drone Combiner Station.",1,160,4,13,FortressUpgrade.Hangar),
            new FortressNode(FortressUpgrade.DroneScrapper,"Drone salvage","Drones","Add the native Drone Scrapper.",1,100,3,13,FortressUpgrade.Hangar),
            new FortressNode(FortressUpgrade.Archive,"Command archive","Market","Unlock one paid common item selection per player each round. Choose through the castle menu.",1,250,5,14,FortressUpgrade.Market,2),
            new FortressNode(FortressUpgrade.ArchiveGreen,"Uncommon archive","Market","Also allow one paid uncommon selection per player each round.",1,400,9,14,FortressUpgrade.Archive),
            new FortressNode(FortressUpgrade.Kitchen,"Wandering CHEF kitchen","Workshop","Build the native recipe station when Alloyed Collective is enabled.",1,250,6,15,FortressUpgrade.Forge),
            new FortressNode(FortressUpgrade.Donation,"Garrison endowment","Sanctum","Unlock one gold-funded emergency garrison repair per intermission. Does not resurrect destroyed features.",1,200,5,12,FortressUpgrade.Sanctum),
            new FortressNode(FortressUpgrade.GoldenVault,"Legendary vault","Market","Add a golden chest, refreshed between rounds. Its purchase price is paid in run gold.",1,500,8,9,FortressUpgrade.Market,3)
        };
        internal static FortressNode Node(FortressUpgrade id) => (int)id >= 0 && (int)id < Nodes.Length ? Nodes[(int)id] : null;
    }
    [Serializable]
    internal sealed class FortressModuleState
    {
        public bool Built, Destroyed;
        public float Health, Maximum=0, GroundOffset=0;
    }
    [Serializable]
    internal sealed class FortressState
    {
        public int Revision;
        public bool Placed, Preparing=true;
        public string Map;
        public float X, Y, Z, Yaw;
        public int[] Ranks = new int[(int)FortressUpgrade.Count];
        public FortressModuleState[] Modules = new FortressModuleState[FortressTree.ModuleCount];
        public int Restock, Rerolls, Endowments;
        public uint[] ShopPlayers=Array.Empty<uint>();
        public int[] ShopBought=Array.Empty<int>();
        public FortressState()
        {
            for(int i=0;i<Modules.Length;i++) Modules[i]=new FortressModuleState();
            Ranks[(int)FortressUpgrade.Ballista]=1;
            for(int i=0;i<=5;i++) Modules[i].Built=true;
        }
        internal int Rank(FortressUpgrade id) => (int)id>=0 && (int)id<Ranks.Length ? Ranks[(int)id] : 0;
        internal bool Built(int module)
        {
            if(module<=5) return true;
            if(module==6) return Rank(FortressUpgrade.Ballista)>=2;
            foreach(var node in FortressTree.Nodes) if(node.Module==module && Rank(node.Id)>0) return true;
            return false;
        }
        internal string UpgradeBlock(FortressUpgrade id, int round, bool allowed)
        {
            var node=FortressTree.Node(id);
            if(node==null) return "Unknown upgrade.";
            if(!allowed) return "Build between rounds.";
            if(Rank(id)>=node.MaxRank) return "Fully upgraded.";
            int requiredRound=node.Round+2*Rank(id);
            if(round<requiredRound) return "Unlocks on round "+requiredRound+".";
            if(node.Parent!=FortressUpgrade.Count && Rank(node.Parent)<node.ParentRank) return "Requires "+FortressTree.Node(node.Parent).Name+" rank "+node.ParentRank+".";
            if(Modules[node.Module].Destroyed) return "Rebuild this feature first.";
            return null;
        }
        internal bool Upgrade(FortressUpgrade id, int round, bool allowed, uint wallet, int price, out uint remaining)
        {
            remaining=wallet;
            if(price<0 || wallet<(uint)price || UpgradeBlock(id,round,allowed)!=null) return false;
            remaining-= (uint)price; Ranks[(int)id]++; Revision++;
            for(int i=0;i<Modules.Length;i++) Modules[i].Built=Built(i);
            return true;
        }
        internal bool Repair(int module, bool allowed, uint wallet, int price, out uint remaining)
        {
            remaining=wallet;
            if(!allowed || module<0 || module>=Modules.Length || price<1 || wallet<(uint)price || !NeedsRepair(module)) return false;
            var target=Modules[module];
            if(!target.Built || target.Maximum<=0 || (!target.Destroyed && target.Health>=target.Maximum-.01f) || module==0 && target.Destroyed) return false;
            target.Destroyed=false; target.Health=target.Maximum;
            remaining-=(uint)price; Revision++; return true;
        }
        internal void Destroy(int module)
        {
            if(module<0 || module>=Modules.Length || Modules[module].Destroyed || !Modules[module].Built) return;
            Modules[module].Health=0; Modules[module].Destroyed=true; Revision++;
        }
        internal bool RepairAll(bool allowed,uint wallet,int[] prices,out uint remaining,out int repaired)
        {
            remaining=wallet;repaired=0;
            if(!allowed || prices==null || prices.Length!=Modules.Length)return false;
            ulong total=0;
            for(int i=0;i<Modules.Length;i++)if(NeedsRepair(i))
            {if(prices[i]<1)return false;total+=(uint)prices[i];repaired++;}
            if(repaired==0 || total>wallet){repaired=0;return false;}
            // Validate the whole bill before mutating anything. Concurrent
            // client requests still serialize through the server transaction.
            for(int i=0;i<Modules.Length;i++)if(NeedsRepair(i))
            {Modules[i].Destroyed=false;Modules[i].Health=Modules[i].Maximum;}
            remaining=wallet-(uint)total;Revision++;return true;
        }
        internal void NewIntermission() { Restock++; Rerolls=0; Endowments=0; Revision++; }
        internal static float MaxHealth(int module,int round,int players,FortressState state)
        {
            float basis=module==0?4000:module<=4?1000:module<=8?700:600;
            float rampart=module>=1 && module<=4?1+.35f*state.Rank(FortressUpgrade.Ramparts):1;
            return basis*(1+.14f*(Math.Max(1,round)-1))*(.5f+.5f*Math.Max(1,players))*(1+.25f*state.Rank(FortressUpgrade.Masonry))*rampart;
        }
        internal int UpgradeBaseCost(FortressUpgrade id) => FortressTree.Node(id).Cost*(1+Rank(id));
        internal bool NeedsRepair(int module)
        {
            if(module<0 || module>=Modules.Length)return false;
            var state=Modules[module];
            return state.Built && state.Maximum>0 && !(module==0 && state.Destroyed) &&
                (state.Destroyed || state.Health<state.Maximum-.01f);
        }
        internal static int FullRepairBaseCost(int module)=>module==0?120:module<=4?35:70;
        internal static int RebuildBaseCost(int module)=>(int)Math.Ceiling(FullRepairBaseCost(module)*1.25);
        // Scale the full price for difficulty BEFORE applying the missing-health
        // fraction. This avoids the old ten-gold floor and double rounding.
        internal int RepairCost(int module,int fullRepairPrice,int rebuildPrice)
        {
            if(!NeedsRepair(module))return 0;
            var state=Modules[module];
            if(state.Destroyed)return Math.Max(1,rebuildPrice);
            double fraction=Math.Max(0,Math.Min(1,1-(double)state.Health/state.Maximum));
            return Math.Max(1,(int)Math.Ceiling(Math.Max(1,fullRepairPrice)*fraction));
        }
    }
}
