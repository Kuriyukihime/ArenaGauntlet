using RoR2;
using RoR2.UI;
using UnityEngine;

namespace BazaarGauntlet
{
    // PositionIndicator projects to depth 1, suitable for native world markers.
    // Canvas graphics must instead stay on their parent's HUD drawing plane:
    // otherwise the perspective UI camera magnifies them many times over.
    internal sealed class GauntletHudMarker:MonoBehaviour
    {
        internal HUD Hud;
        // Added after PositionIndicator so this runs after its native projection.
        private void OnEnable()=>UICamera.onUICameraPreCull+=PlaceOnCanvas;
        private void OnDisable()=>UICamera.onUICameraPreCull-=PlaceOnCanvas;
        private void PlaceOnCanvas(UICamera camera)
        {
            if(!Hud || !camera || camera.cameraRigController!=Hud.cameraRigController || !camera.camera)return;
            var parent=transform.parent as RectTransform;if(!parent)return;
            var view=camera.camera;
            var screen=view.WorldToScreenPoint(transform.position);
            if(RectTransformUtility.ScreenPointToWorldPointInRectangle(parent,screen,view,out var position))
                transform.position=position;
        }
    }
}
