using System.Reflection;
using Vintagestory.GameContent;

namespace CustomMapRegions.Extensions;

public static class ChunkMapLayerExtensions
{
    public static MapDB GetTerrainMapDb(this ChunkMapLayer layer)
    {
        FieldInfo mapField = layer.GetType().GetField("mapdb", BindingFlags.NonPublic | BindingFlags.IgnoreCase | BindingFlags.Instance);
        if(mapField is null) return null;

        object result = mapField.GetValue(layer);
        if(result is not MapDB map) return null;
        return map;
    }
}