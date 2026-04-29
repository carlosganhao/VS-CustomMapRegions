using System;
using CustomMapRegions.Common.Models;
using Microsoft.Data.Sqlite;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CustomMapRegions.Infrastructure;

public class RegionDB : SQLiteDBConnection
{
    public override string DBTypeCode => "region database";

    public RegionDB(ILogger logger) : base(logger) {}

    SqliteCommand createRegionCmd;
    SqliteCommand updateRegionCmd;
    SqliteCommand getRegionCmd;
    SqliteCommand setChunkToRegionCmd;
    SqliteCommand getChunkRegionCmd;
    SqliteCommand deleteRegionCmd;
    SqliteCommand deleteChunkCmd;

    public override void OnOpened()
    {
        base.OnOpened();

        createRegionCmd = sqliteConn.CreateCommand();
        createRegionCmd.CommandText = "INSERT INTO region (id, name, color, fill, playerId) VALUES (@id, @name, @color, @fill, @playerId) RETURNING id";
        createRegionCmd.Parameters.Add("@id", SqliteType.Blob);
        createRegionCmd.Parameters.Add("@name", SqliteType.Text);
        createRegionCmd.Parameters.Add("@color", SqliteType.Integer);
        createRegionCmd.Parameters.Add("@fill", SqliteType.Text);
        createRegionCmd.Parameters.Add("@playerId", SqliteType.Text);
        createRegionCmd.Prepare();

        updateRegionCmd = sqliteConn.CreateCommand();
        updateRegionCmd.CommandText = "UPDATE region SET name = @name, color = @color, fill = @fill WHERE id = @id";
        updateRegionCmd.Parameters.Add("@id", SqliteType.Blob);
        updateRegionCmd.Parameters.Add("@name", SqliteType.Text);
        updateRegionCmd.Parameters.Add("@color", SqliteType.Integer);
        updateRegionCmd.Parameters.Add("@fill", SqliteType.Text);
        updateRegionCmd.Prepare();

        getRegionCmd = sqliteConn.CreateCommand();
        getRegionCmd.CommandText = "SELECT * FROM region WHERE id = @id";
        getRegionCmd.Parameters.Add("@id", SqliteType.Integer);
        getRegionCmd.Prepare();

        setChunkToRegionCmd = sqliteConn.CreateCommand();
        setChunkToRegionCmd.CommandText = "INSERT OR REPLACE INTO chunkRegion (chunkId, regionId) VALUES (@chunk, @region)";
        setChunkToRegionCmd.Parameters.Add("@chunk", SqliteType.Integer);
        setChunkToRegionCmd.Parameters.Add("@region", SqliteType.Blob);
        setChunkToRegionCmd.Prepare();

        getChunkRegionCmd = sqliteConn.CreateCommand();
        getChunkRegionCmd.CommandText = "SELECT chunkRegion.chunkId AS chunkId, region.id AS regionId, region.name as name, region.color as color, region.fill as fill, region.playerId as playerId FROM chunkRegion INNER JOIN region ON chunkRegion.regionId = region.id WHERE chunkId = @chunk";
        getChunkRegionCmd.Parameters.Add("@chunk", SqliteType.Integer);
        getChunkRegionCmd.Prepare();

        deleteRegionCmd = sqliteConn.CreateCommand();
        deleteRegionCmd.CommandText = "DELETE FROM region WHERE id = @regionId";
        deleteRegionCmd.Parameters.Add("@regionId", SqliteType.Blob);
        deleteRegionCmd.Prepare();

        deleteChunkCmd = sqliteConn.CreateCommand();
        deleteChunkCmd.CommandText = "DELETE FROM chunkRegion WHERE chunkId = @chunk";
        deleteChunkCmd.Parameters.Add("@chunk", SqliteType.Integer);
        deleteChunkCmd.Prepare();
    }

    public Guid CreateNewRegion(ChunkRegion chunkRegion)
    {
        using var transaction = sqliteConn.BeginTransaction();

        createRegionCmd.Transaction = transaction;
        createRegionCmd.Parameters["@id"].Value = chunkRegion.Region.RegionId.ToByteArray();
        createRegionCmd.Parameters["@name"].Value = chunkRegion.Region.Name;
        createRegionCmd.Parameters["@color"].Value = chunkRegion.Region.Color;
        createRegionCmd.Parameters["@fill"].Value = chunkRegion.Region.Fill;
        createRegionCmd.Parameters["@playerId"].Value = chunkRegion.Region.PlayerID ?? string.Empty;
        createRegionCmd.ExecuteNonQuery();

        ExecuteAddChunkToRegion(transaction, chunkRegion.ChunkPos, chunkRegion.Region.RegionId);

        transaction.Commit();

        return chunkRegion.Region.RegionId;
    }

    public void UpdateRegion(Region region)
    {
        using var transaction = sqliteConn.BeginTransaction();

        updateRegionCmd.Transaction = transaction;
        updateRegionCmd.Parameters["@id"].Value = region.RegionId.ToByteArray();
        updateRegionCmd.Parameters["@name"].Value = region.Name;
        updateRegionCmd.Parameters["@color"].Value = region.Color;
        updateRegionCmd.Parameters["@fill"].Value = region.Fill;
        updateRegionCmd.ExecuteNonQuery();

        transaction.Commit();
    }

