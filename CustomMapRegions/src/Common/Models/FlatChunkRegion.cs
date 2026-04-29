using System;
using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CustomMapRegions.Common.Models;

[ProtoContract]
public class FlatChunkRegion
{
    [ProtoMember(1)]
    public FastVec2i ChunkPos { get; set; }
    [ProtoMember(2)]
    public Guid RegionId { get; set; }
}