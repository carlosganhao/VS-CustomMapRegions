using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace CustomMapRegions.Client;

public static class WorldMapContext
{
    public static HashSet<FastVec2i> KnownChunks = new HashSet<FastVec2i>();
}