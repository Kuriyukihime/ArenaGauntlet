using RoR2;
using UnityEngine;

namespace BazaarGauntlet
{
    internal static class GauntletShopPresentation
    {
        internal static void Install()
        {
            On.RoR2.PurchaseInteraction.GetContextString+=Context;
            On.RoR2.PurchaseInteraction.UpdateHologramContent+=Hologram;
        }
        internal static bool IsTrade(GauntletShopTag tag)=>tag && (tag.Slot==GauntletShop.GreenTrade || tag.Slot==GauntletShop.RedTrade);
        internal static string CostLabel(bool red)=>red?"1 Red Scrap":"1 Green Scrap";
        internal static Color CostColor(bool red)=>red?new Color(1,.4f,.4f):new Color(.47f,.9f,.47f);
        private static string Context(On.RoR2.PurchaseInteraction.orig_GetContextString original,PurchaseInteraction purchase,Interactor actor)
        {
            var tag=purchase.GetComponent<GauntletShopTag>();
            if(!IsTrade(tag))return original(purchase,actor);
            bool red=tag.Slot==GauntletShop.RedTrade;
            return Language.GetString(purchase.contextToken)+" (<color=#"+ColorUtility.ToHtmlStringRGB(CostColor(red))+">"+CostLabel(red)+"</color>)";
        }
        private static void Hologram(On.RoR2.PurchaseInteraction.orig_UpdateHologramContent original,PurchaseInteraction purchase,GameObject hologram,Transform viewer)
        {
            original(purchase,hologram,viewer);
            var tag=purchase.GetComponent<GauntletShopTag>();var label=hologram.GetComponent<GauntletScrapHologram>();
            if(!IsTrade(tag))
            {
                if(label && label.enabled)
                {
                    label.enabled=false;
                    if(label.Content)
                    {
                        NativeAccess.Set(label.Content,"oldDisplayValue",int.MinValue);
                        NativeAccess.Call(label.Content,"FixedUpdate");
                    }
                }
                return;
            }
            var content=hologram.GetComponent<CostHologramContent>();if(!content)return;
            if(!label)label=hologram.AddComponent<GauntletScrapHologram>();
            label.Content=content;label.Red=tag.Slot==GauntletShop.RedTrade;label.enabled=true;label.Refresh();
        }
    }
    internal sealed class GauntletScrapHologram:MonoBehaviour
    {
        internal CostHologramContent Content;
        internal bool Red;
        // Native CostHologramContent writes the generic item cost in FixedUpdate.
        // Render the scrap-specific cost afterwards, on both host and clients.
        private void LateUpdate()=>Refresh();
        internal void Refresh()
        {
            if(!Content || !Content.targetTextMesh)return;
            // "1 Green Scrap" is longer than the native "$25"-style costs; never wrap or clip it.
            Content.targetTextMesh.enableWordWrapping=false;Content.targetTextMesh.overflowMode=TMPro.TextOverflowModes.Overflow;
            Content.targetTextMesh.text=GauntletShopPresentation.CostLabel(Red);
            Content.targetTextMesh.color=GauntletShopPresentation.CostColor(Red);
        }
    }
}
