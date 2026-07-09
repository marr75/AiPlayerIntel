using AiPlayerIntel.Core;
using Game;
using Game.ObjectInfoDataScripts;
using HarmonyLib;

namespace AiPlayerIntel.Patches;

// Commit choke point (research-fair-offer-allocation.md §4): the last synchronous gate before resources/money
// move. Vetoes a non-grantee AI purchase of an arbiter-tracked offer while a conflicting grant lease is live,
// covering both the reactive child-4 fulfill and the proactive BuyFromOffers path. Kept as its own class,
// SEPARATE from the observe Postfix (OfferObserver), so Harmony's Prefix-before-Postfix pass order guarantees
// the observer never logs a vetoed attempt. No-op for the player and for untracked offers.
[HarmonyPatch(typeof(Offer), nameof(Offer.FullFill))]
static class OfferCommitVeto {
    static bool Prepare() => Services.Config.MasterEnable.Value;

    static bool Prefix(Offer __instance, Company CompanyTakeOffer, ref bool __result) {
        if (Services.Arbiter.VetoCommit(__instance, CompanyTakeOffer)) {
            __result = false;
            return false;
        }
        return true;
    }
}
