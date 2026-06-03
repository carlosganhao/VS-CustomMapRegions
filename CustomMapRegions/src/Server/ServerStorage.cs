using System;
using System.Linq;
using System.Collections.Generic;
using CustomMapRegions.Common.Networking;
using CustomMapRegions.Common.Models;
using CustomMapRegions.Common;
using CustomMapRegions.Config;
using CustomMapRegions.Infrastructure;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using System.Threading;
using Vintagestory.API.Common;
using CustomMapRegions.Server.Models;
using Vintagestory.API.MathTools;

namespace CustomMapRegions.Server;

public class ServerStorage
{
    private const uint MaxAttempts = 3;

    private ICoreServerAPI _api;
    private IServerNetworkChannel _networkChannel;
    private Thread _processThread;

    private object _chunksReceivedLock = new();
    private UniqueQueue<ChunkRegionServerMsg> _chunksReceived = new();
    private object _regionsReceivedLock = new();
    private UniqueQueue<RegionServerMsg> _regionsReceived = new();
    private object _chunksRequestedLock = new();
    private UniqueQueue<ChunkRequestServerMsg> _chunksRequested = new();
    private object _chunksToPushLock = new();
    private UniqueQueue<ChunkRequestServerMsg> _chunksToPush = new();
    private object _chunksToRetryLock = new();
    private UniqueQueue<ChunkRequestServerMsg> _chunksToRetry = new();
    private List<Region> _updatedRegions = new();
    private List<Guid> _deletedRegions = new();

    private Dictionary<IServerPlayer, IList<ChunkRegion>> _readyToSendChunks = new();
    private List<ChunkRegion> _readyToPushChunks = new();

    private Dictionary<Guid, bool> _regionsExistanceCache = new ();

    private RegionDB _regionDB;
    public string getRegionDbFilePath()
    {
        string path = System.IO.Path.Combine(GamePaths.DataPath, "Maps");
        GamePaths.EnsurePathExists(path);

        return System.IO.Path.Combine(path, _api.World.SavegameIdentifier + "-regions.db");
    }

