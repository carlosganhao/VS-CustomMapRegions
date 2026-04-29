using System;
using System.Collections.Generic;
using CustomMapRegions.Client.Abstractions;
using CustomMapRegions.Common;
using CustomMapRegions.Common.Models;
using CustomMapRegions.Common.Networking;
using CustomMapRegions.Config;
using CustomMapRegions.Extensions;
using CustomMapRegions.Infrastructure;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CustomMapRegions.Client.Storage;

public class LocalClientStorage : AbstractClientStorage
{
    private const int MaxGenRetries = 3;

    private object _chunksToGenLock = new();
    private UniqueQueue<FastVec2i> _chunksToGen = new();
    private object _chunksToSendLock = new();
    private UniqueQueue<ChunkRegionMsg> _chunksToSend = new();
    private object _regionsToSendLock = new();
    private UniqueQueue<RegionRequest> _regionsToSend = new();
    private object _chunksToRetryLock = new();
    private UniqueQueue<RetryChunkOp> _chunksToRetry = new();
    private UniqueQueue<ChunkRegion> _readyChunks = new();
    private List<Region> _updatedRegions = new();
    private List<Guid> _deletedRegions = new();

    private CustomMapRegionsConfig _config = ConfigManager.ConfigInstance;
    private RegionDB _regionDB;
    public string getRegionDbFilePath()
    {
        string path = System.IO.Path.Combine(GamePaths.DataPath, "Maps");
        GamePaths.EnsurePathExists(path);

        return System.IO.Path.Combine(path, _api.World.SavegameIdentifier + "-regions.db");
    }

    public LocalClientStorage(ICoreClientAPI capi) : base(capi)
    {
        capi.Event.ChunkDirty += OnChunkDirty;

        _regionDB = new RegionDB(capi.World.Logger);
        string? errorMessage = null;
        string regionDbFilePath = getRegionDbFilePath();
        _regionDB.OpenOrCreate(regionDbFilePath, ref errorMessage, true, true, false);
        if (errorMessage != null)
        {
            throw new Exception(string.Format("Cannot open {0}, possibly corrupted. Please fix manually or delete this file to continue playing", regionDbFilePath));
        }
    }

    public override void QueryChunks(FastVec2i[] chunkCoords)
    {
        lock (_chunksToGenLock)
        {
            foreach (var coords in chunkCoords)
            {
                _chunksToGen.Enqueue(coords);
            }
        }
    }

    public override void CreateRegion(ChunkRegion newRegion)
    {
        lock(_regionsToSendLock)
        {
            _regionsToSend.Enqueue(new RegionRequest() {
                operation = MsgOperationEnum.Create,
                chunkRegion = newRegion,
            });
        }
    }

    public override void UpdateRegion(Region region)
    {
        _regionDB.UpdateRegion(region);
        InvokeOnRegionsUpdated([region]);
    }

    public override void DeleteRegion(Guid regionId)
    {
        _regionDB.DeleteRegion(regionId);
        InvokeOnRegionsDeleted([regionId]);
    }

    public override void AddChunkToRegion(FastVec2i[] chunkCoords, Guid regionId)
    {
        lock(_chunksToSendLock)
        {
            foreach (var coord in chunkCoords)
            {
                _chunksToSend.Enqueue(new ChunkRegionMsg() {chunkCoords = coord, regionId = regionId});
            }
        }
    }

    public override void DeleteChunkRegion(FastVec2i[] chunkCoords)
    {
        lock(_chunksToSendLock)
        {
            foreach (var coord in chunkCoords)
            {
                _chunksToSend.Enqueue(new ChunkRegionMsg() {toDelete = true, chunkCoords = coord});
            }
        }
    }

    public override void OffThreadProcessQueues()
    {
        SharedUtils.SafeDequeueThrough(_regionsToSend, _regionsToSendLock, (RegionRequest op) =>
        {
            switch(op.operation)
            {
                case MsgOperationEnum.Create:
                    if(!_mapDB.CheckChunkPresent(op.chunkRegion.ChunkPos)) return;
                    _regionDB.CreateNewRegion(op.chunkRegion);

                    lock(_chunksToGenLock)
                    {
                        _chunksToGen.Enqueue(op.chunkRegion.ChunkPos);
                    }
                    break;
                case MsgOperationEnum.Update:
                    _regionDB.UpdateRegion(op.chunkRegion.Region);
                    _updatedRegions.Add(op.chunkRegion.Region);
                    break;
                case MsgOperationEnum.Delete:
                    _regionDB.DeleteRegion(op.regionId);
                    _deletedRegions.Add(op.regionId);
                    break;
            }
        });

        SharedUtils.SafeDequeueThrough(_chunksToSend, _chunksToSendLock, (ChunkRegionMsg op) =>
        {
            if(op.toDelete)
            {
                _regionDB.DeleteChunkRegion(op.chunkCoords);
            }
            else
            {
                if(!_mapDB.CheckChunkPresent(op.chunkCoords)) return;
                _regionDB.AddChunkToRegion(op.chunkCoords, op.regionId);
            }

            lock(_chunksToGenLock)
            {
                _chunksToGen.Enqueue(op.chunkCoords);
            }
        });

        SharedUtils.SafeDequeueThrough(_chunksToRetry, _chunksToRetryLock, (RetryChunkOp op) =>
        {
            if(op.tries < MaxGenRetries)
            {
                if(GenerateChunk(op.chunkCoords) || _config.DisableChunkRetries) return;

                op.tries++;
                lock(_chunksToRetryLock)
                {
                    _chunksToRetry.Enqueue(op);
                }
            }
        });

        SharedUtils.SafeDequeueThrough(_chunksToGen, _chunksToGenLock, (FastVec2i chunkCoords) => GenerateChunk(chunkCoords));

        if(_readyChunks.Count > 0)
        {
            InvokeOnChunkRegionsReceived(_readyChunks);
            _readyChunks.Clear();
        }

        bool GenerateChunk(FastVec2i chunkCoords)
        {
            if(!_mapDB.CheckChunkPresent(chunkCoords) && !WorldMapContext.KnownChunks.Contains(chunkCoords)) return false;
            var chunkRegion = _regionDB.GetChunkRegion(chunkCoords);
            if (chunkRegion != null)
            {
                _readyChunks.Enqueue(chunkRegion);
                return true;
            }

            return false;
        }
    }

    public override void Dispose()
    {
        _regionDB?.Dispose();
    }

    private void OnChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
    {
        if(reason == EnumChunkDirtyReason.MarkedDirty) return;
        if(chunkCoord.Y > 0) return;
        lock (_chunksToRetryLock)
        {
            _chunksToRetry.Enqueue(new RetryChunkOp(chunkCoord));
        }
    }

    private struct RetryChunkOp
    {
        public RetryChunkOp(Vec3i chunkCoords)
        {
            this.chunkCoords = new FastVec2i(chunkCoords.X, chunkCoords.Z);
        }

        public int tries;
        public FastVec2i chunkCoords;
    }
}