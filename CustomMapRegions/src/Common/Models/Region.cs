using System;
using ProtoBuf;

namespace CustomMapRegions.Common.Models;

[ProtoContract]
public class Region
{
    [ProtoMember(1)]
    public Guid RegionId { get; set; }
    [ProtoMember(2)]
    public string Name { get; set; }
    [ProtoMember(3)]
    public string Fill { get; set; }
    [ProtoMember(4)]
    public int Color { get; set; }
    [ProtoMember(5)]
    public string PlayerID { get; set; }
}