    public ServerStorage(ICoreServerAPI sapi)
    {
        _api = sapi;
        _regionDB = new RegionDB(_api.World.Logger);
        string? errorMessage = null;
        string regionDbFilePath = getRegionDbFilePath();
        _regionDB.OpenOrCreate(regionDbFilePath, ref errorMessage, true, true, false);
        if (errorMessage != null)
        {
            throw new Exception(string.Format("Cannot open {0}, possibly corrupted. Please fix manually or delete this file to continue playing", regionDbFilePath));
        }

        _networkChannel = _api.Network.RegisterChannel(ConfigManager.NetworkChannelName)
                            .RegisterMessageType<RegionRequest>()
                            .RegisterMessageType<ChunkRegionRequest>()
                            .RegisterMessageType<ChunkQueryRequest>()
                            .RegisterMessageType<ChunkQueryResponse>()
                            .RegisterMessageType<RegionResponse>()
                            .SetMessageHandler<RegionRequest>(ReceiveRegions)
                            .SetMessageHandler<ChunkRegionRequest>(ReceiveChunkRegions)
                            .SetMessageHandler<ChunkQueryRequest>(ReceiveChunkRequest);

        StartProcessThread();

        sapi.Permissions.RegisterPrivilege(ConfigManager.CreateRegionPrivilege, "Allow region creation");
        sapi.Permissions.RegisterPrivilege(ConfigManager.ChangeRegionPrivilege, "Allow updating regions");
        sapi.Permissions.RegisterPrivilege(ConfigManager.DeleteRegionPrivilege, "Allow deleting regions");
        sapi.Permissions.RegisterPrivilege(ConfigManager.ExpandRegionPrivilege, "Allow expanding regions");
        sapi.Permissions.RegisterPrivilege(ConfigManager.ShrinkRegionPrivilege, "Allow shrinking regions");
        sapi.Permissions.RegisterPrivilege(ConfigManager.ManageRegionPrivilege, "Allow using region commands");
        sapi.Permissions.RegisterPrivilege(ConfigManager.SuperRegionPrivilege, "Regions super user, can override all regions");

        sapi.ChatCommands.GetOrCreate("regions")
            .RequiresPrivilege(ConfigManager.ManageRegionPrivilege)
            .BeginSubCommand("restart")
                .RequiresPrivilege(Privilege.root)
                .WithDescription("Restarts the server's process thread. Without it running, no requests to the server get processed and saved to the server's region storage.")
                .HandleWith((args) => {
                    return StartProcessThread() ? TextCommandResult.Success("Restart done!") : TextCommandResult.Error("Process doesn't need restart");
                })
            .EndSubCommand()
            .BeginSubCommand("player")
                .WithArgs(
                    sapi.ChatCommands.Parsers.PlayerUids("player"),
                    sapi.ChatCommands.Parsers.WordRange("operation", ["grant", "revoke"])
                )
                .WithDescription("Grants or revokes all region privileges to a player")
                .HandleWith((args) =>
                {
                    var players = (PlayerUidName[])args[0];
                    string op = (string)args[1];
                    foreach (var player in players)
                    {
                        var otherArgs = new TextCommandCallingArgs()
                        {
                            Caller = args.Caller,
                        };
                        switch(op)
                        {
                            case "grant":
                                sapi.ChatCommands.ExecuteUnparsed($"/player {player.Name} privilege grant {ConfigManager.CreateRegionPrivilege}", otherArgs);
                                sapi.ChatCommands.ExecuteUnparsed($"/player {player.Name} privilege grant {ConfigManager.ChangeRegionPrivilege}", otherArgs);
                                sapi.ChatCommands.ExecuteUnparsed($"/player {player.Name} privilege grant {ConfigManager.DeleteRegionPrivilege}", otherArgs);
                                sapi.ChatCommands.ExecuteUnparsed($"/player {player.Name} privilege grant {ConfigManager.ExpandRegionPrivilege}", otherArgs);
                                sapi.ChatCommands.ExecuteUnparsed($"/player {player.Name} privilege grant {ConfigManager.ShrinkRegionPrivilege}", otherArgs);
                                break;
                            case "revoke":
                                sapi.ChatCommands.ExecuteUnparsed($"/player {player.Name} privilege revoke {ConfigManager.CreateRegionPrivilege}", otherArgs);
                                sapi.ChatCommands.ExecuteUnparsed($"/player {player.Name} privilege revoke {ConfigManager.ChangeRegionPrivilege}", otherArgs);
                                sapi.ChatCommands.ExecuteUnparsed($"/player {player.Name} privilege revoke {ConfigManager.DeleteRegionPrivilege}", otherArgs);
                                sapi.ChatCommands.ExecuteUnparsed($"/player {player.Name} privilege revoke {ConfigManager.ExpandRegionPrivilege}", otherArgs);
                                sapi.ChatCommands.ExecuteUnparsed($"/player {player.Name} privilege revoke {ConfigManager.ShrinkRegionPrivilege}", otherArgs);
                                sapi.Permissions.RemovePrivilegeDenial(player.Uid, ConfigManager.CreateRegionPrivilege);
                                sapi.Permissions.RemovePrivilegeDenial(player.Uid, ConfigManager.ChangeRegionPrivilege);
                                sapi.Permissions.RemovePrivilegeDenial(player.Uid, ConfigManager.DeleteRegionPrivilege);
                                sapi.Permissions.RemovePrivilegeDenial(player.Uid, ConfigManager.ExpandRegionPrivilege);
                                sapi.Permissions.RemovePrivilegeDenial(player.Uid, ConfigManager.ShrinkRegionPrivilege);
                                break;
                        }
                    }
                    return TextCommandResult.Success($"Successfully executed regions permission command."); 
                })
            .EndSubCommand()
            .BeginSubCommand("role")
                .WithArgs(
                    sapi.ChatCommands.Parsers.PlayerRole("role"),
                    sapi.ChatCommands.Parsers.WordRange("operation", ["grant", "revoke"])
                )
                .WithDescription("Grants or revokes all region privileges to a certain role. This doesnt update player's side, so clients of that role need to re-enter the server to have their privileges updated.")
                .HandleWith((args) =>
                {
                    var role = (IPlayerRole)args[0];
                    switch((string)args[1])
                    {
                        case "grant":
                            sapi.Permissions.GetRole(role.Code).GrantPrivilege(
                                ConfigManager.CreateRegionPrivilege,
                                ConfigManager.ChangeRegionPrivilege,
                                ConfigManager.DeleteRegionPrivilege,
                                ConfigManager.ExpandRegionPrivilege,
                                ConfigManager.ShrinkRegionPrivilege
                            );
                            return TextCommandResult.Success($"Successfully granted region privileges to {role.Name}"); 
                        case "revoke":
                            sapi.Permissions.GetRole(role.Code).RevokePrivilege(ConfigManager.CreateRegionPrivilege);
                            sapi.Permissions.GetRole(role.Code).RevokePrivilege(ConfigManager.ChangeRegionPrivilege);
                            sapi.Permissions.GetRole(role.Code).RevokePrivilege(ConfigManager.DeleteRegionPrivilege);
                            sapi.Permissions.GetRole(role.Code).RevokePrivilege(ConfigManager.ExpandRegionPrivilege);
                            sapi.Permissions.GetRole(role.Code).RevokePrivilege(ConfigManager.ShrinkRegionPrivilege);
                            return TextCommandResult.Success($"Successfully revoked region privileges to {role.Name}"); 
                    }
                    return TextCommandResult.Error($"Wrong operation requested, only 'grant' and 'revoke' are allowed!"); 
                })
            .EndSubCommand();
            
    }

