using CustomMapRegions.Client;
using HarmonyLib;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Client;

[HarmonyPatch(typeof(MultiChunkMapComponent))]
public static class MultiChunkMapComponentPatches
{
    [HarmonyPatch(nameof(MultiChunkMapComponent.setChunk))]
    [HarmonyPostfix]
    static void SetChunkPostfix(int dx, int dz, int[] pixels, FastVec2i ___chunkCoord, ICoreClientAPI ___capi)
    {
        ___capi.Logger.Debug($"Cached Chunk: X: {___chunkCoord.X + dx}, Y: {___chunkCoord.Y + dz}");
        WorldMapContext.KnownChunks.Add(new FastVec2i(___chunkCoord.X + dx, ___chunkCoord.Y + dz));
    }

    [HarmonyPatch(nameof(MultiChunkMapComponent.unsetChunk))]
    [HarmonyPostfix]
    static void UnsetChunkPostfix(int dx, int dz, FastVec2i ___chunkCoord, ICoreClientAPI ___capi)
    {
        ___capi.Logger.Debug($"Uncached Chunk: X: {___chunkCoord.X + dx}, Y: {___chunkCoord.Y + dz}");
        WorldMapContext.KnownChunks.Remove(new FastVec2i(___chunkCoord.X + dx, ___chunkCoord.Y + dz));
    }

    [HarmonyPatch(nameof(MultiChunkMapComponent.ActuallyDispose))]
    [HarmonyPostfix]
    static void ActuallyDisposePostfix(FastVec2i ___chunkCoord)
    {
        for(int dx = 0; dx < MultiChunkMapComponent.ChunkLen; dx++)
        {
            for(int dz = 0; dz < MultiChunkMapComponent.ChunkLen; dz++)
            {
                WorldMapContext.KnownChunks.Remove(new FastVec2i(___chunkCoord.X + dx, ___chunkCoord.Y + dz));
            }
        }
    }
}