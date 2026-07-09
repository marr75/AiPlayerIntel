using System;
using System.Linq;
using System.Reflection;
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

        var config = new Configuration(Config); // Config = BepInEx ConfigFile
        config.Validate();
        Services.Init(config); // must precede patching: patch Prepare() reads Services.Config

        PatchAllIsolated();
        IntelController.Ensure();
        if (Services.Config.UnstickEnable.Value) { StuckWatch.Ensure(Services.Deficit); }
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} loaded.");
    }

    // Per-class isolation so one broken patch can't abort the rest of the set.
    static void PatchAllIsolated() {
        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes()) {
            if (!type.GetCustomAttributes<HarmonyPatch>().Any()) { continue; }
            try {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception ex) {
                Log.LogError($"Failed to patch {type.FullName}: {ex}");
            }
        }
    }
}
