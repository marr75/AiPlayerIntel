using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using AI.Decorators;
using AiPlayerIntel.Core;
using HarmonyLib;
using ScriptableObjectScripts;

namespace AiPlayerIntel.Patches;

// A — Post more buy offers (time gate): vanilla MakeOfferPriorityGate.InternalGetCost only advertises a
// standing BUY offer when the cheapest self-source path is >= makeOfferTimeThreshold (365) game-days out (:49).
// This transpiler lowers that gate to the configured value so AIs post buy orders they'd otherwise self-source.
// Fails open on a missed IL match.
static class PostBidsHooks {
    // Only ever lower the gate (never raise a company that already bids sooner than the config value).
    public static float TimeGate(float vanilla) {
        try {
            var threshold = Services.Config.PostBidsTimeThreshold.Value;
            return threshold < vanilla ? threshold : vanilla;
        }
        catch (Exception ex) {
            Plugin.Log.LogError($"PostBids.TimeGate failed: {ex}");
            return vanilla;
        }
    }
}

// Shares MakeOfferPriorityGate.InternalGetCost with NeedPremiumBid, which rewrites the PRICE term (ldfld
// makeOfferUnitCostMultiplier + mul, :55); this one rewrites the TIME gate (ldfld makeOfferTimeThreshold, :49)
// — a different, non-overlapping ldfld anchor, so the two compose regardless of Harmony order. Priority.Low
// runs this last (after NeedPremiumBid); its injected instructions load no makeOfferTimeThreshold, so it still
// matches exactly once.
[HarmonyPatch]
static class PostBidsTimeGate {
    static readonly FieldInfo TimeThreshold = AccessTools.Field(
        typeof(CompanyDefinition.CompanyAIConfig),
        nameof(CompanyDefinition.CompanyAIConfig.makeOfferTimeThreshold)
    );

    static readonly MethodInfo Helper = AccessTools.Method(
        typeof(PostBidsHooks),
        nameof(PostBidsHooks.TimeGate)
    );

    static bool Prepare() => Services.Config.MasterEnable.Value && Services.Config.PostBidsEnable.Value;

    static MethodBase TargetMethod() => NeedPremiumHooks.MoveNextOf(typeof(MakeOfferPriorityGate), "InternalGetCost");

    [HarmonyTranspiler, HarmonyPriority(Priority.Low)]
    static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions) {
        var swapped = 0;
        foreach (var instruction in instructions) {
            yield return instruction;
            if (instruction.LoadsField(TimeThreshold)) {
                yield return new CodeInstruction(OpCodes.Call, Helper);
                swapped++;
            }
        }
        if (swapped != 1) {
            Plugin.Log.LogWarning(
                $"PostBidsTimeGate: expected exactly 1 makeOfferTimeThreshold load, injected {swapped}. "
                + "Post-more-bids time gate inactive; vanilla behavior preserved."
            );
        }
    }
}
