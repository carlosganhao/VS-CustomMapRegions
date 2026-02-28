using Vintagestory.API.MathTools;

namespace CustomMapRegions.Common;

public class ChunkRegion
{
    public FastVec2i ChunkPos { get; set; }
    public required Region Region { get; set; }
}