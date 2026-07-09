using AiPlayerIntel.Config;
using AiPlayerIntel.Core;
using AiPlayerIntel.Intel;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace AiPlayerIntel;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin {
    internal static ManualLogSource Log = null!;

    // Deprecated v3: StyledTheme (IMGUI skin) and BodyFirstGrouping (swappable grouping) retired
    // with the IMGUI panel; UGUI is a single vanilla-matching theme over a fixed body→company tree.

    void Awake() {
        Log = Logger;

        var config = new Configuration(Config);   // Config = BepInEx ConfigFile
        config.Validate();
        Services.Init(config);   // must precede PatchAll: patch Prepare() reads Services.Config

        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
        IntelController.Ensure();
        if (Services.Config.UnstickEnable.Value) { StuckWatch.Ensure(Services.Deficit); }
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} loaded.");
    }
}
