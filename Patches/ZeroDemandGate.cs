using System;
using AI.Conditionals;
using AiPlayerIntel.Config;
using AiPlayerIntel.Core;
using BehaviorDesigner.Runtime.Tasks;
using HarmonyLib;
using ScriptableObjectScripts;

namespace AiPlayerIntel.Patches;

[HarmonyPatch(typeof(IsOfferViable), nameof(IsOfferViable.OnUpdate))]
static class ZeroDemandGate {
    // Behaviour is enum/value-driven, so it stays in-body; skip only when wholly inert at load time.
    static bool Prepare() {
        var config = Services.Config;
        return config.MasterEnable.Value
            && (config.MarketBuyOrder.Value == MarketBuyOrder.ContractOnly || config.ClampBuyQuantity.Value);
    }

    static void Postfix(IsOfferViable __instance, ref TaskStatus __result) {
        if (__result != TaskStatus.Success || __instance.isBuy.Value) { return; } // buyer branch only (!isBuy)

        var companyBehaviour = __instance.CompanyBehaviour;
        var where = __instance.where.Value?.Object;
        var resourceDefinition = __instance.what.Value as ResourceDefinition;
        if (companyBehaviour == null || where == null || resourceDefinition == null) { return; }

        var config = Services.Config;
        var deficit = Services.Deficit.Evaluate(companyBehaviour, where, resourceDefinition);
        if (config.MarketBuyOrder.Value == MarketBuyOrder.ContractOnly && deficit.UnmetVsDemand <= 0 && !deficit.InBom) {
            __result = TaskStatus.Failure; // need-less → reject; Vanilla/ContractFirst = no eligibility flip (§2e)
            return;
        }
        if (config.ClampBuyQuantity.Value && deficit.UnmetVsNeed > 0) {
            // clamp take to remaining deficit
            __instance.howMuch.Value = (float)Math.Min(__instance.howMuch.Value, deficit.UnmetVsNeed);
        }
    }
}
