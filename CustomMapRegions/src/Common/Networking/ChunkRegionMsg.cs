using System;
using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CustomMapRegions.Common.Networking;

[ProtoContract]
public struct ChunkRegionMsg
{
    [ProtoMember(1)]
    public bool toDelete;
    [ProtoMember(2)]
    public FastVec2i chunkCoords;
    [ProtoMember(3)]
    public Guid regionId;
}