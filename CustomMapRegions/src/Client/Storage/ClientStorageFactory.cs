using CustomMapRegions.Client.Abstractions;
using CustomMapRegions.Common.Networking;
using CustomMapRegions.Config;
using Vintagestory.API.Client;

namespace CustomMapRegions.Client.Storage;

public static class ClientStorageFactory
{
    public static bool IsSinglePlayer;
    public static bool ClientSideOnly => IsSinglePlayer || ClientNetworkChannel is null || !ClientNetworkChannel.Connected;
    public static IClientNetworkChannel ClientNetworkChannel { get; internal set; }

    public static void Init(ICoreClientAPI capi)
    {
        IsSinglePlayer = capi.IsSinglePlayer;
        ClientNetworkChannel = capi.Network.RegisterChannel(ConfigManager.NetworkChannelName)
                                .RegisterMessageType<RegionRequest>()
                                .RegisterMessageType<ChunkRegionRequest>()
                                .RegisterMessageType<ChunkQueryRequest>()
                                .RegisterMessageType<ChunkQueryResponse>()
                                .RegisterMessageType<RegionResponse>();
    }

    public static AbstractClientStorage BuildClientStorage(ICoreClientAPI capi)
    {

        if(ClientSideOnly)
        {
            return new LocalClientStorage(capi);
        }

        return new RemoteClientStorage(capi, ClientNetworkChannel);
    }
}