    public Region GetRegion(Guid regionId)
    {
        getRegionCmd.Parameters["@id"].Value = regionId.ToByteArray();

        using var reader = getRegionCmd.ExecuteReader();
        while(reader.Read())
        {
            var colorLong = (long)reader["color"];
            var playerId = reader["playerId"]; 
            return new Region()
            {
                RegionId = regionId,
                Name = (string)reader["name"],
                Color = (int)colorLong,
                Fill = (string)reader["fill"],
                PlayerID = playerId is not DBNull ? (string)playerId : string.Empty,
            };
        };

        return null;
    }

    public void AddChunkToRegion(FastVec2i chunkCoords, Guid regionId)
    {
        using var transaction = sqliteConn.BeginTransaction();

        ExecuteAddChunkToRegion(transaction, chunkCoords, regionId);

        transaction.Commit();
    }

    public ChunkRegion GetChunkRegion(FastVec2i chunkCoords)
    {
        getChunkRegionCmd.Parameters["@chunk"].Value = chunkCoords.ToChunkIndex();

        using var reader = getChunkRegionCmd.ExecuteReader();
        while(reader.Read())
        {
            var chunkIndex = (long)reader["chunkId"];
            var colorLong = (long)reader["color"];
            var playerId = reader["playerId"]; 
            return new ChunkRegion()
            {
                ChunkPos = new FastVec2i((int)(chunkIndex & 0x7ffffff), (int)((chunkIndex & 0x3ffffff8000000) >> 27)),
                Region = new Region()
                {
                    RegionId = new Guid((byte[])reader["regionId"]),
                    Name = (string)reader["name"],
                    Color = (int)colorLong,
                    Fill = (string)reader["fill"],
                    PlayerID = playerId is not DBNull ? (string)playerId : string.Empty,
                }
            };
        }

        return new ChunkRegion()
        {
            ChunkPos = chunkCoords,
            Region = new(),
        };
    }

    public void DeleteChunkRegion(FastVec2i chunkCoords)
    {
        using var transaction = sqliteConn.BeginTransaction();

        deleteChunkCmd.Transaction = transaction;
        deleteChunkCmd.Parameters["@chunk"].Value = chunkCoords.ToChunkIndex();
        deleteChunkCmd.ExecuteNonQuery();

        transaction.Commit();
    }

    public void DeleteRegion(Guid regionId)
    {
        using var transaction = sqliteConn.BeginTransaction();

        deleteRegionCmd.Transaction = transaction;
        deleteRegionCmd.Parameters["@regionId"].Value = regionId.ToByteArray();
        deleteRegionCmd.ExecuteNonQuery();

        transaction.Commit();
    }

    protected override void CreateTablesIfNotExists(SqliteConnection sqliteConn)
    {
        // 0 - Create Regions Table
        using (var sqlite_cmd = sqliteConn.CreateCommand())
        {
            sqlite_cmd.CommandText = "CREATE TABLE IF NOT EXISTS region (id blob PRIMARY KEY, name text, color integer, fill text) ";
            sqlite_cmd.ExecuteNonQuery();
        }

        // 0 - Create ChunkRegion Relation Table
        using (var sqlite_cmd = sqliteConn.CreateCommand())
        {
            sqlite_cmd.CommandText = "CREATE TABLE IF NOT EXISTS chunkRegion (chunkId integer PRIMARY KEY, regionId blob, FOREIGN KEY(regionId) REFERENCES region(id) ON DELETE CASCADE)";
            sqlite_cmd.ExecuteNonQuery();
        }

        // 1 - Add PlayerUID to Region Table
        using (var sqlite_cmd = sqliteConn.CreateCommand())
        {
            sqlite_cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('region') WHERE name = 'playerId'";
            long colCount = (long?)sqlite_cmd.ExecuteScalar() ?? 0;

            if(colCount < 1)
            {
                sqlite_cmd.CommandText = "ALTER TABLE region ADD COLUMN playerId text";
                sqlite_cmd.ExecuteNonQuery();
            }
        }
    }

    public override void Close()
    {
        Cleanup();
        base.Close();
    }

    public override void Dispose()
    {
        Cleanup();
        base.Dispose();
    }

    private void ExecuteAddChunkToRegion(SqliteTransaction transaction, FastVec2i chunkCoords, Guid regionId)
    {
        setChunkToRegionCmd.Transaction = transaction;
        setChunkToRegionCmd.Parameters["@chunk"].Value = chunkCoords.ToChunkIndex();
        setChunkToRegionCmd.Parameters["@region"].Value = regionId.ToByteArray();
        setChunkToRegionCmd.ExecuteNonQuery();
    }

    private void Cleanup()
    {
        createRegionCmd?.Dispose();
        updateRegionCmd?.Dispose();
        getRegionCmd?.Dispose();
        setChunkToRegionCmd?.Dispose();
        getChunkRegionCmd?.Dispose();
        deleteRegionCmd?.Dispose();
        deleteChunkCmd?.Dispose();
    }
}