using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CustomMapRegions.Common.Models;
using CustomMapRegions.Extensions;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CustomMapRegions.Client.Abstractions;

public abstract class AbstractClientStorage
{
    protected readonly ICoreClientAPI _api;
    protected Thread _processThread;
    protected MapDB _mapDB;
    public virtual event Action<IEnumerable<ChunkRegion>> onChunkRegionsRecieved;
    public virtual event Action<IEnumerable<Region>> onRegionsUpdated;
    public virtual event Action<IEnumerable<Guid>> onRegionsDeleted;

    public AbstractClientStorage(ICoreClientAPI api)
    {
        _api = api;
        var mapManager = _api.ModLoader.GetModSystem<WorldMapManager>();
        var chunkLayer = mapManager.MapLayers.Find(x => x is ChunkMapLayer) as ChunkMapLayer;
        if (chunkLayer != null)
        {
            _mapDB = chunkLayer.GetTerrainMapDb();
            _mapDB.SetupExtensionCommands();
        }
    }

    public abstract void QueryChunks(FastVec2i[] chunkCoords);
    public abstract void CreateRegion(ChunkRegion newRegion);
    public abstract void UpdateRegion(Region region);
    public abstract void DeleteRegion(Guid regionId);
    public abstract void AddChunkToRegion(FastVec2i[] chunkCoords, Guid regionId);
    public abstract void DeleteChunkRegion(FastVec2i[] chunkCoords);
    public abstract void OffThreadProcessQueues();
    public abstract void Dispose();

    protected void InvokeOnChunkRegionsReceived(IEnumerable<ChunkRegion> readyChunks)
    {
        onChunkRegionsRecieved?.Invoke(readyChunks);
    }

    protected void InvokeOnRegionsUpdated(IEnumerable<Region> regions)
    {
        onRegionsUpdated?.Invoke(regions);
    }

    protected void InvokeOnRegionsDeleted(IEnumerable<Guid> regionIds)
    {
        onRegionsDeleted?.Invoke(regionIds);
    }
}