    public void Dispose()
    {
        _regionDB.Dispose();
    }

    private bool StartProcessThread()
    {
        if(_processThread is not null && _processThread.IsAlive)
        {
            return false;
        }

        _processThread = new Thread(new ThreadStart(() =>
        {
            try
            {
                while(!_api.Server.IsShuttingDown)
                {
                    OffThreadProcessQueues();
                    Thread.Sleep(100);
                }
            }
            catch(Exception e)
            {
                _api.SendMessageToGroup(GlobalConstants.ServerInfoChatGroup, "[Custom Map Regions] - Process thread stopped unexpectadely! To restart it run /regions restart.", EnumChatType.Notification);
                _api.Logger.LogException(EnumLogType.Error, e);
            }
        }));
        _processThread.IsBackground = true;
        _processThread.Start();
        return true;
    }

    private void ReceiveChunkRegions(IServerPlayer player, ChunkRegionRequest request)
    {
        if(request.chunkRegions is null) return;

        lock(_chunksReceivedLock)
        {
            foreach (var msg in request.chunkRegions)
            {
                _chunksReceived.Enqueue(new () {msg = msg, fromPlayer = player});
            }
        }
    }

    private void ReceiveRegions(IServerPlayer player, RegionRequest request)
    {
        lock(_regionsReceivedLock)
        {
            _regionsReceived.Enqueue(new () {msg = request, fromPlayer = player});
        }
    }

    private void ReceiveChunkRequest(IServerPlayer player, ChunkQueryRequest request)
    {
        if(request.chunkCoords is null) return;

        lock(_chunksRequestedLock)
        {
            foreach (var coords in request.chunkCoords)
            {
                _chunksRequested.Enqueue(new () {chunkCoords = coords, fromPlayer = player});
            }
        }
    }
    
