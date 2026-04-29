using System;
using System.Collections.Generic;
using CustomMapRegions.Common.Networking;
using CustomMapRegions.Common.Models;
using CustomMapRegions.Config;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using CustomMapRegions.Extensions;
using System.Linq;
using Vintagestory.API.Datastructures;
using CustomMapRegions.Common;
using CustomMapRegions.Client.Abstractions;

namespace CustomMapRegions.Client.Storage;

public class RemoteClientStorage : AbstractClientStorage
{
    private IClientNetworkChannel _networkChannel;

    private object _chunksToGenLock = new();
    private UniqueQueue<FastVec2i> _chunksToGen = new();
    private object _chunksToSendLock = new();
    private UniqueQueue<ChunkRegionMsg> _chunksToSend = new();
    private object _regionsToSendLock = new();
    private UniqueQueue<RegionRequest> _regionsToSend = new();
    private object _readyChunksLock = new();
    private UniqueQueue<ChunkRegion> _readyChunks = new();

    public RemoteClientStorage(ICoreClientAPI capi, IClientNetworkChannel networkChannel) : base(capi)
    {
        _networkChannel = networkChannel;
        _networkChannel.SetMessageHandler<ChunkQueryResponse>(ChunkRegionsRecieved)
                    .SetMessageHandler<RegionResponse>(RegionsRecieved);
    }

    public override void QueryChunks(FastVec2i[] chunkCoords)
    {
        lock(_chunksToGen)
        {
            foreach (var chunkCoord in chunkCoords)
            {
                _chunksToGen.Enqueue(chunkCoord);
            }
        }
    }

    public override void CreateRegion(ChunkRegion newRegion)
    {
        _regionsToSend.Enqueue(new RegionRequest()
        {
            operation = MsgOperationEnum.Create,
            regionId = newRegion.Region.RegionId,
            chunkRegion = new ChunkRegion()
            {
                ChunkPos = newRegion.ChunkPos,
                Region = newRegion.Region
            }
        });
    }

    public override void UpdateRegion(Region region)
    {
        _regionsToSend.Enqueue(new RegionRequest()
        {
            operation = MsgOperationEnum.Update,
            regionId = region.RegionId,
            chunkRegion = new ChunkRegion()
            {
                Region = region
            }
        });
    }

    public override void DeleteRegion(Guid regionGuid)
    {
        _regionsToSend.Enqueue(new RegionRequest()
        {
            operation = MsgOperationEnum.Delete,
            regionId = regionGuid,
        });
    }

    public override void AddChunkToRegion(FastVec2i[] chunkCoords, Guid regionId)
    {
        lock(_chunksToSendLock)
        {
            for (int i = 0; i < chunkCoords.Length; i++)
            {
                _chunksToSend.Enqueue(new ChunkRegionMsg
                {
                    chunkCoords = chunkCoords[i],
                    regionId = regionId
                });
            }
        }
    }

    public override void DeleteChunkRegion(FastVec2i[] chunkCoords)
    {
        lock(_chunksToSendLock)
        {
            for (int i = 0; i < chunkCoords.Length; i++)
            {
                if(!_mapDB.CheckChunkPresent(chunkCoords[i])) continue;

                _chunksToSend.Enqueue(new ChunkRegionMsg
                {
                    chunkCoords = chunkCoords[i],
                    toDelete = true
                });
            }
        }
    }

    public override void OffThreadProcessQueues()
    {
        if(_chunksToGen.Count > 0)
        {
            lock(_chunksToGenLock)
            {
                _networkChannel.SendPacket(new ChunkQueryRequest()
                {
                    chunkCoords = _chunksToGen.Where(_mapDB.CheckChunkPresent).ToArray()
                });
                _chunksToGen.Clear();
            }
        }

        if(_chunksToSend.Count > 0)
        {
            lock(_chunksToSendLock)
            {
                _networkChannel.SendPacket(new ChunkRegionRequest()
                {
                    chunkRegions = _chunksToSend.Where(x => _mapDB.CheckChunkPresent(x.chunkCoords)).ToArray()
                });
                _chunksToSend.Clear();
            }
        }

        SharedUtils.SafeDequeueThrough(_regionsToSend, _regionsToSendLock, (RegionRequest request) =>
        {
            if(request.operation is MsgOperationEnum.Create 
                && !_mapDB.CheckChunkPresent(request.chunkRegion.ChunkPos))
                    return;

            _networkChannel.SendPacket(request);
        });

        if(_readyChunks.Count > 0)
        {
            lock(_readyChunksLock)
            {
                _api.Logger.Debug($"OffThread Remote: {_readyChunks.Count}");
                InvokeOnChunkRegionsReceived(_readyChunks.Where(x => _mapDB.CheckChunkPresent(x.ChunkPos)));
                _readyChunks.Clear();
            }
        }
    }

    public override void Dispose()
    {
    }

    private void ChunkRegionsRecieved(ChunkQueryResponse readyChunkRegions)
    {
        lock(_readyChunksLock)
        {
            _api.Logger.Debug($"Chunk Regions Recieved: {readyChunkRegions.chunkRegions.Length}");
            foreach (var chunkRegion in readyChunkRegions.chunkRegions)
            {
                _readyChunks.Enqueue(chunkRegion);
            }
        }
    }

    private void RegionsRecieved(RegionResponse response)
    {
        List<Region> updatedRegions = new();
        List<Guid> deletedRegions = new();

        foreach (var msg in response.regions)
        {
            switch(msg.operation)
            {
                case MsgOperationEnum.Update:
                    updatedRegions.Add(msg.region);
                    break;
                case MsgOperationEnum.Delete:
                    deletedRegions.Add(msg.regionId);
                    break;
            }
        }

        InvokeOnRegionsUpdated(updatedRegions);
        InvokeOnRegionsDeleted(deletedRegions);
    }
}