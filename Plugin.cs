using AiPlayerIntel.Ui;
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
    internal static ConfigEntry<bool> StyledTheme = null!;
    internal static ConfigEntry<bool> BodyFirstGrouping = null!;

    void Awake() {
        Log = Logger;
        ToggleKey = Config.Bind("General", "ToggleKey", KeyCode.F10,
            "Open/close the AI Player Intel panel.");
        RefreshSeconds = Config.Bind("General", "RefreshSeconds", 4f,
            "Seconds between snapshot recomputes (clamped 1-30).");
        EnableDiyValuation = Config.Bind("Valuation", "EnableDiyValuation", true,
            "Re-derive each AI's DIY willingness-to-pay via ObtainResourcePriorityGate.Calc. "
            + "Read-only; auto-disabled if the game blocks non-player mission checks.");
        StyledTheme = Config.Bind("Window", "StyledTheme", true, "Dark/teal skin vs. plain IMGUI.");
        BodyFirstGrouping = Config.Bind("Window", "BodyFirstGrouping", true,
            "Group rows by body then company (true) vs. company then body (false). Toggleable live in the panel.");

        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
        IntelWindow.Ensure();
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} loaded.");
    }
}
