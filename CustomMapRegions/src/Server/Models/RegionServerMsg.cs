using CustomMapRegions.Common.Networking;
using Vintagestory.API.Server;

namespace CustomMapRegions.Server.Models;

public struct RegionServerMsg
{
    public RegionRequest msg;
    public IServerPlayer fromPlayer;
}