    private void OffThreadProcessQueues()
    {
        SharedUtils.SafeDequeueThrough(_regionsReceived, _regionsReceivedLock, (RegionServerMsg smsg) =>
        {
            var msg = smsg.msg;
            switch(msg.operation)
            {
                case MsgOperationEnum.Create:
                    if(!HasPrivilege(smsg.fromPlayer, ConfigManager.CreateRegionPrivilege)) return;
                    msg.chunkRegion.Region.PlayerID = smsg.fromPlayer.PlayerUID;
                    _regionDB.CreateNewRegion(msg.chunkRegion);

                    lock(_chunksToPushLock)
                    {
                        _chunksToPush.Enqueue(new ChunkRequestServerMsg() { chunkCoords = msg.chunkRegion.ChunkPos, fromPlayer = smsg.fromPlayer});
                    }
                    break;
                case MsgOperationEnum.Update:
                    if(!HasPrivilege(smsg.fromPlayer, ConfigManager.ChangeRegionPrivilege) || !IsActionAllowed(msg.chunkRegion.Region.RegionId, smsg.fromPlayer)) return;
                    _regionDB.UpdateRegion(msg.chunkRegion.Region);
                    _updatedRegions.Add(msg.chunkRegion.Region);
                    break;
                case MsgOperationEnum.Delete:
                    if(!HasPrivilege(smsg.fromPlayer, ConfigManager.DeleteRegionPrivilege) || !IsActionAllowed(msg.regionId, smsg.fromPlayer)) return;
                    _regionDB.DeleteRegion(msg.regionId);
                    _deletedRegions.Add(msg.regionId);
                    break;
            }
        });

        SharedUtils.SafeDequeueThrough(_chunksReceived, _chunksReceivedLock, (ChunkRegionServerMsg smsg) =>
        {
            var msg = smsg.msg;
            if(msg.toDelete)
            {
                if(!HasPrivilege(smsg.fromPlayer, ConfigManager.ShrinkRegionPrivilege) || !IsActionAllowed(msg.chunkCoords, smsg.fromPlayer)) return;
                _regionDB.DeleteChunkRegion(msg.chunkCoords);
            }
            else
            {
                if(!RegionExists(msg.regionId))
                {
                    if(smsg.attempt < MaxAttempts)
                    {
                        smsg.attempt++;
                        _chunksReceived.Enqueue(smsg);
                    }
                    return;
                }

                if(!HasPrivilege(smsg.fromPlayer, ConfigManager.ExpandRegionPrivilege) || !IsActionAllowed(msg.chunkCoords, smsg.fromPlayer) || !IsActionAllowed(msg.regionId, smsg.fromPlayer)) return;
                _regionDB.AddChunkToRegion(msg.chunkCoords, msg.regionId);
            }

            lock(_chunksToPushLock)
            {
                _chunksToPush.Enqueue(new ChunkRequestServerMsg() { chunkCoords = msg.chunkCoords, fromPlayer = smsg.fromPlayer});
            }
        });

        SharedUtils.SafeDequeueThrough(_chunksRequested, _chunksRequestedLock,
            (ChunkRequestServerMsg chunkCoords) => GenerateChunk(chunkCoords, (msg, chunkRegion) =>
            {
                if(!_readyToSendChunks.TryGetValue(msg.fromPlayer, out IList<ChunkRegion> listToSend))
                {
                    _readyToSendChunks.Add(msg.fromPlayer, listToSend = new List<ChunkRegion>());
                }
                listToSend.Add(chunkRegion);
            })
        );

        SharedUtils.SafeDequeueThrough(_chunksToPush, _chunksToPushLock,
            (ChunkRequestServerMsg chunkCoords) => GenerateChunk(chunkCoords, (msg, chunkRegion) =>
            {
                _readyToPushChunks.Add(chunkRegion);
            })
        );

        if(_readyToSendChunks.Count > 0)
        {
            foreach (var sendRequest in _readyToSendChunks)
            {
                _networkChannel.SendPacket(
                    new ChunkQueryResponse() 
                    { 
                        chunkRegions = sendRequest.Value.ToArray()
                    }, 
                    sendRequest.Key
                );
            }

            _readyToSendChunks.Clear();
        }

        if(_readyToPushChunks.Count > 0)
        {
            _networkChannel.BroadcastPacket(
                new ChunkQueryResponse() 
                { 
                    chunkRegions = _readyToPushChunks.ToArray()
                }
            );

            _readyToPushChunks.Clear();
        }

        if(_updatedRegions.Count > 0)
        {
            _networkChannel.BroadcastPacket(
                new RegionResponse() 
                { 
                    regions = _updatedRegions.Select(x => new RegionResponseMsg()
                    {
                        operation = MsgOperationEnum.Update,
                        region = x,
                    }).ToArray(),
                }
            );

            _updatedRegions.Clear();
        }

        if(_deletedRegions.Count > 0)
        {
            _networkChannel.BroadcastPacket(
                new RegionResponse() 
                { 
                    regions = _deletedRegions.Select(x => new RegionResponseMsg()
                    {
                        operation = MsgOperationEnum.Delete,
                        regionId = x,
                    }).ToArray(),
                }
            );

            _deletedRegions.Clear();
        }

        bool GenerateChunk(ChunkRequestServerMsg msg, Action<ChunkRequestServerMsg, ChunkRegion> onChunkAction)
        {
            var chunkRegion = _regionDB.GetChunkRegion(msg.chunkCoords);
            if (chunkRegion != null)
            {
                onChunkAction.Invoke(msg, chunkRegion);
                return true;
            }

            return false;
        }
    }

    private bool HasPrivilege(IServerPlayer player, string priviledge)
    {
        return player.HasPrivilege("root")
            || player.HasPrivilege(priviledge);
    }

    private bool IsActionAllowed(Region region, IServerPlayer player)
    {
        if(region.PlayerID == player.PlayerUID || player.HasPrivilege(ConfigManager.SuperRegionPrivilege))
        {
            return true;
        }

        if(string.IsNullOrEmpty(region.PlayerID))
        {
            return false;
        }

        var playerData = _api.PlayerData.GetPlayerDataByUid(region.PlayerID);
        string roleCode;
        if(playerData is not null)
        {
            roleCode = playerData.RoleCode;
        }
        else
        {
            roleCode = _api.Server.Config.DefaultRoleCode;
        }
        var role = _api.Permissions.GetRole(roleCode);
        return player.Role.IsSuperior(role);
    }

    private bool IsActionAllowed(Guid regionId, IServerPlayer player)
    {
        Region region = _regionDB.GetRegion(regionId);
        return IsActionAllowed(region, player);
    }

    private bool IsActionAllowed(FastVec2i chunkPos, IServerPlayer player)
    {
        ChunkRegion chunkRegion = _regionDB.GetChunkRegion(chunkPos);
        Region region = chunkRegion.Region;
        return region.RegionId != Guid.Empty ? IsActionAllowed(region, player) : true;
    }
    
    private bool RegionExists(Guid regionId)
    {
        if(!_regionsExistanceCache.TryGetValue(regionId, out bool exists))
        {
            exists = _regionDB.GetRegion(regionId) is not null;
            _regionsExistanceCache.TryAdd(regionId, exists);
        }
        return exists;
    }
}