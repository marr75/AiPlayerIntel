using AiPlayerIntel.Ui;
using Game.UI;
using HarmonyLib;
using UnityEngine;

namespace AiPlayerIntel.Patches;

[HarmonyPatch(typeof(UIManager), "Update")]
static class EscapeGuardPatch {
    static bool Prefix() {
        if (!Input.GetKeyDown(KeyCode.Escape)) { return true; }
        return !IntelPanel.HandleEscape();
    }
}
