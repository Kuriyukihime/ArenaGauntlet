using System;
using System.Collections.Generic;
namespace BazaarGauntlet
{
    // Circular navigation stays inside the open fighting floor. Decorative
    // seating/arcades are never included in the monster spawn/path graph.
    internal sealed class FortressBattlefieldGrid
    {
        internal const int HalfSize=180;
        internal readonly int Step,Radius,Side;
        internal readonly float[] Heights;
        private readonly List<(int X,int Z)> cells=new List<(int,int)>();
        private readonly Dictionary<(int X,int Z),int> lookup=new Dictionary<(int,int),int>();
        internal int Count=>cells.Count*Heights.Length;
        internal FortressBattlefieldGrid(int extent,int step,params float[] heights)
        {
            if(extent<step || step<=0 || heights.Length==0)throw new ArgumentException("Invalid battlefield grid.");
            Step=step;Radius=extent/step;Side=Radius*2+1;Heights=(float[])heights.Clone();
            for(int z=-Radius;z<=Radius;z++)for(int x=-Radius;x<=Radius;x++)
                if(x*x+z*z<=Radius*Radius){lookup[(x,z)]=cells.Count;cells.Add((x,z));}
        }
        internal (float X,float Y,float Z) Position(int index)
        {var p=cells[index%cells.Count];return (p.X*Step,Heights[index/cells.Count],p.Z*Step);}
        internal IEnumerable<int> Neighbors(int index)
        {
            int layer=index/cells.Count;var cell=cells[index%cells.Count];
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                if((dx!=0 || dz!=0) && lookup.TryGetValue((cell.X+dx,cell.Z+dz),out int next))yield return layer*cells.Count+next;
            if(layer>0)yield return index-cells.Count;
            if(layer+1<Heights.Length)yield return index+cells.Count;
        }
        internal bool Edge(int index)
        {var p=Position(index);return p.X*p.X+p.Z*p.Z>=(Radius-1)*(Radius-1)*Step*Step;}
        internal static FortressBattlefieldGrid Ground()=>new FortressBattlefieldGrid(168,6,.06f);
        internal static FortressBattlefieldGrid Air()=>new FortressBattlefieldGrid(156,12,16,32,52);
    }
}
