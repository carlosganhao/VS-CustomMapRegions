using ProtoBuf;

namespace CustomMapRegions.Common.Networking;

[ProtoContract]
public struct ChunkRegionRequest
{
    [ProtoMember(1)]
    public ChunkRegionMsg[] chunkRegions;
}