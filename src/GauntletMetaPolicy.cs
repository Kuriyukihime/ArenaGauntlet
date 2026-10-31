using System;
using System.Linq;

namespace BazaarGauntlet
{
    [Serializable]
    internal sealed class GauntletCareer
    {
        public int Version=1,Armory,Barracks,Patronage,Ascension,HighestClear=-1,Company=0;
        public long Earned;
        public string[] Items=new string[5];
        public string Equipment="";
        public string[] Receipts=Array.Empty<string>();
        public int[] Received=Array.Empty<int>();
        // Treasury: owned arena skins and decorations, the chosen skin ("" is the default arena) and shown decorations.
        public string[] Cosmetics=Array.Empty<string>(),Decor=Array.Empty<string>();
        public string Skin="";
        // Labors: ids of completed one-time feats. Each paid its Glory into Earned when completed.
        public string[] Labors=Array.Empty<string>();
        // Glory spent so far. 0 on careers last saved by 1.0.0, which paid the legacy prices for what they own.
        public long Paid;
        internal long Balance=>Earned-GauntletMetaPolicy.Spent(this);
    }
    internal static class GauntletMetaPolicy
    {
        internal static readonly int[][] Costs={new[]{15,30,45,70,90,120},new[]{25,60,90},new[]{30,75}};
        internal const int CosmeticCost=50,MaxCosmetics=32;
        // 1.0.0 prices. Purchases made then stay at what they cost.
        private static readonly int[][] LegacyCosts={new[]{10,20,30,45,60,80},new[]{15,40,60},new[]{20,50}};
        private const int LegacyCosmeticCost=30;
        internal const int MyriadKills=400,IronWillRounds=8,AgoraPurchases=25,SpoilsItems=60,FavoredAscension=3,UnbrokenRound=5;
        // The host credits these during runs. Ids are saved in careers: never rename or reuse one.
        internal static readonly (string Id,string Name,string Text,int Glory)[] Labors={
            ("initiate","First Blood","Clear your first arena round.",5),
            ("halfway","Halfway to Glory","Clear round 5 by defeating its great champion.",10),
            ("victor","Victor of the Arena","Win a run by clearing round 10.",20),
            ("favored","Favored of Olympus","Win a run on Ascension III or higher.",25),
            ("unbroken","Unbroken",$"Clear round {UnbrokenRound} or later without falling.",10),
            ("ironwill","Iron Will",$"Clear {IronWillRounds} rounds in one run without falling in them.",15),
            ("giantslayer","Giant-Slayer","Land the killing blow on an arena champion yourself.",10),
            ("myriad","Myriad",$"Defeat {MyriadKills} enemies in one run. Your companies' kills count.",10),
            ("strategos","Strategos","Lead a mercenary company into battle.",5),
            ("warhost","Warhost","Command two companies at the same time.",10),
            ("agora","Patron of the Agora",$"Make {AgoraPurchases} Bazaar purchases in one run.",10),
            ("spoils","Spoils of War",$"Hold {SpoilsItems} items when a round ends.",10)};
        internal static int Rank(GauntletCareer career,int branch)=>branch==0?career.Armory:branch==1?career.Barracks:career.Patronage;
        internal static long Spent(GauntletCareer c)=>c.Paid>0?c.Paid:LegacyCosts[0].Take(c.Armory).Sum()+LegacyCosts[1].Take(c.Barracks).Sum()+LegacyCosts[2].Take(c.Patronage).Sum()+
            (c.Cosmetics?.Length??0)*LegacyCosmeticCost;
        // Saves and packets from before the Treasury and Labors have no fields for them.
        internal static GauntletCareer Normalize(GauntletCareer c)
        {
            if(c!=null){c.Cosmetics=c.Cosmetics??Array.Empty<string>();c.Decor=c.Decor??Array.Empty<string>();c.Skin=c.Skin??"";c.Labors=c.Labors??Array.Empty<string>();}
            return c;
        }
        internal static bool ShortId(string id)=>!string.IsNullOrEmpty(id) && id.Length<=24;
        internal static bool Valid(GauntletCareer c)=>c!=null && c.Version==1 && c.Armory>=0 && c.Armory<=6 && c.Barracks>=0 && c.Barracks<=3 &&
            c.Cosmetics!=null && c.Decor!=null && c.Skin!=null && c.Cosmetics.Length<=MaxCosmetics && c.Cosmetics.All(ShortId) &&
            c.Cosmetics.Distinct().Count()==c.Cosmetics.Length && (c.Skin=="" || c.Cosmetics.Contains(c.Skin)) &&
            c.Decor.All(c.Cosmetics.Contains) && c.Decor.Distinct().Count()==c.Decor.Length &&
            // Shape only, so a save stays loadable if a later version retires a labor.
            c.Labors!=null && c.Labors.Length<=64 && c.Labors.All(ShortId) && c.Labors.Distinct().Count()==c.Labors.Length &&
            c.Patronage>=0 && c.Patronage<=2 && c.Paid>=0 && c.Earned>=0 && c.Earned<=1000000000 && c.Earned>=Spent(c) && c.HighestClear>=-1 && c.HighestClear<=6 &&
            c.Ascension>=0 && c.Ascension<=Math.Min(6,c.HighestClear+1) && c.Company>=0 && c.Company<=1 && c.Items!=null && c.Items.Length==5 &&
            c.Items.All(name=>name==null || name.Length<=160) && c.Equipment!=null && c.Equipment.Length<=160 &&
            c.Receipts!=null && c.Received!=null && c.Receipts.Length==c.Received.Length && c.Receipts.Length<=32 &&
            c.Receipts.All(id=>!string.IsNullOrEmpty(id) && id.Length<=96) && c.Received.All(amount=>amount>=0 && amount<=100000);
        internal static bool Buy(GauntletCareer c,int branch)
        {
            if(!Valid(c) || branch<0 || branch>=Costs.Length)return false;
            int rank=Rank(c,branch);if(rank>=Costs[branch].Length || c.Balance<Costs[branch][rank])return false;
            c.Paid=Spent(c)+Costs[branch][rank];
            if(branch==0)c.Armory++;else if(branch==1)c.Barracks++;else c.Patronage++;
            return true;
        }
        internal static bool BuyCosmetic(GauntletCareer c,string id)
        {
            if(!Valid(c) || !ShortId(id) || c.Cosmetics.Length>=MaxCosmetics || c.Cosmetics.Contains(id) || c.Balance<CosmeticCost)return false;
            c.Paid=Spent(c)+CosmeticCost;c.Cosmetics=c.Cosmetics.Append(id).ToArray();return true;
        }
        internal static bool CompleteLabor(GauntletCareer c,string id)
        {
            int index=Array.FindIndex(Labors,labor=>labor.Id==id);
            if(index<0 || !Valid(c) || c.Labors.Contains(id))return false;
            c.Labors=c.Labors.Append(id).ToArray();c.Earned=Math.Min(1000000000,c.Earned+Labors[index].Glory);return true;
        }
        internal static bool ShowDecor(GauntletCareer c,string id,bool shown)
        {
            if(!Valid(c) || !c.Cosmetics.Contains(id))return false;
            c.Decor=shown?c.Decor.Where(d=>d!=id).Append(id).ToArray():c.Decor.Where(d=>d!=id).ToArray();return true;
        }
        internal static int Slots(int rank)=>rank>=6?5:rank>=5?4:3;
        internal static int Budget(int rank)=>new[]{0,6,9,11,14,17,21}[Math.Max(0,Math.Min(6,rank))];
        // Categories: white, green, red, lunar item, ordinary equipment, lunar equipment.
        internal static int Cost(int category)=>category==0?1:category==1?2:category==2?5:category==3?4:category==4?3:4;
        internal static bool Allowed(int rank,int category)=>rank>=1 && category>=0 && category<=5 && (category<=1 || category==2 && rank>=4 || category==3 && rank>=3 || category==4 && rank>=2 || category==5 && rank>=3);
        internal static int Capacity(int players,int configured,int barracks)=>GauntletNetworkPolicy.CompanyCapacity(players,configured)+(barracks>=2?1:0);
        internal static int TotalGlory(int completed,int ascension)=>(Math.Max(0,Math.Min(10,completed))+(completed>=5?3:0)+(completed>=10?7:0))*(4+ascension)/4;
        internal static int RoundGlory(int round,int ascension)=>TotalGlory(round,ascension)-TotalGlory(round-1,ascension);
        internal static int Bosses(int normal,bool ordinary,int ascension)=>normal+(ordinary && ascension>=4?1:0);
        internal static int HealthStacks(int ascension,bool boss)=>ascension<=0?0:(boss?new[]{0,3,6,10,15,22,30}:new[]{0,1,2,3,4,5,6})[Math.Min(6,ascension)];
        internal static int DamageStacks(int ascension)=>new[]{0,1,2,3,4,5,7}[Math.Max(0,Math.Min(6,ascension))];
        internal static bool Receipt(GauntletCareer c,string id,int total,int clear)
        {
            if(!Valid(c) || string.IsNullOrEmpty(id) || id.Length>96 || total<0 || total>100000 || clear< -1 || clear>6)return false;
            if(c.Receipts==null || c.Received==null || c.Receipts.Length!=c.Received.Length || c.Receipts.Length>32){c.Receipts=Array.Empty<string>();c.Received=Array.Empty<int>();}
            int index=Array.IndexOf(c.Receipts,id),before=index>=0?c.Received[index]:0;
            if(total<before)return false;
            c.Earned=Math.Min(1000000000,c.Earned+total-before);c.HighestClear=Math.Max(c.HighestClear,clear);
            if(index>=0)c.Received[index]=total;
            else {c.Receipts=c.Receipts.Concat(new[]{id}).TakeLast(32).ToArray();c.Received=c.Received.Concat(new[]{total}).TakeLast(32).ToArray();}
            return true;
        }
    }
}
