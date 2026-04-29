using ProtoBuf;

namespace CustomMapRegions.Common.Networking;

[ProtoContract]
public enum MsgOperationEnum
{
    Create,
    Update,
    Delete,
}