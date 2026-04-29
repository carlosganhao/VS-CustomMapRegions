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
    private string patchId = "customMapRegions";
    private Harmony harmonyInstance;
    private ServerStorage serverStorage;

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);

        if(api.Side == EnumAppSide.Client)
        {
            if(!Harmony.HasAnyPatches(patchId))
            {
                harmonyInstance = new Harmony(patchId);
                harmonyInstance.PatchAll();
            }
        }
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
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
        harmonyInstance?.UnpatchAll(patchId);
        base.Dispose();
    }
}
