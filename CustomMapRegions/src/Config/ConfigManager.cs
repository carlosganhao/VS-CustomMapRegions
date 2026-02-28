using System;
using Vintagestory.API.Common;

namespace CustomMapRegions.Config;

public static class ConfigManager
{
    public static CustomMapRegionsConfig ConfigInstance { get; internal set; }
    private static string configPath = "customMapRegionsConfig.json";

    public static void LoadModConfig(ICoreAPI api)
    {
        try
        {
            ConfigInstance = api.LoadModConfig<CustomMapRegionsConfig>(configPath);
            if (ConfigInstance == null)
            {
                ConfigInstance = new CustomMapRegionsConfig();
                ConfigInstance.SetDefaults();
            }

            api.StoreModConfig<CustomMapRegionsConfig>(ConfigInstance, configPath);
        }
        catch (Exception e)
        {
            api.Logger.Error("[Custom Map Regions] - Could not load config! Loading default settings instead.");
            api.Logger.Error(e);
            ConfigInstance = new CustomMapRegionsConfig();
            ConfigInstance.SetDefaults();
        }
    }

    public static void SaveModConfig(ICoreAPI api)
    {
        api.StoreModConfig(ConfigInstance, configPath);
    }
}