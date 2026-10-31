using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal sealed class GauntletPaidLoot:MonoBehaviour {}
    internal static class GauntletShopLoot
    {
        internal static int PurchaseDepth;
        internal static void Install()
        {
            On.RoR2.PickupDropletController.CreatePickupDroplet_CreatePickupInfo_Vector3_Vector3+=Droplet;
            On.RoR2.PickupDropletController.CreatePickup+=Land;
        }
        private static void Droplet(On.RoR2.PickupDropletController.orig_CreatePickupDroplet_CreatePickupInfo_Vector3_Vector3 original,GenericPickupController.CreatePickupInfo info,Vector3 position,Vector3 velocity)
        {
            var owner=GauntletController.Current;
            var def=PickupCatalog.GetPickupDef(info.pickup.pickupIndex);
            var item=def==null?null:ItemCatalog.GetItemDef(def.itemIndex);
            bool paid=NetworkServer.active && owner && (PurchaseDepth>0 || item && item.ContainsTag(ItemTag.Scrap) && (position-GauntletShop.Position(GauntletShop.Scrapper)).sqrMagnitude<36);
            if(!paid){original(info,position,velocity);return;}
            var before=InstanceTracker.GetInstancesList<PickupDropletController>().ToArray();
            original(info,position,velocity);
            foreach(var droplet in InstanceTracker.GetInstancesList<PickupDropletController>().Where(value=>value && !before.Contains(value)))droplet.gameObject.AddComponent<GauntletPaidLoot>();
        }
        private static void Land(On.RoR2.PickupDropletController.orig_CreatePickup original,PickupDropletController droplet)
        {
            if(!droplet.GetComponent<GauntletPaidLoot>()){original(droplet);return;}
            var pickups=InstanceTracker.GetInstancesList<GenericPickupController>().ToArray();
            var pickers=InstanceTracker.GetInstancesList<PickupPickerController>().ToArray();
            original(droplet);
            foreach(var pickup in InstanceTracker.GetInstancesList<GenericPickupController>().Where(value=>value && !pickups.Contains(value)))pickup.gameObject.AddComponent<GauntletPaidLoot>();
            foreach(var picker in InstanceTracker.GetInstancesList<PickupPickerController>().Where(value=>value && !pickers.Contains(value)))picker.gameObject.AddComponent<GauntletPaidLoot>();
        }
    }
}
