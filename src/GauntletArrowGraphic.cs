using UnityEngine;
using UnityEngine.UI;

namespace BazaarGauntlet
{
    // A texture-free HUD arrow, pointing right with its tip at the target.
    internal sealed class GauntletArrowGraphic:Graphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Vector2[] points={Vector2.zero,new Vector2(-14,12),new Vector2(-14,5),new Vector2(-32,5),new Vector2(-32,-5),new Vector2(-14,-5),new Vector2(-14,-12)};
            foreach(var point in points)mesh.AddVert(point,color,Vector2.zero);
            mesh.AddTriangle(0,1,6);mesh.AddTriangle(4,5,2);mesh.AddTriangle(4,2,3);
        }
    }
}
