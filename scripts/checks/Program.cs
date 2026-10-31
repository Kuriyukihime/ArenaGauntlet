using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BazaarGauntlet;

internal static class Program
{
    private static int checks;
    private static void DirectedApproaches()
    {
        foreach(var layout in new[]{FortressBattlefieldGrid.Ground(),FortressBattlefieldGrid.Air()})
        {
            var links=new List<(int From,int To)>();
            for(int i=0;i<layout.Count;i++)
            {
                var point=layout.Position(i);
                Check(point.X*point.X+point.Z*point.Z<=(FortressBattlefieldGrid.HalfSize-12)*(FortressBattlefieldGrid.HalfSize-12),"Native hulls retain at least twelve metres of floor at the arena edge");
                Check(point.Y>0 && point.Y<60,"Ground and flying nodes stay above the new terrain, never on the original map underneath");
                foreach(int other in layout.Neighbors(i))
                {
                    Check(other>=0 && other<layout.Count && other!=i,"Navigation indices stay valid");
                    Check(layout.Neighbors(other).Contains(i),"Every battlefield connection is bidirectional");
                    links.Add((i,other));
                }
            }
            var reached=GauntletRoutePlan.Reaches(layout.Count,layout.Count/2,links);
            Check(reached.All(value=>value),"Every approach and air layer reaches the centre without jumps or disconnected islands");
            if(layout.Heights.Length==1)Check(Enumerable.Range(0,layout.Count).All(i=>layout.Position(i).Y==.06f),"Ground navigation is perfectly level with the physical floor");
            foreach(int hull in new[]{0,1,2})
                Check(Enumerable.Range(0,layout.Count).Count(i=>{var p=layout.Position(i);float d=p.X*p.X+p.Y*p.Y+p.Z*p.Z;return !layout.Edge(i) && d>=80*80 && d<=140*140;})>300,"Every native hull has abundant distant ground/air placements");
        }
        Check(FortressBattlefieldGrid.Ground().Count<3249 && FortressBattlefieldGrid.Air().Count<2187,"Circular arena excludes square-corner navigation outside its walls");
        var edges=new[]{(0,1),(1,2),(2,3),(3,4),(5,6)};
        var reaches=GauntletRoutePlan.Reaches(7,3,edges);
        Check(reaches.SequenceEqual(new[]{true,true,true,true,false,false,false}),"A downhill-only exit is not a route back up to the castle");
        Check(!GauntletRoutePlan.Reaches(7,-1,edges).Any(value=>value),"Missing castle anchor cannot authorize disconnected spawns");
        Check(GauntletRoutePlan.Reaches(0,-1,Array.Empty<(int,int)>()).Length==0,"Empty graph safely rejects route");
        var random=new Random(700);
        for(int scenario=0;scenario<150;scenario++)
        {
            int count=random.Next(4,32),goal=random.Next(count);var links=new List<(int From,int To)>();
            for(int a=0;a<count;a++)for(int b=0;b<count;b++)if(random.Next(9)==0)links.Add((a,b));
            var actual=GauntletRoutePlan.Reaches(count,goal,links);
            for(int start=0;start<count;start++)
            {
                var visited=new HashSet<int>{start};var queue=new Queue<int>();queue.Enqueue(start);
                while(queue.Count>0)
                {int from=queue.Dequeue();foreach(var edge in links.Where(edge=>edge.From==from))if(visited.Add(edge.To))queue.Enqueue(edge.To);}
                Check(actual[start]==visited.Contains(goal),"Reverse approach selection agrees with a forward path through cycles, branches and disconnected islands");
            }
        }
    }
    private static void ManagementAndRepairs()
    {
        var all=FortressMenuPlan.Branches.SelectMany(FortressMenuPlan.Ordered).ToArray();
        Check(all.Length==FortressTree.Nodes.Length && all.Select(node=>node.Id).Distinct().Count()==all.Length,"Logical menu contains every upgrade exactly once");
        foreach(string branch in FortressMenuPlan.Branches)
        {
            var nodes=FortressMenuPlan.Ordered(branch);
            foreach(var node in nodes)
            {
                int parent=Array.FindIndex(nodes,value=>value.Id==node.Parent);
                Check(parent<0 || parent<Array.IndexOf(nodes,node),"Each prerequisite appears before its dependent upgrade");
                Check(FortressMenuPlan.Depth(node)>=0 && FortressMenuPlan.Depth(node)<FortressTree.Nodes.Length,"Menu path depth is bounded");
            }
        }
        var state=new FortressState();
        for(int i=0;i<state.Modules.Length;i++){state.Modules[i].Maximum=1000;state.Modules[i].Health=1000;}
        Check(FortressMenuPlan.Damaged(state).Length==0 && state.RepairCost(1,350,440)==0,"Healthy features have no repair fee and are hidden");
        state.Modules[1].Health=900;Check(state.RepairCost(1,35,44)==4,"Ten-percent wall damage costs four base gold, without the old ten-gold floor");
        state.Modules[1].Health=500;Check(state.RepairCost(1,35,44)==18 && state.RepairCost(1,350,440)==175,"Repair fraction applies after difficulty scaling, without double rounding");
        state.Modules[1].Health=100;Check(state.RepairCost(1,350,440)==315,"Ninety-percent damage costs ninety percent of the full repair price");
        int previous=0;
        for(int damage=1;damage<=1000;damage++)
        {
            state.Modules[1].Health=1000-damage;int cost=state.RepairCost(1,350,440);
            Check(cost>=previous && cost>=1 && cost<=350,"Damage-based repair prices are monotonic, positive and bounded by full repair");previous=cost;
        }
        state.Destroy(1);Check(state.RepairCost(1,350,440)==440,"Destroyed feature uses its separate rebuild fee");
        state.Modules[0].Health=950;state.Modules[2].Health=500;
        var damaged=FortressMenuPlan.Damaged(state);
        Check(damaged.SequenceEqual(new[]{0,1,2}),"Repair navigation prioritizes the keep, ruins and then damaged features; unbuilt/intact features are hidden");
        var prices=Enumerable.Range(0,state.Modules.Length).Select(i=>state.RepairCost(i,350,440)).ToArray();
        uint total=(uint)prices.Sum();int revision=state.Revision;
        Check(!state.RepairAll(true,total-1,prices,out uint remaining,out int count) && remaining==total-1 && count==0 && state.Revision==revision && state.Modules[1].Destroyed && state.Modules[2].Health==500,"Unaffordable Repair All is atomic and charges nothing");
        Check(!state.RepairAll(false,total,prices,out remaining,out count) && remaining==total && state.Revision==revision,"Combat blocks Repair All before any changes");
        int old=prices[2];prices[2]=0;
        Check(!state.RepairAll(true,total,prices,out remaining,out count) && remaining==total && state.Modules[1].Destroyed,"Invalid repair quote cannot partially repair or charge");prices[2]=old;
        Check(state.RepairAll(true,total,prices,out remaining,out count) && remaining==0 && count==3 && !state.Modules[1].Destroyed && state.Modules[0].Health==1000 && state.Modules[2].Health==1000 && state.Revision==revision+1,"Repair All charges the exact total once and restores all eligible features");
        Check(!state.RepairAll(true,total,prices,out remaining,out count) && remaining==total && count==0,"Repeated Repair All cannot double-charge an intact castle");
        state.Destroy(0);Check(!state.NeedsRepair(0) && state.RepairCost(0,120,150)==0,"Destroyed core is never offered as a repair target");
        Check(!state.NeedsRepair(-1) && !state.NeedsRepair(state.Modules.Length),"Invalid repair indices are safely rejected");
        var overflow=new FortressState();for(int i=0;i<overflow.Modules.Length;i++){overflow.Modules[i].Maximum=100;overflow.Modules[i].Health=0;}
        var huge=Enumerable.Repeat(int.MaxValue,overflow.Modules.Length).ToArray();
        Check(!overflow.RepairAll(true,uint.MaxValue,huge,out remaining,out count) && remaining==uint.MaxValue && overflow.Modules[1].Health==0,"Large multi-repair totals cannot overflow into a cheap or free bill");
    }
    private static void HordeVarietyAndRoles()
    {
        var mix=new GauntletHordeMix(new[]{1,1,1,1});bool[] available={true,true,true,true};int[] costs={10,20,25,40};int last=-1;
        var seen=new int[4];
        for(int i=0;i<400;i++)
        {
            int index=mix.Pick(available,costs,100,i*97);
            Check(index>=0 && index<4 && index!=last,"Mixed horde rotates successful species instead of repeating the same batch");
            // Simulate the actual two-enemy native series.
            mix.Record(index);mix.Record(index);seen[index]+=2;last=index;
        }
        Check(seen.All(value=>value==200),"Equal-weight affordable bodies are represented evenly over complete rotations");
        var cheap=new GauntletHordeMix(new[]{1,1,1});
        bool[] three={true,true,true};int[] prices={10,20,100};
        for(int i=0;i<40;i++)
        {int index=cheap.Pick(three,prices,25,i);Check(index<2,"Rotation never forces an unaffordable species or invents director credits");cheap.Record(index);}
        int expensive=cheap.Pick(three,prices,150,1);Check(expensive==2,"An underrepresented costly enemy joins once the real budget can afford it");
        var failed=new GauntletHordeMix(new[]{1,1,1});int first=failed.Pick(three,prices,150,7);
        int next=failed.Pick(three,prices,150,7);Check(next!=first,"A failed placement receives retry backoff instead of monopolizing the wave");
        failed.Record(next);Check(failed.Pick(new[]{false,false,true},prices,150,3)==2,"A single valid species remains spawnable without a diversity deadlock");
        Check(failed.Pick(new[]{false,false,false},prices,150,3)==-1,"Empty native availability falls back safely");
        var waiting=new GauntletHordeMix(new[]{1,1,1});Check(waiting.Pick(three,prices,0,6)==0,"An empty budget waits for its cheapest eligible card rather than an expensive one");
        Check(GauntletEnemyRoles.Classify(true,false,1000,7,true)==FortressEnemyRole.Commander,"Champions lead the assault regardless of weapon traits");
        Check(GauntletEnemyRoles.Classify(false,false,800,5,true)==FortressEnemyRole.Breaker,"Durable grounded enemies break defenses even when ranged");
        Check(GauntletEnemyRoles.Classify(false,true,600,8,true)==FortressEnemyRole.Saboteur,"Ranged flyers attack outer facilities");
        Check(GauntletEnemyRoles.Classify(false,true,600,8,false)==FortressEnemyRole.Raider,"Melee flyers raid defenders rather than being assigned a ranged role");
        Check(GauntletEnemyRoles.Classify(false,false,300,10,false)==FortressEnemyRole.Raider,"Fast melee enemies pressure defenders");
        Check(GauntletEnemyRoles.Classify(false,false,100,6,false)==FortressEnemyRole.Raider,"Small melee enemies act as raiders");
        Check(GauntletEnemyRoles.Classify(false,false,400,7,false)==FortressEnemyRole.Assault,"Other eligible melee species still receive an assault role");
    }
    private static void Architecture()
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        FortressArchitectureData data;
        using(var stream=File.OpenRead(Path.Combine(root,"assets/castle.meshbin")))data=FortressArchitectureData.Read(stream);
        Check(data.Parts.Length>100,"The castle uses the authored architectural library, not primitive stand-ins");
        var appearanceState=new FortressState();ulong originalAppearance=FortressArchitectureData.AppearanceKey(appearanceState);
        appearanceState.Revision++;appearanceState.NewIntermission();appearanceState.Modules[0].Health=50;
        Check(FortressArchitectureData.AppearanceKey(appearanceState)==originalAppearance,"Health and shop/round updates do not rebuild the castle mesh");
        appearanceState.Ranks[(int)FortressUpgrade.Masonry]++;
        Check(FortressArchitectureData.AppearanceKey(appearanceState)!=originalAppearance,"A visible upgrade invalidates the architecture cache");
        foreach(var surface in data.Surfaces)
            Check(File.Exists(Path.Combine(root,"assets/castle_"+surface.Name+".png")),"Each architectural material has its packaged surface");
        foreach(var node in FortressTree.Nodes)
        {
            var state=new FortressState();var prerequisite=node;
            while(prerequisite.Parent!=FortressUpgrade.Count)
            {
                state.Ranks[(int)prerequisite.Parent]=Math.Max(state.Rank(prerequisite.Parent),prerequisite.ParentRank);
                prerequisite=FortressTree.Node(prerequisite.Parent);
            }
            state.Ranks[(int)node.Id]=0;
            for(int rank=1;rank<=node.MaxRank;rank++)
            {
                int before=data.Parts.Where(part=>part.Visible(state)).Sum(part=>part.Indices.Length);
                Check(state.Upgrade(node.Id,20,true,uint.MaxValue,state.UpgradeBaseCost(node.Id),out _),"Visual rank uses a real valid castle purchase");
                int after=data.Parts.Where(part=>part.Visible(state)).Sum(part=>part.Indices.Length);
                Check(after>before,"Every purchased rank adds actual visible castle geometry: "+node.Name+" "+rank);
            }
        }
        var full=new FortressState();foreach(var node in FortressTree.Nodes)full.Ranks[(int)node.Id]=node.MaxRank;
        for(int i=0;i<FortressTree.ModuleCount;i++)
        {
            full.Modules[i].Built=full.Built(i);full.Modules[i].Maximum=100;full.Modules[i].Health=100;
            int live=data.Parts.Count(part=>part.Module==i && part.Visible(full));
            Check(live>0,"Every fixed module has authored live geometry");
            full.Destroy(i);
            Check(data.Parts.Any(part=>part.Module==i && part.Ruin && part.Visible(full)),"Every destroyed module has authored ruins");
            Check(!data.Parts.Any(part=>part.Module==i && !part.Ruin && part.Visible(full)),"Ruins cannot show intact upgraded geometry");
            if(i==0)continue;
            Check(full.Repair(i,true,uint.MaxValue,50,out _),"Purchased rebuild succeeds for visual persistence check");
            Check(data.Parts.Count(part=>part.Module==i && part.Visible(full))==live,"Rebuild returns the exact upgraded architectural selection");
        }
    }
    private static void Check(bool value, string message)
    {
        checks++; if (!value) throw new Exception(message);
    }
    private static void PlayCycle(GauntletFlow flow)
    {
        Check(flow.ArenaLoaded(),"Arena load begins the gladiator cycle");
        for (int round = 1; round <= GauntletPlan.RoundsPerCycle; round++)
        {
            Check(flow.Round == round, "Castle round progression remains ordered");
            Check(!flow.EnterBazaar(), "Cannot advance before clearing the round");
            Check(!flow.ArenaLoaded(), "Combat cannot accept a duplicate arena arrival");
            Check(!flow.BossCleared(), "Boss cannot be completed during the horde");
            Check(flow.HordeCleared(), "Horde unlocks the boss");
            Check(!flow.HordeCleared(), "Duplicate horde clear cannot skip the boss");
            var expected = round == GauntletPlan.RoundsPerCycle ? GauntletBossKind.Final : round % 5 == 0 ? GauntletBossKind.Great : GauntletBossKind.Ordinary;
            Check(flow.BossKind == expected, "Correct ordinary/great/final boss tier");
            Check(flow.BossCleared(), "Boss clears once");
            Check(!flow.BossCleared(), "Duplicate boss death cannot award another round");
            if (round == GauntletPlan.RoundsPerCycle)
            {
                Check(flow.Phase == GauntletPhase.FinalDecision, "Final boss offers explicit completion branch");
                Check(!flow.EnterBazaar(), "Final boss cannot silently advance or reset the run");
                continue;
            }
            Check(!flow.ReturnToArena(),"Cannot skip the shopping trip before entering the Bazaar");
            Check(flow.EnterBazaar(),"Cleared arena permits one Bazaar departure");
            Check(!flow.EnterBazaar() && !flow.BossCleared(),"Duplicate portal/clear cannot grant a second trip or reward");
            Check(flow.Round==round && flow.Phase==GauntletPhase.LoadingBazaar,"Shop departure does not skip a round");
            Check(flow.BazaarLoaded() && !flow.BazaarLoaded(),"Only one shop arrival is accepted");
            Check(!flow.HordeCleared() && !flow.ArenaLoaded(),"Shopping cannot start combat or skip the return portal");
            Check(flow.ReturnToArena(),"Return portal advances one round");
            Check(!flow.ReturnToArena(),"Duplicate portal requests cannot advance twice");
            Check(flow.Phase==GauntletPhase.LoadingArena && flow.Round==round+1,"Next round remains pending during scene loading");
            Check(flow.ArenaLoaded(),"Arena rebuild resumes the next horde");
        }
    }
    private static void Castle()
    {
        Check(FortressTree.Nodes.Length==(int)FortressUpgrade.Count,"Every upgrade has exactly one definition");
        foreach(var node in FortressTree.Nodes)
        {
            Check(FortressTree.Node(node.Id)==node && (int)node.Id<Array.IndexOf(FortressTree.Nodes,node)+1,"Upgrade IDs retain a stable catalog order");
            Check(node.Module>=0 && node.Module<FortressTree.ModuleCount && node.MaxRank>0 && node.Cost>0,"Every feature has valid construction limits and price");
            var seen=new HashSet<FortressUpgrade>();var current=node;
            while(current.Parent!=FortressUpgrade.Count)
            {Check(seen.Add(current.Id),"No cyclic upgrade prerequisite can strand a path");current=FortressTree.Node(current.Parent);Check(current!=null,"All prerequisites exist");}
        }
        var state=new FortressState {Placed=true,Map="test-stage",X=100,Y=10,Z=50,Yaw=90};state.Modules[0].GroundOffset=0;uint wallet=50000;
        Check(state.Modules[0].Built && state.Modules[1].Built && !state.Modules[9].Built,"Initial keep has defenses but paid market utilities remain locked");
        Check(!state.Upgrade(FortressUpgrade.Artillery,20,true,wallet,160,out uint unchanged) && unchanged==wallet,"Missing weapon prerequisites never spend gold");
        Check(!state.Upgrade(FortressUpgrade.Masonry,1,false,wallet,60,out unchanged) && unchanged==wallet,"Combat rejects construction without spending");
        Check(!state.Upgrade(FortressUpgrade.Masonry,1,true,10,60,out unchanged) && unchanged==10,"Unaffordable construction leaves the wallet untouched");
        Check(!state.Upgrade(FortressUpgrade.Masonry,1,true,wallet,-1,out unchanged),"Negative prices cannot create gold");
        var tierGate=new FortressState();Check(tierGate.Upgrade(FortressUpgrade.Masonry,1,true,wallet,60,out unchanged),"Opening durability rank is available");
        Check(tierGate.UpgradeBlock(FortressUpgrade.Masonry,1,true)!=null && tierGate.UpgradeBlock(FortressUpgrade.Masonry,3,true)==null,"Higher durability ranks require later rounds even when the player is rich");
        foreach(var node in FortressTree.Nodes)
            while(state.Rank(node.Id)<node.MaxRank)
            {
                int before=state.Rank(node.Id),revision=state.Revision,price=state.UpgradeBaseCost(node.Id);
                Check(state.Upgrade(node.Id,20,true,wallet,price,out uint remaining),"All valid specialized paths can be completed");
                Check(remaining==wallet-(uint)price && state.Rank(node.Id)==before+1 && state.Revision==revision+1,"An accepted purchase charges once and applies one rank");
                wallet=remaining;
            }
        int maxed=state.Revision;
        Check(!state.Upgrade(FortressUpgrade.Masonry,20,true,wallet,60,out unchanged) && unchanged==wallet && state.Revision==maxed,"Maxed upgrades cannot charge again");
        for(int module=0;module<FortressTree.ModuleCount;module++)
        {
            var data=state.Modules[module];data.Maximum=FortressState.MaxHealth(module,10,1,state);data.Health=data.Maximum*.25f;
            Check(data.Built,"Every construction path unlocks its corresponding fixed feature");
            if(module==0)continue;
            int[] ranks=(int[])state.Ranks.Clone();state.Destroy(module);int revision=state.Revision;state.Destroy(module);
            Check(state.Revision==revision && data.Destroyed && data.Health==0,"Repeated death reports preserve one ruined feature");
            Check(!state.Repair(module,false,wallet,50,out unchanged) && unchanged==wallet,"Combat cannot resurrect a destroyed feature");
            Check(state.Repair(module,true,wallet,50,out uint remaining) && remaining==wallet-50 && !data.Destroyed && data.Health==data.Maximum,"Rebuild restores exactly the original feature for one payment");
            Check(string.Join(",",ranks)==string.Join(",",state.Ranks),"Rebuilding retains every purchased upgrade");
            Check(!state.Repair(module,true,wallet,50,out unchanged) && unchanged==wallet,"Full-health features cannot be repaired for another charge");
        }
        state.Destroy(0);Check(!state.Repair(0,true,wallet,100,out unchanged),"Destroyed core cannot be bought back after the loss condition");
        var early=new FortressState();
        Check(early.UpgradeBlock(FortressUpgrade.RedPrinter,1,true)!=null,"Late-game utility paths are not purchasable at the opening");
        for(int module=0;module<FortressTree.ModuleCount;module++)
        {
            float hp=FortressState.MaxHealth(module,20,1,early);
            Check(FortressState.MaxHealth(module,21,1,early)>=hp,"Continuing beyond twenty rounds does not shrink the castle");
            Check(FortressState.MaxHealth(module,20,4,early)>hp,"Structure durability scales with party size");
        }
        early.Rerolls=1;early.Endowments=1;early.NewIntermission();
        Check(early.Rerolls==0 && early.Endowments==0 && early.Restock==1,"Intermissions renew limited service availability once");
    }
    private static void EncounterFailures()
    {
        // Production ownership gate, with Unity-independent object identities.
        var gate=new GauntletEncounterGate<object>();var first=new object();var stale=new object();var retry=new object();
        Check(!gate.TryBegin(1,1,GauntletPhase.Boss) && !gate.TryBegin(1,1,GauntletPhase.LoadingArena),"Neither a boss nor preparation may start the first encounter");
        Check(gate.TryBegin(1,1,GauntletPhase.Horde) && gate.NativeIndex==1,"First horde reserves native index one");
        Check(!gate.TryBegin(1,1,GauntletPhase.Horde) && !gate.TryBegin(1,1,GauntletPhase.Boss),"Duplicate native start callbacks cannot create a second wave while pending");
        Check(gate.Attach(first) && !gate.Attach(stale),"Only the reserved native wave can be attached");
        Check(!gate.Clear(first) && !gate.Commit(stale),"A half-initialized wave cannot progress or steal ownership");
        Check(gate.Fail(first) && gate.CanRetry && !gate.Committed && !gate.Clear(first),"The reported setup exception rolls back the incomplete wave without crediting a clear");
        Check(gate.TryBegin(1,1,GauntletPhase.Horde) && gate.NativeIndex==1,"Retry keeps native parity at the same horde slot instead of consuming the boss slot");
        Check(gate.Attach(retry) && gate.Commit(retry) && !gate.Commit(retry),"Defeat subscription setup commits exactly once");
        Check(!gate.TryBegin(1,1,GauntletPhase.Horde) && !gate.Clear(first),"Old callbacks cannot initialize another horde or clear the retry");
        Check(gate.Clear(retry) && !gate.Clear(retry),"The committed quota can clear once");
        Check(!gate.TryBegin(1,2,GauntletPhase.Horde) && !gate.TryBegin(1,1,GauntletPhase.Horde),"A horde clear cannot skip its boss or start another horde");
        Check(gate.TryBegin(1,1,GauntletPhase.Boss) && gate.NativeIndex==2,"The next valid encounter is the one boss");
        var boss=new object();Check(gate.Attach(boss) && gate.Fail(boss),"Boss setup can also fail safely");
        Check(gate.TryBegin(1,1,GauntletPhase.Boss) && gate.NativeIndex==2,"Boss retry cannot turn into a new horde");
        var bossRetry=new object();Check(gate.Attach(bossRetry) && gate.Commit(bossRetry) && gate.Clear(bossRetry),"A successful boss retry clears normally");
        Check(!gate.TryBegin(1,1,GauntletPhase.Boss) && !gate.Fail(bossRetry),"A cleared boss cannot be retried or rewarded twice");
        gate.Reset();Check(!gate.Clear(bossRetry) && gate.TryBegin(2,1,GauntletPhase.Horde) && gate.NativeIndex==21,"Bazaar arrival/restart resets ownership while maintaining native cycle parity");
        gate.Reset();Check(gate.TryBegin(1,1,GauntletPhase.Horde) && gate.Fail(null),"A factory failure before attachment can retry without inventing a wave");
        Check(!gate.Fail(null),"Repeated failure callbacks cannot reschedule an already aborted attempt");
        foreach(string name in new[]{"BazaarGauntletOpening_cscLemurian","BazaarGauntlet_RoR2.BaseContent","BazaarGauntlet_cscFalseSon"})
            Check(!GauntletCatalogueRules.ExternalSource(name),"Deferred-destroy owned assets are excluded even while Unity still reports them alive");
        foreach(string name in new[]{"cscLemurian","dccsHallowedConcepts","ModdedEnemyDeck"})Check(GauntletCatalogueRules.ExternalSource(name),"Native and registered mod sources remain eligible");
        Check(GauntletBossPolicy.Standalone("cscReaper","ReaperBody",new[]{"EntityStates.Reaper.SpawnState","EntityStates.GenericCharacterMain"}),"The roaming Anima Harvester remains available");
        foreach(string name in new[]{"cscReaperBoss","cscReaperBossMalachite","Mod_ReaperBoss(Clone)","cscChampRushReaper"})
            Check(!GauntletBossPolicy.Standalone(name,"ReaperBody",Array.Empty<string>()),"Mission-only Harvester cards cannot enter either arena pool");
        foreach(string name in new[]{"ReaperBossBody","ReaperBossMalachiteBody","ReaperBossSoulBody"})
            Check(!GauntletBossPolicy.Standalone("RenamedCard",name,null),"Body identity protects renamed mission cards");
        foreach(string state in new[]{"EntityStates.ChampRushReaper.SpawnState","EntityStates.ChampRushReaper.ReaperSecondPhaseSpawnTransition, RoR2","EntityStates.ChampRushReaper.PhaseTransitionSpawnState"})
            Check(!GauntletBossPolicy.Standalone("RenamedCard","RenamedBody",new[]{"EntityStates.Idle",state}),"Scripted state identity protects fully renamed mission variants");
        foreach(string name in new[]{"cscLemurian","cscTitan","cscRoboBallBoss","ModdedChampion"})
            Check(GauntletBossPolicy.Standalone(name,name,new string[]{null,"EntityStates.GenericCharacterMain"}),"Unrelated native/modded enemies retain eligibility");
        // Replay many rapid restarts and injected initialization failures using
        // the actual flow/gate. No failed attempt is a defeat or a new round.
        for(int run=0;run<80;run++)
        {
            var flow=new GauntletFlow();flow.Start();flow.ArenaLoaded();gate.Reset();
            foreach(var phase in new[]{GauntletPhase.Horde,GauntletPhase.Boss})
            {
                for(int attempt=0;attempt<4;attempt++)
                {
                    Check(gate.TryBegin(flow.Cycle,flow.Round,phase),"The requested slot can start after a restart or recoverable setup failure");
                    var wave=new object();Check(gate.Attach(wave),"Attempt receives its unique native wave identity");
                    if(attempt<3){Check(gate.Fail(wave) && !gate.Clear(wave) && flow.Round==1 && flow.Phase==phase,"Setup failures cannot alter phase/round or grant clear credit");continue;}
                    Check(gate.Commit(wave) && gate.Clear(wave),"One successful attempt owns the clear");
                    Check(phase==GauntletPhase.Horde?flow.HordeCleared():flow.BossCleared(),"Only committed horde then boss clears advance the mode");
                    Check(!gate.Clear(wave),"Delayed duplicate clear is harmless");
                }
            }
            Check(flow.Phase==GauntletPhase.AwaitingExit && flow.EnterBazaar(),"After injected failures the round still acknowledges its boss and permits the blue portal");
        }
    }
    private static void Colosseum()
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        using var stream=File.OpenRead(Path.Combine(root,"assets/colosseum.meshbin"));
        var art=FortressArchitectureData.Read(stream);
        Check(art.Parts.Length<=16,"Detailed Greek architecture stays in bounded material batches, never per-column objects");
        Check(art.Surfaces.Select(s=>s.Name).OrderBy(s=>s).SequenceEqual(new[]{"basalt","grass","limestone","marble","paving","roofstone"}),"Arena uses the authored Greek stone palette and grass");
        int triangles=0;double grassArea=0;
        foreach(var part in art.Parts)
        {
            Check(part.Module==0 && part.Attachment==0 && part.Conditions.Length==0 && !part.Ruin,"Colosseum is permanent geometry, independent of retired castle upgrades");
            triangles+=part.Indices.Length/3;
            for(int i=0;i<part.Vertices.Length;i+=8)
            {
                float x=part.Vertices[i],y=part.Vertices[i+1],z=part.Vertices[i+2];
                Check(y>=-6.01f && y<=42 && x*x+z*z<=242*242,"Arena model fits its authored collision floor and boundary");
                Check(x*x+z*z>=168*168 || y<.06f,"Navigation/combat circle contains only flush markings; merchant architecture stays outside");
                float nx=part.Vertices[i+3],ny=part.Vertices[i+4],nz=part.Vertices[i+5];
                Check(Math.Abs(nx*nx+ny*ny+nz*nz-1)<.002,"Authored stone normals stay unit length");
                if(part.Name.StartsWith("Courtyard"))Check(y<=.025f,"Paving, inlays and garden edges stay flush and cannot become movement barriers");
            }
            for(int i=0;i<part.Indices.Length;i+=3)
            {
                int a=part.Indices[i]*8,b=part.Indices[i+1]*8,c=part.Indices[i+2]*8;
                double ux=part.Vertices[b]-part.Vertices[a],uy=part.Vertices[b+1]-part.Vertices[a+1],uz=part.Vertices[b+2]-part.Vertices[a+2];
                double vx=part.Vertices[c]-part.Vertices[a],vy=part.Vertices[c+1]-part.Vertices[a+1],vz=part.Vertices[c+2]-part.Vertices[a+2];
                double cx=uy*vz-uz*vy,cy=uz*vx-ux*vz,cz=ux*vy-uy*vx;
                Check(cx*cx+cy*cy+cz*cz>1e-12,"Arena triangles must have physical area after float serialization");
                if(art.Surfaces[part.Surface].Name=="grass" && cy>0)grassArea+=cy*.5;
            }
        }
        double grassFraction=grassArea/(Math.PI*179*179);
        Check(grassFraction>.15 && grassFraction<.25,"Grass is a minority of the stone courtyard, with useful visible planting");
        Check(triangles>200000 && triangles<420000,"Greek relief, merchant bays and fluted architecture retain a bounded static geometry budget");
        Console.WriteLine($"Colosseum: {triangles:N0} triangles in {art.Parts.Length} surface batches.");
    }
    // Identities and states read from the installed game's bundles (see
    // .build/scripted-boss-audit.json). Death states are part of the identity.
    private static void ScriptedBosses()
    {
        var rejected=new (string Card,string Body,string[] States)[]
        {
            ("cscFalseSonBoss","FalseSonBossBody",new[]{"EntityStates.FalseSonBoss.HeartSpawnState","EntityStates.FalseSonBoss.FalseSonBossGenericStateWithSwing","EntityStates.FalseSonBoss.CrystalDeathState"}),
            ("cscFalseSonBossLunarShard","FalseSonBossBodyLunarShard",new[]{"EntityStates.FalseSonBoss.Lightning1SpawnState","EntityStates.FalseSonBoss.BrokenCrystalDeathState"}),
            ("cscFalseSonBossBrokenLunarShard","FalseSonBossBodyBrokenLunarShard",new[]{"EntityStates.FalseSonBoss.Lightning2SpawnState","EntityStates.FalseSonBoss.SkyJumpDeathState"}),
            ("cscProvidence","ProvBody",new[]{"EntityStates.Providence.SpawnState","EntityStates.GenericCharacterMain","EntityStates.Providence.DeathState"}),
            ("cscProvidenceDesperation","ProvDesperationBody",new[]{"EntityStates.Providence.DesperationSpawnState","EntityStates.Providence.DeathState"}),
            ("cscProvidenceFinal","ProvFinalBody",new[]{"EntityStates.Providence.FinalSpawnState","EntityStates.Providence.DeathStateFinal"}),
            ("cscProvidenceStationary","ProvStationaryBody",new[]{"EntityStates.Providence.UltimaIdle","EntityStates.Providence.UltimaDestroyImmediate"}),
            ("cscProvidenceShadow","ProvidenceShadowCloneBody",new[]{"EntityStates.ProvidenceShadowClone.Intangible","EntityStates.ProvidenceShadowClone.ProvidenceShadowCloneDeath"}),
            ("cscGildedWurmLaser","GildedWurmLaserBody",new[]{"EntityStates.GenericCharacterMain","EntityStates.MagmaWorm.DeathState"}),
            ("cscGildedWurmOrb","GildedWurmOrbBody",new[]{"EntityStates.GenericCharacterMain","EntityStates.MagmaWorm.DeathState"}),
            ("cscSacrificeBody","SacrificeBody",new[]{"EntityStates.Wake.SpawnState","EntityStates.Wake.Death"}),
            ("cscSolusWing","SolusWingBody",new[]{"EntityStates.SolusWing.SpawnState","EntityStates.SolusWing.DeathState"}),
            ("cscSolusHeart","SolusHeartBody",new[]{"EntityStates.SolusHeart.SolusHeartMain","EntityStates.SolusHeart.Death.SolusHeartDeathSequence+DeathIdle"}),
            ("cscMiniVoidRaidCrabPhase1","MiniVoidRaidCrabBodyPhase1",new[]{"EntityStates.VoidRaidCrab.SpawnState","EntityStates.FlyState","EntityStates.VoidRaidCrab.EscapeDeath"}),
            ("cscMiniVoidRaidCrabPhase2","MiniVoidRaidCrabBodyPhase2",new[]{"EntityStates.VoidRaidCrab.SpawnState","EntityStates.FlyState","EntityStates.VoidRaidCrab.EscapeDeath"}),
            ("cscBrotherHurt","BrotherHurtBody",new[]{"EntityStates.BrotherMonster.SpellChannelEnterState","EntityStates.BrotherMonster.TrueDeathState"}),
            ("cscBrotherThrone","BrotherThroneBody",new[]{"EntityStates.BrotherMonster.Throne.Spawn","EntityStates.BrotherMonster.Throne.Death"}),
            ("cscBrotherPhantom","BrotherPhantomBody",new[]{"EntityStates.GenericCharacterMain","EntityStates.BrotherMonster.BrotherPhantom.DeathState"}),
            ("cscKarmaSpire","KarmaSpireBody",new[]{"EntityStates.BrotherMonster.KarmaSpire.SpawnState","EntityStates.BrotherMonster.KarmaSpire.DeathState"}),
            ("cscTitanGold","TitanGoldBody",new[]{"EntityStates.TitanMonster.SpawnState","EntityStates.TitanMonster.DeathStateGold"}),
            ("cscReaperBoss","ReaperBossBody",new[]{"EntityStates.ChampRushReaper.SpawnState","EntityStates.ChampRushReaper.PhaseTransitionDeathState"}),
            ("cscReaperMalachiteBoss","ReaperBossMalachiteBody",new[]{"EntityStates.ChampRushReaper.SpawnState"}),
            ("cscMimic","MimicBody",new[]{"EntityStates.Mimic.Chest.Spawn","EntityStates.Mimic.Monster.Idle","EntityStates.Mimic.Death"}),
            ("cscSolusAmalgamatorThruster","SolusAmalgamatorThrusterBody",new[]{"EntityStates.GenericCharacterMain","EntityStates.Thruster.DeathState"}),
            ("cscSolusAmalgamatorMissilePod","SolusAmalgamatorMissilePodBody",new[]{"EntityStates.FlyState","EntityStates.Thruster.DeathState"}),
            ("cscSolusAmalgamatorFlamethrowerCannon","SolusAmalgamatorFlamethrowerCannonBody",new[]{"EntityStates.FlyState","EntityStates.Thruster.DeathState"}),
            ("cscSolusMine","SolusMineBody",new[]{"EntityStates.SolusMine.SpawnState","EntityStates.SolusMine.Detonate"}),
            ("cscShrineDeath","Lynx2Body",new[]{"EntityStates.Lynx2Monster.SpawnState","EntityStates.Lynx2Monster.DeathState"}),
            ("cscFriendUnitQuestUnlock","FriendUnitBody",new[]{"EntityStates.DefectiveUnit.SpawnState"}),
        };
        foreach(var item in rejected)
        {
            Check(GauntletBossPolicy.Reason(item.Card,item.Body,item.States)!=null,"Scripted encounter stays out of both arena pools: "+item.Card);
            // Renamed modded copies are still caught by their native states.
            if(!item.Card.Contains("GildedWurm") && !item.Card.Contains("ShrineDeath") && !item.Card.Contains("QuestUnlock") && !item.Card.Contains("ReaperMalachite"))
                Check(!GauntletBossPolicy.Standalone("ModCopy","ModBody",item.States),"State identity catches a renamed copy: "+item.Card);
        }
        var accepted=new (string Card,string Body,string[] States)[]
        {
            ("cscBrother","BrotherBody",new[]{"EntityStates.BrotherMonster.SkySpawnState","EntityStates.GenericCharacterMain","EntityStates.BrotherMonster.SkyLeapDeathState"}),
            ("cscBrotherIT","ITBrotherBody",new[]{"EntityStates.BrotherMonster.SkySpawnState","EntityStates.GenericCharacterMain","EntityStates.BrotherMonster.SkyLeapDeathState"}),
            ("cscMiniVoidRaidCrabPhase3","MiniVoidRaidCrabBodyPhase3",new[]{"EntityStates.VoidRaidCrab.SpawnState","EntityStates.FlyState","EntityStates.VoidRaidCrab.DeathState"}),
            ("cscScavLunar","ScavLunar3Body",new[]{"EntityStates.ScavMonster.Sit","EntityStates.GenericCharacterMain","EntityStates.ScavMonster.DeathLunar"}),
            ("cscScavSanctum","ScavSanctum1Body",new[]{"EntityStates.ScavMonster.Sit","EntityStates.ScavMonster.DeathSanctum"}),
            ("cscSuperRoboBallBoss","SuperRoboBallBossBody",new[]{"EntityStates.RoboBallBoss.SpawnState","EntityStates.FlyState","EntityStates.RoboBallBoss.DeathState"}),
            ("cscSolusAmalgamator","SolusAmalgamatorBody",new[]{"EntityStates.SolusAmalgamator.SpawnState","EntityStates.SolusAmalgamator.DeathState"}),
            ("cscTotem","TotemBody",new[]{"EntityStates.Totem.SpawnState","EntityStates.Totem.DeathState"}),
            ("cscBigMush","BigMushBody",new[]{"EntityStates.BigMush.SpawnState","EntityStates.BigMush.DeathState"}),
            ("cscVultureHunter","VultureHunterBody",new[]{"EntityStates.VultureHunter.Flight.Flying","EntityStates.VultureHunter.DeathExplodeAirState"}),
            ("cscReaper","ReaperBody",new[]{"EntityStates.Reaper.SpawnState","EntityStates.GenericCharacterMain"}),
            ("cscMinorConstructAttachable","MinorConstructAttachableBody",new[]{"EntityStates.MinorConstruct.Hidden","EntityStates.MinorConstruct.NoCastSpawn"}),
            ("cscLynx2","Lynx2Body",new[]{"EntityStates.Lynx2Monster.SpawnState","EntityStates.Lynx2Monster.DeathState"}),
            ("cscTitanGolemPlains","TitanBody",new[]{"EntityStates.TitanMonster.SpawnState","EntityStates.TitanMonster.DeathState"}),
        };
        foreach(var item in accepted)Check(GauntletBossPolicy.Standalone(item.Card,item.Body,item.States),"Self-contained boss or enemy remains eligible: "+item.Card);
        Check(!GauntletBossPolicy.Released("SPIRIT_BODY_NAME","SPIRIT_BODY_NAME") && !GauntletBossPolicy.Released("","x") && !GauntletBossPolicy.Released("SWIFT_BODY_NAME",null),
            "Unnamed native bodies are unreleased content");
        Check(GauntletBossPolicy.Released("LYNXTOTEM_BODY_NAME","Lynx Totem"),"Named enemies remain eligible");
    }
    private static void DifficultyCurve()
    {
        // Installed native ordinary-enemy director costs after the scripted filter.
        int[] roster={8,10,10,10,11,11,12,12,14,15,18,20,25,25,28,28,28,35,35,40,40,40,40,40,40,60,60,80,80,100,115,125,150,150,200,200,200,300,350,450,550};
        double previousCeiling=0,previousMean=0,worstStep=0,firstMean=0;
        for(int round=1;round<=GauntletPlan.RoundsPerCycle;round++)
        {
            int ceiling=GauntletBalance.HordeCostCeiling(round,1);
            Check(ceiling>=previousCeiling && (previousCeiling==0 || ceiling/previousCeiling<=1.68),"Threat unlock grows smoothly by round");previousCeiling=ceiling;
            var eligible=roster.Where(cost=>cost<=ceiling).ToArray();
            Check(eligible.Length>=10,"Every round offers a varied mix");
            double weight=eligible.Sum(cost=>(double)GauntletBalance.SpeciesWeight(cost,round,1,false));
            double mean=eligible.Sum(cost=>cost*(double)GauntletBalance.SpeciesWeight(cost,round,1,false))/weight;
            Check(mean>=previousMean,"Average spawn threat never falls as rounds advance");
            if(previousMean>0)worstStep=Math.Max(worstStep,mean/previousMean);else firstMean=mean;previousMean=mean;
            for(int i=1;i<eligible.Length;i++)Check(GauntletBalance.SpeciesWeight(eligible[i],round,1,false)<=GauntletBalance.SpeciesWeight(eligible[i-1],round,1,false),"Fodder is at least as common as heavier species");
            Check(GauntletBalance.SpeciesWeight(40,round,1,true)==GauntletBalance.SpeciesWeight(40,round,1,false)*3,"Theme keeps its threefold bias");
        }
        Check(GauntletBalance.HordeCostCeiling(1,1)==40 && GauntletBalance.HordeCostCeiling(6,1)<550 && GauntletBalance.HordeCostCeiling(7,1)>=550,"Opening matches 0.14; every native enemy is eligible by round seven");
        Check(GauntletBalance.HordeCostCeiling(1,2)==int.MaxValue,"Continuation cycles start with the full roster");
        // The 0.14 curve: equal-count mix, 40/100 then everything at round five.
        double Old(int round)
        {
            int cap=round<=2?40:round<=4?100:int.MaxValue;var pool=roster.Where(cost=>cost<=cap).ToArray();
            double w=pool.Sum(cost=>round<=2 && cost<=20?3d:1),m=pool.Sum(cost=>cost*(round<=2 && cost<=20?3d:1));return m/w;
        }
        Check(Old(5)/Old(4)>2 && worstStep<1.7,$"Round-five threat cliff ({Old(5)/Old(4):0.00}x) replaced by gradual steps (worst {worstStep:0.00}x)");
        Console.WriteLine($"Difficulty curve: mean spawn cost {firstMean:0} -> {previousMean:0} over ten rounds, worst round-to-round step {worstStep:0.00}x (0.14 jumped {Old(5)/Old(4):0.00}x at round five).");
        // Champion ladder by native director cost bands.
        var champions=new (string Name,int Cost,float Health)[]{("BeetleQueen",600,2100),("ClayBoss",600,2100),("Titan",600,2100),("Vagrant",600,2100),("Reaper",600,4000),("Totem",700,2500),
            ("Gravekeeper",800,2800),("ImpBoss",800,2800),("MagmaWorm",800,2400),("MegaConstruct",800,2800),("RoboBallBoss",800,2500),("VoidMegaCrab",800,2800),("SolusAmalgamator",800,3000),
            ("VultureHunter",800,3600),("BigMush",800,2800),("Grandparent",1150,3625),("ScavBoss",2000,3800),("ElectricWorm",4000,12000),
            ("SuperRoboBallBoss",GauntletBalance.BossCost(0,2500),2500),("ScavSanctum",GauntletBalance.BossCost(0,2500),2500)};
        Check(GauntletBalance.BossCost(0,2500)==750 && GauntletBalance.BossCost(600,99999)==600,"Scripted-entry champions without a card cost are ranked by durability");
        for(int round=1;round<=GauntletPlan.RoundsPerCycle;round++)
        {
            int high=GauntletBalance.BossCostCeiling(round,1),low=GauntletBalance.BossCostFloor(round,1);
            var pool=champions.Where(c=>c.Cost<=high && c.Cost>=low && (round>1 || GauntletBalance.OpeningBoss(c.Health,20))).ToArray();
            Check(pool.Length>=4,"Every ordinary champion round has several species");
            Check(round>=9 || pool.All(c=>c.Name!="ElectricWorm"),"Overloading Worm waits for the final stretch");
            Check(round>1 || pool.All(c=>c.Cost<=600 && c.Health<=2800),"Opening champions stay in the gentlest band");
            Check(round<5 || pool.All(c=>c.Cost>=700),"Late rounds skip the opening champions");
        }
        foreach(var (round,cycle,expected) in new[]{(5,1,new[]{1}),(10,1,new[]{3}),(5,2,new[]{2,3}),(10,2,new[]{3})})
        {
            GauntletBalance.GreatTierRange(round,cycle,out int low,out int high);
            Check(Enumerable.Range(low,high-low+1).SequenceEqual(expected),$"Major encounter ladder for round {round} cycle {cycle}");
        }
        Check(GauntletBalance.GreatTier("BazaarGauntlet_cscScavLunar ScavLunar3Body")==1 && GauntletBalance.GreatTier("BazaarGauntlet_cscMiniVoidRaidCrabPhase3 MiniVoidRaidCrabBodyPhase3")==2 &&
            GauntletBalance.GreatTier("BazaarGauntlet_cscBrotherIT ITBrotherBody")==3,"Twisted Scavenger, Voidling and Mithrix escalate");
    }
    private static void WarCouncil()
    {
        var roster=GauntletCommandRoster.Commanders;
        Check(roster.Length==10 && Enumerable.Range(1,5).All(tier=>roster.Count(def=>def.Tier==tier)==2),"Ten commanders, two per tier");
        for(int tier=2;tier<=5;tier++)
        {
            Check(roster.Where(def=>def.Tier==tier).Min(def=>def.Cost)>roster.Where(def=>def.Tier==tier-1).Max(def=>def.Cost),"Cost escalates with tier");
            Check(GauntletCommandRoster.UnlockRound(tier)>GauntletCommandRoster.UnlockRound(tier-1),"Higher tiers unlock later");
            Check(GauntletCommandRoster.HealthStacks(tier,false)>GauntletCommandRoster.HealthStacks(tier-1,false) && GauntletCommandRoster.DamageStacks(tier,true)>GauntletCommandRoster.DamageStacks(tier-1,true),"Durability and damage bonuses keep increasing with tier");
            if(tier>=3)
                Check(GauntletCommandRoster.DamageStacks(tier,true)-GauntletCommandRoster.DamageStacks(tier-1,true)==2 &&
                    GauntletCommandRoster.DamageStacks(tier,false)-GauntletCommandRoster.DamageStacks(tier-1,false)==1,"Later tiers add the same damage increment instead of a growing jump");
        }
        foreach(var def in roster)
        {
            Check(def.Radius>0 && def.Period>=5 && def.Power>0 && def.SquadSize>=2 && def.SquadSize<=4,"Ability and squad are bounded: "+def.Name);
            Check(def.LeaderCard.StartsWith("csc") && def.LeaderCard.EndsWith("_asset") && def.SquadCard.EndsWith("_asset"),"Native registry spawn cards: "+def.Name);
            Check(GauntletBossPolicy.Standalone(def.LeaderCard,def.LeaderName,null),"Commanders never use scripted encounter bodies: "+def.Name);
        }
        Check(roster.Select(def=>def.Ability).Distinct().Count()==roster.Length,"Every commander has a unique ability");
        Check(GauntletCommandRoster.Unlocked(5,1,2) && !GauntletCommandRoster.Unlocked(5,7,1) && GauntletCommandRoster.Unlocked(5,8,1),"Continuation cycles open every tier");
        Check(GauntletCommandRoster.Renown(80,false,1)==1 && GauntletCommandRoster.Renown(80,true,1)==2 && GauntletCommandRoster.Renown(2200,false,1)==6,"Kill renown follows base durability and elites");
        Check(GauntletCommandRoster.Renown(float.NaN,false,1)==1 && GauntletCommandRoster.Renown(1e30f,true,10)>0 && GauntletCommandRoster.Renown(80,false,0)==0 && GauntletCommandRoster.Renown(80,false,float.NaN)==0,"Renown is bounded and can be disabled");
        Check(GauntletCommandRoster.BossRenown(5,1,GauntletBossKind.Great,1)==58 && GauntletCommandRoster.BossRenown(10,1,GauntletBossKind.Final,1)==3*50 &&
            GauntletCommandRoster.BossRenown(1,2,GauntletBossKind.Ordinary,1)>GauntletCommandRoster.BossRenown(1,1,GauntletBossKind.Ordinary,1),"Champion renown rises with round, major encounters and cycles");
        var council=new GauntletWarCouncil();double now=0;
        double Next()=>now+=1;
        Check(council.Hire(1,0,1,1,3,true,Next())==CommandResult.Unaffordable && council.Get(1).Companies.Count==0,"No renown, no company");
        council.Award(1,19);Check(council.Hire(1,0,1,1,3,true,Next())==CommandResult.Unaffordable && council.Get(1).Renown==19,"Insufficient renown is never spent");
        council.Award(1,1);Check(council.Hire(1,0,1,1,3,true,Next())==CommandResult.Hired && council.Get(1).Renown==0,"Exact renown hires a tier-one company");
        Check(council.Hire(1,0,1,1,3,true,now+.01)==CommandResult.Throttled,"Rapid repeated requests are throttled");
        council.Award(1,5000);
        Check(council.Hire(1,0,1,1,3,true,Next())==CommandResult.AlreadyCommanding && council.Get(1).Renown==5000,"A commanded company cannot be bought twice");
        Check(council.Hire(1,2,1,1,3,true,Next())==CommandResult.Locked && council.Hire(1,2,2,1,3,true,Next())==CommandResult.Hired,"Tier two opens at round two");
        Check(council.Hire(1,7,10,1,3,false,Next())==CommandResult.Unavailable,"Disabled expansions cannot be hired");
        Check(council.Hire(1,1,3,1,3,true,Next())==CommandResult.Hired && council.Hire(1,3,3,1,3,true,Next())==CommandResult.NoCapacity,"Company capacity is enforced");
        long before=council.Get(1).Renown;
        Check(council.Dismiss(1,1,Next())==CommandResult.Dismissed && council.Get(1).Renown==before && council.Get(1).Active==2,"Discharge frees a slot without refunds");
        Check(council.Dismiss(1,1,Next())==CommandResult.NotCommanding && council.Hire(2,99,1,1,3,true,Next())==CommandResult.Unknown,"Stale and unknown requests are harmless");
        Check(council.Fall(1,0) && !council.Fall(1,0) && council.Get(1).Active==1 && council.Get(1).Find(0).Fallen,"A fallen commander frees its slot once");
        Check(council.Hire(1,0,3,1,3,true,Next())==CommandResult.Hired && !council.Get(1).Find(0).Fallen && council.Get(1).Companies.Count(c=>c.Commander==0)==1,"A fallen banner can be raised again in place");
        council.Award(3,long.MaxValue);council.Award(3,long.MaxValue);Check(council.Get(3).Renown==long.MaxValue,"Renown saturates instead of overflowing");
        Check(!council.Award(3,0) && !council.Award(3,-5),"Negative awards cannot spend renown");
        EconomyAudit.Run(Check); 
    }
    private static void Insignia()
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        using var stream=File.OpenRead(Path.Combine(root,"assets/commanders.meshbin"));
        var art=FortressArchitectureData.Read(stream);
        var names=art.Parts.Select(part=>part.Name).ToArray();
        Check(names.SequenceEqual(new[]{"standard-brass","banner-ivory","banner-brass","rank1-energy","rank2-energy","rank3-energy","rank4-energy","rank5-energy","sigil-brass","sigil-energy"}),"Runtime insignia part names match the loader");
        int triangles=art.Parts.Sum(part=>part.Indices.Length/3);
        Check(triangles<10000,"Insignia stay cheap enough for every allied unit");
        foreach(var part in art.Parts)
            for(int i=0;i<part.Vertices.Length;i+=8)
            {
                float x=part.Vertices[i],y=part.Vertices[i+1],z=part.Vertices[i+2];
                Check(part.Name.StartsWith("sigil")?x*x+y*y+z*z<=.3f*.3f:y>=-.001f && y<=2.3f && Math.Abs(x)<=.65f && Math.Abs(z)<=.2f,"Insignia fit their runtime anchor: "+part.Name);
            }
        for(int tier=1;tier<=5;tier++)
            Check(art.Parts.Single(part=>part.Name=="rank"+tier+"-energy").Indices.Length==art.Parts.Single(part=>part.Name=="rank1-energy").Indices.Length*tier,"Banner shows exactly its tier in chevrons: "+tier);
        var banner=art.Parts.Single(part=>part.Name=="banner-ivory");
        for(int t=0;t<banner.Indices.Length;t+=3)
        {
            // The swallowtail notch stays open on both faces.
            float cx=0,cy=0;for(int k=0;k<3;k++){cx+=banner.Vertices[banner.Indices[t+k]*8];cy+=banner.Vertices[banner.Indices[t+k]*8+1];}
            cx/=3;cy/=3;Check(!(Math.Abs(cx)<.08f && cy<.8f && cy>.58f),"Banner tail notch is not filled");
        }
        Console.WriteLine($"Insignia: {triangles:N0} triangles in {art.Parts.Length} parts.");
    }
    private static void CompressedCycle()
    {
        Check(GauntletPlan.RoundsPerCycle==10,"A complete cycle has ten rounds");
        int[] quotas={23,34,44,55,59,63,68,72,76,80};
        int[] caps={15,30,43,53,62,69,75,84,95,105};
        int[] counts={1,1,1,1,1,2,2,2,2,1};
        int[] ceiling={600,800,800,1150,1150,2000,2000,2000,int.MaxValue,int.MaxValue};
        int[] leads={0,0,1,1,2,2,3,3,4,5};
        for(int round=1;round<=10;round++)
        {
            Check(GauntletBalance.HordeSize(round,1,1)==quotas[round-1],"Compressed horde curve golden value: "+round);
            Check(GauntletBalance.ConcurrentLimit(round,1,1,160)==caps[round-1],"Compressed concurrency golden value: "+round);
            Check(GauntletPlan.BossCount(round)==counts[round-1],"Only ordinary encounters after five have two bosses: "+round);
            Check(GauntletBalance.BossCostCeiling(round,1)==ceiling[round-1],"Champion bands reach the same final roster: "+round);
            Check(GauntletPlan.LevelAdvantage(round)==leads[round-1],"Player-relative levels reach the old endpoint at ten: "+round);
            Check(GauntletBalance.AdvancesDifficultyStage(round)==(round%2==0),"Stage pressure preserves five advances per cycle");
        }
        Check(GauntletPlan.OriginalRound(int.MinValue)==1 && GauntletPlan.OriginalRound(int.MaxValue)==20,"Out-of-range curve inputs clamp safely");
        Check(Enumerable.Range(1,5).Select(GauntletCommandRoster.UnlockRound).SequenceEqual(new[]{1,2,4,6,8}),"Every commander tier remains reachable before the final encounter");
        var pair=new GauntletQuotaLedger<int>(2);
        Check(pair.Spawn(1) && pair.Defeat(1) && !pair.Complete,"An instant first-boss kill cannot finish before the second boss spawns");
        Check(pair.Spawn(2) && pair.Full && !pair.Complete,"Second spawned boss must still be killed");
        Check(pair.Despawn(2) && !pair.Complete && pair.Spawn(3) && !pair.Complete,"A despawned second boss is replaced, never credited as defeated");
        Check(pair.Defeat(3) && pair.Complete && !pair.Defeat(3) && !pair.Spawn(4),"Both counted deaths complete once and cannot farm more quota");
        var gate=new GauntletEncounterGate<object>();var wave=new object();
        Check(!gate.TryBegin(1,11,GauntletPhase.Horde),"Old rounds eleven to twenty cannot start in a ten-round run");
        Check(gate.TryBegin(2,1,GauntletPhase.Horde) && gate.NativeIndex==21 && gate.Attach(wave) && gate.Commit(wave),"Continuation starts at native encounter twenty-one without a phantom ten-round gap");
    }
    public static void Main()
    {
        CompressedCycle();
        var gate=new GauntletArrivalGate();
        Check(!gate.Tick(0,false) && !gate.Tick(10,true) && !gate.Tick(12.99,true),"Automatic fight waits three seconds after readiness");
        Check(!gate.Tick(13,false) && !gate.Tick(20,true) && !gate.Tick(22.99,true),"A player still loading resets the arrival grace");
        Check(gate.Tick(23,true) && !gate.Tick(24,true) && !gate.Tick(99,true),"Arrival starts exactly one native fight");
        gate.Reset();Check(!gate.Tick(100,true) && gate.Tick(103,true),"Returning from the Bazaar rearms automatic start");
        foreach(int living in new[]{-1,0,1,5,6,80})
        {
            Check(!GauntletPlan.ShouldMarkEnemies(living,false),"Never mark while more quota enemies have yet to spawn");
            Check(GauntletPlan.ShouldMarkEnemies(living,true)==(living>=1 && living<=5),"Only the final five living enemies are marked");
        }
        var flow = new GauntletFlow();
        Castle();
        Architecture();
        Colosseum();
        EncounterFailures();
        for(int mask=0;mask<16;mask++)
        {
            bool mode=(mask&1)!=0,server=(mask&2)!=0,transfer=(mask&4)!=0,player=(mask&8)!=0;
            Check(GauntletCurrencyPolicy.BlockTransferDebit(mode,server,transfer,player,0)==(mask==15),"Only BiggerBazaar's temporary host-player zeroing is blocked in this mode");
            Check(!GauntletCurrencyPolicy.BlockTransferDebit(mode,server,transfer,player,125),"Actual purchases, rewards and nonzero balance changes remain native");
        }
        foreach(uint starting in new uint[]{0,500,10000,uint.MaxValue})
        {
            uint wallet=starting,stored=0;
            for(int pass=0;pass<3;pass++)
            {stored=wallet;if(!GauntletCurrencyPolicy.BlockTransferDebit(true,true,true,true,0))wallet=0;}
            Check(wallet==starting && stored==starting,"Repeated Begin/AdvanceStage/round-one Bazaar snapshots preserve both wallet and saved money");
            wallet=starting>100?starting-100:0;uint remaining=wallet;
            if(!GauntletCurrencyPolicy.BlockTransferDebit(true,true,false,true,remaining))wallet=remaining;
            Check(wallet==remaining,"Shopping spending is not restored or refunded by transfer protection");
        }
        Check(GauntletLootPolicy.Sweep(false) && GauntletLootPolicy.Restore(false),"Arena free drops and queued choices are eligible");
        Check(!GauntletLootPolicy.Sweep(true) && !GauntletLootPolicy.Restore(true),"Neither native nor modded Bazaar display/choice stock can enter the carry bag");
        Check(!GauntletLootPolicy.NeedsRewardMigration(0) && !GauntletLootPolicy.NeedsRewardMigration(4),"Fresh settings and already-updated settings never get a second reward boost");
        foreach(int revision in new[]{1,2,3})Check(GauntletLootPolicy.NeedsRewardMigration(revision),"Old saved settings receive the authorized increase once");
        Check(GauntletLootPolicy.Boost(.6f)==.72f && GauntletLootPolicy.Boost(3f)==GauntletBalance.GoldMultiplier,"Historical reward migration still applies exactly twenty percent");
        Check(GauntletLootPolicy.TenRoundDrops(.72f)==1.6f && GauntletLootPolicy.TenRoundDrops(0)==0 && GauntletLootPolicy.TenRoundDrops(1.1f)==1.1f,"Ten-round migration updates the old default and preserves custom/disabled drops");
        Check(GauntletLootPolicy.TenRoundDrops(GauntletLootPolicy.TenRoundDrops(.72f))==1.6f,"Ten-round default migration is idempotent");
        Check(GauntletLootPolicy.BoostGold(150)==GauntletBalance.StartingGold && GauntletLootPolicy.BoostGold(40)==GauntletBalance.RoundGold,"Starting and round gold gain twenty percent");
        Check(Math.Abs(GauntletBalance.BossItemChance-.2f*1.2f)<.00001f,"Yellow boss chance rises from twenty to twenty-four percent");
        Check(GauntletLootPolicy.Boost(5)==6 && GauntletLootPolicy.Boost(10)==12 && GauntletLootPolicy.Boost(0)==0,"Custom and disabled reward settings retain their proportions");
        foreach(bool arenaToBazaar in new[]{true,false})
        {
            Check(GauntletCurrencyPolicy.BlockExtraction(true,true,true,true),"Accepted arena/Bazaar departure must not convert any gold to XP in either direction");
            for(int mask=0;mask<16;mask++)
            {
                bool mode=(mask&1)!=0,server=(mask&2)!=0,accepted=(mask&4)!=0,extracting=(mask&8)!=0;
                Check(GauntletCurrencyPolicy.BlockExtraction(mode,server,accepted,extracting)==(mask==15),"Other game modes, unaccepted exits, clients and non-extraction states retain native behavior");
            }
        }
        ManagementAndRepairs();
        HordeVarietyAndRoles();
        DirectedApproaches();
        Check(flow.Start() && !flow.Start(), "Run begins exactly once");
        PlayCycle(flow);
        Check(flow.ContinueThroughBazaar() && !flow.ContinueThroughBazaar(),"Endless continuation queues only once, without a menu");
        Check(flow.Round==10 && flow.Cycle==1,"Queueing continuation preserves the completed cycle until portal return");
        Check(flow.EnterBazaar() && flow.BazaarLoaded() && flow.ReturnToArena(),"Endless cycle continues through the real Bazaar");
        Check(!flow.ReturnToArena(),"Duplicate final-round portal requests cannot skip cycles");
        Check(flow.Cycle == 2 && flow.Round == 1, "Next cycle keeps persistent run ownership with fresh local round");
        PlayCycle(flow);
        Check(flow.ClaimVictory() && !flow.ClaimVictory(), "Victory commits once");
        Check(!flow.Lose() && !flow.ContinueThroughBazaar(), "Finalized victory cannot mutate");
        var loss = new GauntletFlow();loss.Start();Check(loss.Lose() && !loss.Lose(), "Team wipe is terminal and idempotent");
        for (int player = 1; player <= 100; player++)
        for (int round = 1; round <= GauntletPlan.RoundsPerCycle; round++)
        {
            int bonus = GauntletPlan.LevelAdvantage(round);
            Check(bonus >= 0 && bonus <= 5, "Player-relative level floor stays in intended band");
            Check(GauntletPlan.EnemyLevelFloor(1, player, round) == player + bonus, "Underleveled enemies use adaptive floor");
            Check(GauntletPlan.EnemyLevelFloor(player + 30, player, round) == player + 30, "Harder native/modded difficulty is never reduced");
        }
        Check(GauntletPlan.LevelAdvantage(1) == 0 && GauntletPlan.LevelAdvantage(10) == 5, "No artificial opening level lead; final offset ramps to five");
        Check(GauntletPlan.EnemyLevelFloor(1, 1, 1) == 1, "Itemless opening no longer forces level-six enemies");
        Check(GauntletPlan.LevelAdvantage(1, 5, 10) == 5 && GauntletPlan.LevelAdvantage(10, 5, 10) == 10, "Explicit host custom level leads remain supported");
        Check(GauntletPlan.EnemyLevelFloor(int.MaxValue, int.MaxValue, 10) == int.MaxValue, "Extreme custom levels cannot overflow");
        // 0.18 keeps the former opening/final endpoints across ten rounds.
        Check(GauntletBalance.HordeSize(1, 1, 1) == 23 && GauntletBalance.HordeSize(10, 1, 1) == 80, "Preserved opening and eighty-enemy solo endpoint");
        Check(GauntletBalance.ConcurrentLimit(1, 1, 1, 80) == 15 && GauntletBalance.ConcurrentLimit(10, 1, 1, 80) == 80, "Progressive live-enemy endpoints");
        Check(GauntletBalance.ConcurrentLimit(10, 1, 1, 160) == 105, "A raised host ceiling admits the full late-round live count");
        // The early curve sits above the plain +25% line and meets it at round 14.
        Check(GauntletBalance.EarlyBoost(7)>1 && GauntletBalance.EarlyBoost(8)==1 && GauntletBalance.EarlyBoost(10)==1,
            "The old round-14 early boost ends between compressed rounds seven and eight");
        Check(Math.Abs(GauntletBalance.CreditRate(10,1,1)-30.625f*80/118)<.001f,"Final spawn credits shrink by the same percentage as the horde");
        Check(GauntletBalance.ConcurrentLimit(10, 1, 1, 80) > 40, "Later rounds exceed the old fixed forty-enemy ceiling");
        Check(GauntletBalance.ConcurrentLimit(10, 1, 1, 24) == 24, "Lower host performance cap remains effective");
        Check(Math.Abs(GauntletBalance.CreditRate(1, 1, 1) - 11.025f) < .001f, "Credit stream grows with the early live cap so it actually fills");
        Check(GauntletBalance.InitialCredits(1, 1) == 37.5f, "Opening budget buys a small advance group");
        ScriptedBosses();
        DifficultyCurve();
        WarCouncil();
        NetworkAudit.Run(Check);
        ShopAudit.Run(Check);
        ExperienceAudit.Run(Check);
        DropAudit.Run(Check);
        ItemRunAudit.Run(Check);
        StarterAudit.Run(Check);
        HordeAudit.Run(Check);
        CompanyDamageAudit.Run(Check);
        MetaAudit.Run(Check);
        Insignia();
        for (int players = 1; players <= 16; players++)
        for (int cycle = 1; cycle <= 20; cycle++)
        {
            int previous = 0, previousCap = 0;
            for (int round = 1; round <= GauntletPlan.RoundsPerCycle; round++)
            {
                int size = GauntletBalance.HordeSize(round, cycle, players);
                Check(size >= previous && size <= 500, "Finite horde grows monotonically and remains bounded");previous = size;
                int cap = GauntletBalance.ConcurrentLimit(round, cycle, players, 80);
                Check(cap >= previousCap && cap >= 4 && cap <= 80, "Live cap grows monotonically within the host ceiling");previousCap = cap;
                Check(GauntletBalance.CreditRate(round, players, 5) >= GauntletBalance.CreditRate(round, players, 1), "Higher difficulty cannot reduce credit pressure");
                Check(GauntletBalance.HordePopulationRatio(round,cycle,players)>0 && GauntletBalance.HordePopulationRatio(round,cycle,players)<=1,"Smaller hordes never receive a larger director or item budget");
            }
        }
        Check(GauntletBalance.ClockMultiplier(3.5f, 3.5f) == 1 && GauntletBalance.ClockMultiplier(6, 3.5f) == 1, "Typhoon or harder never receives duplicate scaling");
        Check(Math.Abs(GauntletBalance.ClockMultiplier(3, 3.5f) * 3 - 3.5f) < .0001f, "Monsoon clock reaches Typhoon slope");
        Check(GauntletBalance.ClockMultiplier(3, 0) == 1, "Host can disable the minimum clock slope");
        int stages = 0;
        for (int round = 1; round <= GauntletPlan.RoundsPerCycle; round++) if (GauntletBalance.AdvancesDifficultyStage(round)) stages++;
        Check(stages == 5, "Ten arenas preserve five difficulty-stage increments");
        var quota = new GauntletQuotaLedger<int>(3);
        Check(quota.Spawn(1) && quota.Spawn(2) && quota.Spawn(3) && quota.Full, "Only the finite roster fills the quota");
        Check(!quota.Spawn(4), "Extra summons do not enter a full roster");
        Check(!quota.Defeat(4) && quota.Defeated == 0, "Untracked enemy deaths cannot farm quota progress");
        Check(quota.Defeat(1) && !quota.Defeat(1) && quota.Defeated == 1, "Duplicate death reports count once");
        Check(quota.Despawn(2) && !quota.Full && quota.Defeated == 1 && !quota.Despawn(2), "Despawned enemies reopen a slot without granting a kill");
        Check(quota.Spawn(5) && quota.Full, "Replacement fills only the missing slot");
        Check(quota.Defeat(3) && quota.Defeat(5) && quota.Defeated == 3, "All required enemies must be defeated");
        var restarted = new GauntletFlow();
        Check(restarted.Start() && restarted.Round == 1 && restarted.Cycle == 1 && restarted.Phase == GauntletPhase.LoadingArena,
            "A replacement run starts at round one despite the previous coordinator's terminal state");
        Console.WriteLine($"PASS: {checks} production arena geometry/navigation, Bazaar round-flow, duplicate-event, adaptive-level and legacy asset assertions. No game launched.");
    }
}
