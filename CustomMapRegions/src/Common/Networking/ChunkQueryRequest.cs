using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CustomMapRegions.Common.Networking;

[ProtoContract]
public struct ChunkQueryRequest
{
    [ProtoMember(1)]
    public FastVec2i[] chunkCoords;
}