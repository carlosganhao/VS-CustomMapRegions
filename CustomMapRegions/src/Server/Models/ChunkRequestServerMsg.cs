using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CustomMapRegions.Server.Models;

public struct ChunkRequestServerMsg
{
    public FastVec2i chunkCoords;
    public IServerPlayer fromPlayer;
}