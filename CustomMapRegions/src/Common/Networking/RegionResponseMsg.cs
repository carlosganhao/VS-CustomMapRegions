using System;
using CustomMapRegions.Common.Models;
using ProtoBuf;

namespace CustomMapRegions.Common.Networking;

[ProtoContract]
public struct RegionResponseMsg
{
    [ProtoMember(1)]
    public MsgOperationEnum operation;
    [ProtoMember(2)]
    public Guid regionId;
    [ProtoMember(3)]
    public Region region;
}