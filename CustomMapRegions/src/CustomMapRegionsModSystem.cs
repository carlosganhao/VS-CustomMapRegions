using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using HarmonyLib;
using CustomMapRegions.Client;
using CustomMapRegions.Config;
using Vintagestory.API.Server;
using CustomMapRegions.Client.Storage;
using CustomMapRegions.Server;

namespace CustomMapRegions;

public class CustomMapRegionsModSystem : ModSystem
{
    private ServerStorage serverStorage;

    public override void StartClientSide(ICoreClientAPI api)
    {
        new Harmony(Mod.Info.ModID).PatchAll();
                
        Mod.Logger.Notification("[Custom Map Regions] - Loaded client side version");
        ConfigManager.LoadModConfig(api);
        ClientStorageFactory.Init(api);
        var mapManager = api.ModLoader.GetModSystem<WorldMapManager>();
        mapManager.RegisterMapLayer<RegionMapLayer>("RegionLayer", 2);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        Mod.Logger.Notification("[Custom Map Regions] - Loaded server side version");
        serverStorage = new ServerStorage(api);
    }

    public override void Dispose()
    {
        ConfigManager.ConfigInstance = null;
        serverStorage?.Dispose();
        new Harmony(Mod.Info.ModID).UnpatchAll();
        base.Dispose();
    }
}
