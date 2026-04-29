using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CustomMapRegions.Common.Models;

[ProtoContract]
public class ChunkRegion
{
    [ProtoMember(1)]
    public FastVec2i ChunkPos { get; set; }
    [ProtoMember(2)]
    public required Region Region { get; set; }
}