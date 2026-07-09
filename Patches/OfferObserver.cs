using AiPlayerIntel.Core;
using Game;
using Game.ObjectInfoDataScripts;
using HarmonyLib;
using Manager;

namespace AiPlayerIntel.Patches;

// Telemetry (feature F): logs only COMMITTED AI fills — reads __result after the veto Prefix's original, so a
// vetoed attempt is never recorded. Observe-only.
[HarmonyPatch(typeof(Offer), nameof(Offer.FullFill))]
static class OfferObserver {
    static bool Prepare() => Services.Config.MasterEnable.Value && Services.Config.ObserveLogFills.Value;

    static void Postfix(Offer __instance, Company CompanyTakeOffer, double count, bool __result) {
        if (!__result || CompanyTakeOffer == null) { return; }
        var gameManager = MonoBehaviourSingleton<GameManager>.Instance;
        if (gameManager != null && CompanyTakeOffer == gameManager.Player) { return; } // silent AI buys only

        try {
            var companyBehaviour = CompanyTakeOffer.companyBehaviour;
            var needClass = companyBehaviour != null
                ? Services.Deficit.Evaluate(companyBehaviour, __instance.WhereOffer, __instance.Rd).Class
                : NeedClass.NeedLess;
            Plugin.Log.LogInfo(
                $"[MarketFill] {CompanyTakeOffer.ID} bought {count:0.##} {__instance.Rd?.name} "
                + $"@ {__instance.WhereOffer?.ObjectName} ({needClass}, offer {__instance.ID})"
            );
        }
        catch {
            // Swallow deliberately: telemetry must never break a fill.
        }
    }
}
