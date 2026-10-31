using System;
using System.Collections.Generic;

namespace BazaarGauntlet
{
    internal static class GauntletRoutePlan
    {
        // Reverse traversal matters: a drop down a ledge is not a way back up.
        internal static bool[] Reaches(int count,int goal,IEnumerable<(int From,int To)> edges)
        {
            var result=new bool[count];if(goal<0 || goal>=count)return result;
            var incoming=new List<int>[count];for(int i=0;i<count;i++)incoming[i]=new List<int>();
            foreach(var edge in edges)
                if(edge.From>=0 && edge.From<count && edge.To>=0 && edge.To<count)incoming[edge.To].Add(edge.From);
            var queue=new Queue<int>();queue.Enqueue(goal);result[goal]=true;
            while(queue.Count>0)foreach(int previous in incoming[queue.Dequeue()])
                if(!result[previous]){result[previous]=true;queue.Enqueue(previous);}
            return result;
        }
    }
}
