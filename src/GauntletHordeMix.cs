using System;

namespace BazaarGauntlet
{
    // Measures actual successful spawns, not attempted cards or native variants.
    // Failed placements get a short retry backoff instead of monopolizing a wave.
    internal sealed class GauntletHordeMix
    {
        private readonly int[] counts,weights,retryAfter;
        private int serial,lastPick=-1,lastSpawn=-1;
        private bool spawnedSincePick;
        internal GauntletHordeMix(int[] selectionWeights)
        {
            weights=(int[])selectionWeights.Clone();counts=new int[weights.Length];retryAfter=new int[weights.Length];
            for(int i=0;i<weights.Length;i++)weights[i]=Math.Max(1,weights[i]);
        }
        internal void Record(int index)
        {if(index<0 || index>=counts.Length)return;counts[index]++;lastSpawn=index;spawnedSincePick=true;}
        internal int Pick(bool[] available,int[] costs,float credits,int entropy)
        {
            serial++;
            if(lastPick>=0 && !spawnedSincePick)retryAfter[lastPick]=serial+3;
            int cheapest=int.MaxValue;bool affordable=false;
            for(int i=0;i<counts.Length;i++)if(available[i])
            {cheapest=Math.Min(cheapest,costs[i]);if(costs[i]<=credits)affordable=true;}
            bool Eligible(int i)=>available[i] && (affordable?costs[i]<=credits:costs[i]==cheapest);
            bool retryAlternative=false,otherThanLast=false;
            for(int i=0;i<counts.Length;i++)if(Eligible(i) && retryAfter[i]<=serial)retryAlternative=true;
            for(int i=0;i<counts.Length;i++)if(Eligible(i) && (!retryAlternative || retryAfter[i]<=serial) && i!=lastSpawn)otherThanLast=true;
            double best=double.PositiveInfinity;int selected=-1,ties=0;
            uint random=unchecked((uint)entropy);
            for(int i=0;i<counts.Length;i++)
            {
                if(!Eligible(i) || retryAlternative && retryAfter[i]>serial || otherThanLast && i==lastSpawn)continue;
                double score=(double)counts[i]/weights[i];
                if(score<best-1e-9){best=score;selected=i;ties=1;}
                else if(Math.Abs(score-best)<1e-9)
                {ties++;random=unchecked(random*1664525+1013904223);if(random%(uint)ties==0)selected=i;}
            }
            lastPick=selected;spawnedSincePick=false;return selected;
        }
    }
}
