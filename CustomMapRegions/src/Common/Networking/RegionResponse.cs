using ProtoBuf;

namespace CustomMapRegions.Common.Networking;

[ProtoContract]
public struct RegionResponse
{
    [ProtoMember(1)]
    public RegionResponseMsg[] regions;
}