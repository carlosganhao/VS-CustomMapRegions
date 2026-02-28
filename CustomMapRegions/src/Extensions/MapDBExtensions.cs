using System.Reflection;
using Microsoft.Data.Sqlite;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CustomMapRegions.Extensions;

public static class MapDBExtensions
{
    public static SqliteConnection sqliteConn;
    public static SqliteCommand existsChunkCommand;

    public static void SetupExtensionCommands(this MapDB mapDB)
    {
        FieldInfo mapField = mapDB.GetType().GetField("sqliteConn", BindingFlags.NonPublic | BindingFlags.IgnoreCase | BindingFlags.Instance);
        if(mapField is null) return;

        object result = mapField.GetValue(mapDB);
        sqliteConn = result as SqliteConnection;

        existsChunkCommand = sqliteConn.CreateCommand();
        existsChunkCommand.CommandText = "SELECT * FROM mappiece WHERE position = @pos LIMIT 1";
        existsChunkCommand.Parameters.Add("@pos", SqliteType.Integer);
    }

    public static bool CheckChunkPresent(this MapDB mapDB, FastVec2i chunkPos)
    {
        if(sqliteConn is null || existsChunkCommand is null) return false;

        existsChunkCommand.Parameters["@pos"].Value = chunkPos.ToChunkIndex();
        var result = existsChunkCommand.ExecuteScalar();
        return result is not null;
    }
}