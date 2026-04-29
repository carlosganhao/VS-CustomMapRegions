using CustomMapRegions.Common.Models;
using ProtoBuf;

namespace CustomMapRegions.Common.Networking;

[ProtoContract]
public struct ChunkQueryResponse
{
    [ProtoMember(1)]
    public ChunkRegion[] chunkRegions;
}