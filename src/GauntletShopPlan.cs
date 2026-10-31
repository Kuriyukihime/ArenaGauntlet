using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarGauntlet
{
    // Server-owned votes and purchases. A changed party cancels the countdown.
    internal sealed class GauntletShopPlan
    {
        internal const int ItemChoices=6,LunarChoices=6,EquipmentChoices=6;
        internal const int LunarStart=ItemChoices,EquipmentStart=LunarStart+LunarChoices,DroneStart=EquipmentStart+EquipmentChoices;
        internal const int GreenTrade=DroneStart+3,RedTrade=GreenTrade+1,Scrapper=RedTrade+1,Ready=Scrapper+1,SlotCount=Ready+1;
        internal const int MaxPurchaseMask=(1<<SlotCount)-1;
        internal static bool ValidMask(int mask)=>mask>=0 && mask<=MaxPurchaseMask;
        internal static int ItemLimit(int tier)=>tier==0?3:tier==1?2:1;
        internal static (float X,float Y,float Z) Position(int slot)
        {
            if(slot<0 || slot>=SlotCount)throw new ArgumentOutOfRangeException(nameof(slot));
            if(slot<DroneStart)
            {
                int vendor=slot/ItemChoices,local=slot%ItemChoices;
                return (-24+vendor*12+(local%3-1)*3.5f,0,-177-(local/3)*4);
            }
            if(slot<GreenTrade)
            {
                int local=slot-DroneStart;
                return (12+(local-1)*3.5f,0,-179-(local==1?5:0));
            }
            if(slot==GreenTrade)return (21,0,-179);
            if(slot==RedTrade)return (27,0,-179);
            if(slot==Scrapper)return (24,0,-186);
            return (0,2.4f,-171);
        }
        private readonly HashSet<uint> ready=new HashSet<uint>();
        private readonly Dictionary<uint,Dictionary<int,int>> bought=new Dictionary<uint,Dictionary<int,int>>();
        private uint[] party=Array.Empty<uint>();
        private float startAt=-1;
        internal void Reset(){ready.Clear();bought.Clear();party=Array.Empty<uint>();startAt=-1;}
        internal bool Toggle(uint player)
        {startAt=-1;if(ready.Remove(player))return false;ready.Add(player);return true;}
        internal bool IsReady(uint player)=>ready.Contains(player);
        internal void Unready(uint player){if(ready.Remove(player))startAt=-1;}
        internal int Count(uint player,int slot)=>bought.TryGetValue(player,out var slots) && slots.TryGetValue(slot,out int count)?count:0;
        private int GroupCount(uint player,int slot,Func<int,int> group)
        {
            if(group==null)return Count(player,slot);
            if(!bought.TryGetValue(player,out var slots))return 0;
            int key=group(slot);
            return slots.Sum(purchase=>group(purchase.Key)==key?purchase.Value:0);
        }
        internal bool CanBuy(uint player,int slot,int limit=1,Func<int,int> group=null)=>slot>=0 && slot<SlotCount && GroupCount(player,slot,group)<Math.Max(0,limit);
        internal int Mask(uint player,Func<int,int> limit,Func<int,int> group=null)=>bought.ContainsKey(player)?
            Enumerable.Range(0,SlotCount).Aggregate(0,(mask,slot)=>CanBuy(player,slot,limit(slot),group)?mask:mask|(1<<slot)):0;
        internal bool Buy(uint player,int slot,int limit=1,Func<int,int> group=null)
        {
            if(!CanBuy(player,slot,limit,group))return false;
            if(!bought.TryGetValue(player,out var slots))bought[player]=slots=new Dictionary<int,int>();
            slots[slot]=Count(player,slot)+1;return true;
        }
        internal bool Tick(float now,IEnumerable<uint> connected,bool allowed)
        {
            var current=connected.Distinct().OrderBy(id=>id).ToArray();
            if(!party.SequenceEqual(current)){party=current;ready.IntersectWith(party);startAt=-1;}
            if(!allowed || party.Length==0 || !party.All(ready.Contains)){startAt=-1;return false;}
            if(startAt<0){startAt=now+3;return false;}
            return now>=startAt;
        }
        internal int ReadyCount=>party.Count(ready.Contains);
        internal int PlayerCount=>party.Length;
    }
}
