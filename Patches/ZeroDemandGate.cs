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
        var c = Services.Config;
        return c.MasterEnable.Value
            && (c.MarketBuyOrder.Value == MarketBuyOrder.ContractOnly || c.ClampBuyQuantity.Value);
    }

    static void Postfix(IsOfferViable __instance, ref TaskStatus __result) {
        if (__result != TaskStatus.Success || __instance.isBuy.Value) { return; }   // buyer branch only (!isBuy)

        var cb = __instance.CompanyBehaviour;
        var where = __instance.where.Value?.Object;
        var rd = __instance.what.Value as ResourceDefinition;
        if (cb == null || where == null || rd == null) { return; }

        var c = Services.Config;
        var d = Services.Deficit.Evaluate(cb, where, rd);
        if (c.MarketBuyOrder.Value == MarketBuyOrder.ContractOnly && d.UnmetVsDemand <= 0 && !d.InBom) {
            __result = TaskStatus.Failure;   // need-less → reject; Vanilla/ContractFirst = no eligibility flip (§2e)
            return;
        }
        if (c.ClampBuyQuantity.Value && d.UnmetVsNeed > 0) {                // clamp take to remaining deficit
            __instance.howMuch.Value = (float)Math.Min(__instance.howMuch.Value, d.UnmetVsNeed);
        }
    }
}
