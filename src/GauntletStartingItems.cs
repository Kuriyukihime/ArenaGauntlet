using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal sealed class GauntletStartingItems
    {
        private readonly GauntletStarterPlan<NetworkUserId,UniquePickup> plan=new GauntletStarterPlan<NetworkUserId,UniquePickup>();
        private readonly HashSet<NetworkUserId> warned=new HashSet<NetworkUserId>();
        private Xoroshiro128Plus rng;
        private float nextAttempt;
        internal void Tick(GauntletController owner)
        {
            if(!NetworkServer.active || !owner || GauntletController.Current!=owner || owner.ShuttingDown || !owner.ArenaReady || !owner.Fortress.InCastle ||
                owner.Run.isGameOverServer || owner.Run.NetworkinSceneTransition || Time.unscaledTime<nextAttempt)return;
            nextAttempt=Time.unscaledTime+1;
            if(rng==null)rng=new Xoroshiro128Plus(owner.Run.seed^0x53544152544552UL);
            foreach(var player in PlayerCharacterMasterController.instances)
            {
                var user=player && player.isConnected?player.networkUser:null;
                var master=user && user.isParticipating?user.master:null;
                var body=master?master.GetBody():null;
                var inventory=master?master.inventory:null;
                if(!user || !owner.Meta.Ready(user))continue;
                var career=owner.Meta.For(user);int slots=GauntletMetaPolicy.Slots(career.Career.Armory);
                if(!user || !body || !body.healthComponent || !body.healthComponent.alive || !inventory || inventory.inventoryDisabled ||
                    plan.Given(user.id)>=slots && career.EquipmentGiven)continue;
                try
                {
                    var whites=Eligible(owner.Run.availableTier1DropList,inventory,ItemTier.Tier1);
                    var greens=Eligible(owner.Run.availableTier2DropList,inventory,ItemTier.Tier2);
                    var choices=owner.Meta.Loadout(user);
                    while(plan.Next(user.id,()=>
                    {
                        int index=plan.Given(user.id);var selected=index<choices.Length?choices[index]:PickupIndex.none;
                        var def=PickupCatalog.GetPickupDef(selected);
                        return selected!=PickupIndex.none && def!=null && !inventory.IsItemDisabled(def.itemIndex)?new UniquePickup(selected):Roll(whites,greens);
                    },candidate=>candidate.isValid,out var pickup,slots))
                    {
                        var definition=PickupCatalog.GetPickupDef(pickup.pickupIndex);
                        long before=Count(inventory);
                        try {inventory.GiveItemPermanent(definition.itemIndex,1);}
                        catch
                        {
                            // An inventory hook may throw after applying the item.
                            // Count all stacks so corruption/conversion is covered.
                            if(Count(inventory)>before)plan.Confirm(user.id);
                            throw;
                        }
                        plan.Confirm(user.id);
                        // Notifications follow committed grants, so a failed UI
                        // message cannot cause the item to be granted twice.
                        GenericPickupController.SendPickupMessage(master,pickup,1);
                    }
                    if(!career.EquipmentGiven)
                    {
                        var equipment=choices.Length>slots?PickupCatalog.GetPickupDef(choices[slots]):null;
                        if(equipment!=null && equipment.equipmentIndex!=EquipmentIndex.None)
                        {
                            // Native equipment replication; never overwrite equipment already supplied by a survivor or another mod.
                            if(inventory.currentEquipmentIndex==EquipmentIndex.None)
                            {
                                try {inventory.SetEquipmentIndex(equipment.equipmentIndex,inventory.activeEquipmentLocation);}
                                finally {if(inventory.currentEquipmentIndex==equipment.equipmentIndex)career.EquipmentGiven=true;}
                            }
                        }
                        career.EquipmentGiven=true;
                    }
                    warned.Remove(user.id);
                }
                catch(Exception error)
                {if(warned.Add(user.id))GauntletPlugin.Log.LogWarning("Starter items will retry for a player: "+error);}
            }
            if(NetworkUser.AllParticipatingNetworkUsersReady() && plan.Announce(NetworkUser.readOnlyInstancesList
                .Where(user=>user && user.isParticipating).Select(user=>user.id),id=>
                {var user=NetworkUser.readOnlyInstancesList.FirstOrDefault(u=>u && u.id.Equals(id));return GauntletMetaPolicy.Slots(owner.Meta.For(user)?.Career.Armory??0);} ))
            {
                try {GauntletPortals.Announce("Starting items have been granted!");}
                catch(Exception error){GauntletPlugin.Log.LogWarning("Starter grant notification failed: "+error);}
            }
        }
        private static List<PickupIndex> Eligible(IEnumerable<PickupIndex> pool,Inventory inventory,ItemTier tier)=>pool.Where(index=>
        {
            var pickup=PickupCatalog.GetPickupDef(index);
            var item=pickup==null?null:ItemCatalog.GetItemDef(pickup.itemIndex);
            return item && !item.hidden && item.tier==tier && !item.ContainsTag(ItemTag.Scrap) && !item.ContainsTag(ItemTag.PriorityScrap) && !inventory.IsItemDisabled(item.itemIndex);
        }).Distinct().ToList();
        private UniquePickup Roll(List<PickupIndex> whites,List<PickupIndex> greens)
        {
            int tier=GauntletStarterPolicy.Tier(rng.nextNormalizedFloat,whites.Count,greens.Count);
            if(tier<0)return UniquePickup.none;
            var pool=tier==0?whites:greens;
            return new UniquePickup(pool[rng.RangeInt(0,pool.Count)]);
        }
        internal static long Count(Inventory inventory)
        {
            long total=0;
            foreach(var item in ItemCatalog.allItems)total+=Math.Max(0,inventory.GetItemCountPermanent(item));
            return total;
        }
    }
}
