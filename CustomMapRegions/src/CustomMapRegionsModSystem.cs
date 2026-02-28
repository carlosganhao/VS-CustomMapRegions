using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using HarmonyLib;
using CustomMapRegions.Client;
using CustomMapRegions.Config;

namespace CustomMapRegions;

public class CustomMapRegionsModSystem : ModSystem
{
    private string patchId = "customMapRegions";
    private Harmony harmonyInstance;

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);

        if(api.Side == EnumAppSide.Client)
        {
            ConfigManager.LoadModConfig(api);

            if(!Harmony.HasAnyPatches(patchId))
            {
                harmonyInstance = new Harmony(patchId);
                harmonyInstance.PatchAll();
            }
        }
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        Mod.Logger.Notification("[Custom Map Regions] - Loaded client side only version");
        var mapManager = api.ModLoader.GetModSystem<WorldMapManager>();
        mapManager.RegisterMapLayer<RegionMapLayer>("RegionLayer", 2);
    }

    public override void Dispose()
    {
        ConfigManager.ConfigInstance = null;
        harmonyInstance?.UnpatchAll(patchId);
        base.Dispose();
    }
}
