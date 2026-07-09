using AiPlayerIntel.Core;
using Game;
using Game.ObjectInfoDataScripts;
using HarmonyLib;

namespace AiPlayerIntel.Patches;

// Commit veto (research-fair-offer-allocation.md §4): last sync gate before resources move; blocks a non-grantee
// AI buy of a tracked offer under a live grant lease, covering both the reactive child-4 fulfill and the proactive
// BuyFromOffers path. Separate class from OfferObserver so Prefix runs before its Postfix, never logging a vetoed
// attempt. No-op for player/untracked.
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
