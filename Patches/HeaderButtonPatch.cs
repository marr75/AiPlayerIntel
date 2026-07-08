using AiPlayerIntel.Ui;
using HarmonyLib;
using Manager;

namespace AiPlayerIntel.Patches;

[HarmonyPatch(typeof(NotificationManager), "Awake")]
static class HeaderButtonPatch {
    static void Postfix(NotificationManager __instance) {
        HeaderButton.Inject(__instance);
        IntelPanel.Inject(__instance);
    }
}
