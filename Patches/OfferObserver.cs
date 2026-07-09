using AiPlayerIntel.Core;
using Game;
using Game.ObjectInfoDataScripts;
using HarmonyLib;
using Manager;

namespace AiPlayerIntel.Patches;

// Telemetry (feature F): log only COMMITTED AI fills. A SEPARATE class from the veto Prefix — it reads
// __result AFTER the (possibly vetoed) original, so a vetoed attempt is never recorded as a fill. Observe-only;
// never blocks or mutates a buy.
[HarmonyPatch(typeof(Offer), nameof(Offer.FullFill))]
static class OfferObserver {
    static bool Prepare() => Services.Config.MasterEnable.Value && Services.Config.ObserveLogFills.Value;

    static void Postfix(Offer __instance, Company CompanyTakeOffer, double count, bool __result) {
        if (!__result || CompanyTakeOffer == null) { return; }
        var gm = MonoBehaviourSingleton<GameManager>.Instance;
        if (gm != null && CompanyTakeOffer == gm.Player) { return; }   // silent AI buys only

        try {
            var cb = CompanyTakeOffer.companyBehaviour;
            var cls = cb != null ? Services.Deficit.Evaluate(cb, __instance.WhereOffer, __instance.Rd).Class : NeedClass.NeedLess;
            Plugin.Log.LogInfo($"[MarketFill] {CompanyTakeOffer.ID} bought {count:0.##} {__instance.Rd?.name} "
                + $"@ {__instance.WhereOffer?.ObjectName} ({cls}, offer {__instance.ID})");
        } catch { /* telemetry must never break a fill */ }
    }
}
