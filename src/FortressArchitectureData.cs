using System;
using System.IO;

namespace BazaarGauntlet
{
    // Plain binary recipe shared by the game loader and offline checks.
    internal sealed class FortressArchitectureData
    {
        internal sealed class Surface
        {
            internal string Name;internal float R,G,B,Smoothness,Emission;
        }
        internal sealed class Condition
        {
            internal int Upgrade,Minimum,Maximum;
        }
        internal sealed class Part
        {
            internal string Name;internal int Module,Attachment,Surface;internal bool Ruin;
            internal Condition[] Conditions;internal float[] Vertices;internal int[] Indices;
            internal bool Visible(FortressState state)
            {
                if(!state.Built(Module) || state.Modules[Module].Destroyed!=Ruin)return false;
                foreach(var condition in Conditions)
                {int rank=state.Rank((FortressUpgrade)condition.Upgrade);if(rank<condition.Minimum || rank>condition.Maximum)return false;}
                return true;
            }
        }
        internal Surface[] Surfaces;internal Part[] Parts;
        private static ulong Mix(ulong hash,int value)=>unchecked((hash^(uint)value)*1099511628211UL);
        internal static ulong AppearanceKey(FortressState state)
        {
            ulong hash=14695981039346656037UL;
            foreach(int rank in state.Ranks)hash=Mix(hash,rank);
            for(int i=0;i<state.Modules.Length;i++)
            {hash=Mix(hash,state.Built(i)?1:0);hash=Mix(hash,state.Modules[i].Destroyed?1:0);hash=Mix(hash,state.Modules[i].GroundOffset.GetHashCode());}
            hash=Mix(hash,state.X.GetHashCode());hash=Mix(hash,state.Y.GetHashCode());hash=Mix(hash,state.Z.GetHashCode());
            return Mix(hash,state.Yaw.GetHashCode());
        }
        internal static FortressArchitectureData Read(Stream stream)
        {
            using(var reader=new BinaryReader(stream,System.Text.Encoding.UTF8,true))
            {
                if(new string(reader.ReadChars(5))!="CAST1")throw new InvalidDataException("Castle mesh format.");
                int surfaces=reader.ReadInt32();if(surfaces<1 || surfaces>24)throw new InvalidDataException("Castle material count.");
                var data=new FortressArchitectureData{Surfaces=new Surface[surfaces]};
                for(int i=0;i<surfaces;i++)data.Surfaces[i]=new Surface{Name=reader.ReadString(),R=reader.ReadSingle(),G=reader.ReadSingle(),B=reader.ReadSingle(),Smoothness=reader.ReadSingle(),Emission=reader.ReadSingle()};
                int count=reader.ReadInt32();if(count<1 || count>2048)throw new InvalidDataException("Castle part count.");
                data.Parts=new Part[count];
                for(int i=0;i<count;i++)
                {
                    var part=new Part{Name=reader.ReadString(),Module=reader.ReadInt32(),Attachment=reader.ReadInt32(),Surface=reader.ReadInt32(),Ruin=reader.ReadBoolean()};
                    if(part.Module<0 || part.Module>=FortressTree.ModuleCount || part.Attachment<0 || part.Attachment>2 || part.Surface<0 || part.Surface>=surfaces)throw new InvalidDataException("Castle part assignment.");
                    int conditions=reader.ReadInt32();if(conditions<0 || conditions>8)throw new InvalidDataException("Castle part conditions.");
                    part.Conditions=new Condition[conditions];
                    for(int j=0;j<conditions;j++)
                    {
                        var condition=new Condition{Upgrade=reader.ReadInt32(),Minimum=reader.ReadInt32(),Maximum=reader.ReadInt32()};
                        if(condition.Upgrade<0 || condition.Upgrade>=(int)FortressUpgrade.Count || condition.Minimum<0 || condition.Maximum<condition.Minimum)throw new InvalidDataException("Castle rank condition.");
                        part.Conditions[j]=condition;
                    }
                    int vertices=reader.ReadInt32(),indices=reader.ReadInt32();
                    if(vertices<3 || vertices>250000 || indices<3 || indices>1500000 || indices%3!=0)throw new InvalidDataException("Castle geometry limits.");
                    part.Vertices=new float[vertices*8];part.Indices=new int[indices];
                    for(int j=0;j<part.Vertices.Length;j++)
                    {float value=reader.ReadSingle();if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("Non-finite castle geometry.");part.Vertices[j]=value;}
                    for(int j=0;j<indices;j++){int index=reader.ReadInt32();if(index<0 || index>=vertices)throw new InvalidDataException("Castle mesh index.");part.Indices[j]=index;}
                    data.Parts[i]=part;
                }
                if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing castle mesh data.");
                return data;
            }
        }
    }
}
