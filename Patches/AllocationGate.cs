using AI.Actions;
using AiPlayerIntel.Core;
using BehaviorDesigner.Runtime.Tasks;
using Game.ObjectInfoDataScripts;
using HarmonyLib;
using Manager;

namespace AiPlayerIntel.Patches;

// Register a freshly posted offer with the arbiter (player SELL offers only; the arbiter itself filters).
[HarmonyPatch(typeof(MarketOfferManager), nameof(MarketOfferManager.AddOffer))]
static class OfferPostedPatch {
    static bool Prepare() => Services.Config.MasterEnable.Value;

    static void Postfix(Offer offer, bool __result) {
        if (__result) { Services.Arbiter.OnOfferPosted(offer); }
    }
}

// Reactive ordering gate. On the acquire tick (@lock==true) the arbiter parks non-best-fit buyers at Running
// (they re-tick without consuming their attempt); the ranked winner falls through to the vanilla acquire.
// Release ticks and untracked offers always run vanilla. Fail-open lives in the arbiter.
[HarmonyPatch(typeof(LockOffer), nameof(LockOffer.OnUpdate))]
static class LockOfferGatePatch {
    static bool Prepare() => Services.Config.MasterEnable.Value;

    static bool Prefix(LockOffer __instance, ref TaskStatus __result) {
        if (!__instance.@lock) { return true; } // release path — never gate
        var offer = __instance.offer.Value?.Object;
        if (offer == null) { return true; }
        if (Services.Arbiter.ShouldDefer(offer, __instance.Company)) {
            __result = TaskStatus.Running;
            return false;
        }
        return true;
    }
}
