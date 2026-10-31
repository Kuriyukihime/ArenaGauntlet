using System.Collections.Generic;
using UnityEngine;

namespace BazaarGauntlet
{
    // One continuous road skin: no overlapping landing boxes or raised end caps.
    internal static class FortressCausewayMesh
    {
        private sealed class Surface
        {
            private readonly List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>();
            private readonly List<Vector2> uv=new List<Vector2>();
            private readonly List<int> triangles=new List<int>();
            internal void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward)
            {
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),outward)<0){var swap=b;b=d;d=swap;}
                int start=vertices.Count;var normal=Vector3.Cross(b-a,c-a).normalized;
                foreach(var point in new[]{a,b,c,d})
                {
                    vertices.Add(point);normals.Add(normal);
                    uv.Add(Mathf.Abs(normal.y)>.5f?new Vector2(point.x/3,point.z/3):new Vector2((point.x+point.z)/3,point.y/3));
                }
                triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
            }
            internal Mesh Finish(string name)
            {
                var mesh=new Mesh{name="Causeway "+name};mesh.SetVertices(vertices);mesh.SetNormals(normals);
                mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();return mesh;
            }
        }
        private struct Section
        {
            internal Vector3 Center,Side;
            internal float HalfWidth,Thickness,Curb,Distance;
        }
        private static float Smooth(float t){t=Mathf.Clamp01(t);return t*t*(3-2*t);}
        private static Vector3 Side(Vector3 direction)=>Vector3.Cross(Vector3.up,direction).normalized;
        internal static IEnumerable<(string Name,Mesh Mesh)> Build(Vector3[] route,float width)
        {
            var stone=new Surface();var ivory=new Surface();var brass=new Surface();var glow=new Surface();
            var sections=new List<Section>();float total=0,distance=0;
            for(int i=1;i<route.Length;i++)total+=Vector3.Distance(route[i-1],route[i]);
            for(int segment=0;segment<route.Length-1;segment++)
            {
                Vector3 a=route[segment],b=route[segment+1];float length=Vector3.Distance(a,b);
                int rows=Mathf.CeilToInt(length/4);
                for(int row=segment==0?0:1;row<=rows;row++)
                {
                    float t=(float)row/rows,along=distance+length*t,remaining=total-along;
                    Vector3 side=Side(b-a);
                    if(row==rows && segment<route.Length-2)
                    {
                        var next=Side(route[segment+2]-b);var bisector=(side+next).normalized;
                        side=bisector/Mathf.Max(.6f,Vector3.Dot(bisector,side));
                    }
                    sections.Add(new Section{Center=Vector3.Lerp(a,b,t),Side=side,Distance=along,
                        HalfWidth=width*.5f+2*Smooth(1-remaining/12),
                        Thickness=.025f+.8f*Smooth(Mathf.Min(along,remaining)/8),
                        Curb=.2f*Smooth(Mathf.Min(along/10,(remaining-16)/12))});
                }
                distance+=length;
            }
            for(int i=1;i<sections.Count;i++)
            {
                var a=sections[i-1];var b=sections[i];
                Vector3 al=a.Center-a.Side*a.HalfWidth,ar=a.Center+a.Side*a.HalfWidth,
                    bl=b.Center-b.Side*b.HalfWidth,br=b.Center+b.Side*b.HalfWidth;
                Vector3 alb=al+a.Side*.35f-Vector3.up*a.Thickness,arb=ar-a.Side*.35f-Vector3.up*a.Thickness,
                    blb=bl+b.Side*.35f-Vector3.up*b.Thickness,brb=br-b.Side*.35f-Vector3.up*b.Thickness;
                stone.Quad(al,ar,br,bl,Vector3.up);stone.Quad(arb,alb,blb,brb,Vector3.down);
                foreach(float sign in new[]{-1f,1f})
                {
                    Vector3 edgeA=sign<0?al:ar,edgeB=sign<0?bl:br,baseA=sign<0?alb:arb,baseB=sign<0?blb:brb;
                    Vector3 outward=(a.Side+b.Side).normalized*sign;
                    stone.Quad(edgeA,edgeB,baseB,baseA,outward);
                    // Beveled ivory fascia follows the deck, never across an entrance.
                    ivory.Quad(edgeA+outward*.01f,edgeB+outward*.01f,baseB+outward*.01f,baseA+outward*.01f,outward);
                    Vector3 ca=a.Center+a.Side*sign*(a.HalfWidth-.55f),cb=b.Center+b.Side*sign*(b.HalfWidth-.55f);
                    if(a.Curb>.001f || b.Curb>.001f)
                    {
                        Vector3 highA=ca+Vector3.up*a.Curb,highB=cb+Vector3.up*b.Curb;
                        ivory.Quad(highA-a.Side*.18f,highA+a.Side*.18f,highB+b.Side*.18f,highB-b.Side*.18f,Vector3.up);
                        ivory.Quad(ca,cb,highB,highA,-outward);
                    }
                    // Thin inlays and inset blue markers use native castle materials.
                    Vector3 ia=ca-a.Side*sign*.75f+Vector3.up*.018f,ib=cb-b.Side*sign*.75f+Vector3.up*.018f;
                    brass.Quad(ia-a.Side*.07f,ia+a.Side*.07f,ib+b.Side*.07f,ib-b.Side*.07f,Vector3.up);
                    if(i%3==0 && b.Distance<total-18 && a.Distance>10)
                    {
                        Vector3 midpoint=Vector3.Lerp(ca,cb,.5f)+Vector3.up*.025f;
                        Vector3 forward=(b.Center-a.Center).normalized*.5f,lateral=outward*.08f;
                        glow.Quad(midpoint-forward-lateral,midpoint-forward+lateral,midpoint+forward+lateral,midpoint+forward-lateral,Vector3.up);
                    }
                }
                if(i==1)stone.Quad(ar,al,alb,arb,(a.Center-b.Center).normalized);
                if(i==sections.Count-1)stone.Quad(bl,br,brb,blb,(b.Center-a.Center).normalized);
            }
            yield return ("stone",stone.Finish("stone"));yield return ("ivory",ivory.Finish("ivory"));
            yield return ("brass",brass.Finish("brass"));yield return ("energy",glow.Finish("energy"));
        }
    }
}
