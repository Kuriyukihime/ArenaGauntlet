using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarGauntlet
{
    internal static class GauntletStarterPolicy
    {
        internal const int Items=3;
        internal static int Tier(double roll,int whites,int greens)=>whites<=0?(greens>0?1:-1):greens<=0?0:roll<.8?0:1;
    }
    // Run-owned and keyed by the native account/subplayer identity. Pending
    // rolls survive retries; respawns and reconnects retain the granted count.
    internal sealed class GauntletStarterPlan<TKey,TItem>
    {
        private sealed class Grant {internal int Given;internal bool Pending;internal TItem Item;}
        private readonly Dictionary<TKey,Grant> grants=new Dictionary<TKey,Grant>();
        private readonly HashSet<TKey> announced=new HashSet<TKey>();
        internal int Given(TKey key)=>grants.TryGetValue(key,out var grant)?grant.Given:0;
        internal bool Next(TKey key,Func<TItem> roll,Func<TItem,bool> valid,out TItem item,int count=GauntletStarterPolicy.Items)
        {
            item=default;
            if(!grants.TryGetValue(key,out var grant)){grant=new Grant();grants.Add(key,grant);}
            if(grant.Given>=count)return false;
            if(!grant.Pending)
            {
                var candidate=roll();if(!valid(candidate))return false;
                grant.Item=candidate;grant.Pending=true;
            }
            item=grant.Item;return true;
        }
        internal bool Confirm(TKey key)
        {
            if(!grants.TryGetValue(key,out var grant) || !grant.Pending)return false;
            grant.Pending=false;grant.Given++;return true;
        }
        internal bool Announce(IEnumerable<TKey> participants,Func<TKey,int> count=null)
        {
            var party=participants.Distinct().ToArray();
            if(party.Length==0 || party.Any(key=>Given(key)<(count?.Invoke(key)??GauntletStarterPolicy.Items)) || party.All(announced.Contains))return false;
            foreach(var key in party)announced.Add(key);
            return true;
        }
    }
}
