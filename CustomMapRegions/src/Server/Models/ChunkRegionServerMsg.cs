using CustomMapRegions.Common.Networking;
using Vintagestory.API.Server;

namespace CustomMapRegions.Server.Models;

public struct ChunkRegionServerMsg
{
    public ChunkRegionMsg msg;
    public IServerPlayer fromPlayer;
    public uint attempt;
}