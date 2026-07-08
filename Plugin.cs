using AiPlayerIntel.Intel;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AiPlayerIntel;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin {
    internal static ManualLogSource Log = null!;
    internal static ConfigEntry<KeyCode> ToggleKey = null!;
    internal static ConfigEntry<float> RefreshSeconds = null!;
    internal static ConfigEntry<bool> EnableDiyValuation = null!;

    // Deprecated v3: StyledTheme (IMGUI skin) and BodyFirstGrouping (swappable grouping) retired
    // with the IMGUI panel; UGUI is a single vanilla-matching theme over a fixed body→company tree.

    void Awake() {
        Log = Logger;
        ToggleKey = Config.Bind("General", "ToggleKey", KeyCode.F10,
            "Open/close the AI Player Intel panel.");
        RefreshSeconds = Config.Bind("General", "RefreshSeconds", 4f,
            "Seconds between snapshot recomputes (clamped 1-30).");
        EnableDiyValuation = Config.Bind("Valuation", "EnableDiyValuation", true,
            "Re-derive each AI's DIY willingness-to-pay via ObtainResourcePriorityGate.Calc. "
            + "Read-only; auto-disabled if the game blocks non-player mission checks.");

        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
        IntelController.Ensure();
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} loaded.");
    }
}
