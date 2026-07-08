using System;
using AI;
using AiPlayerIntel.Core;
using HarmonyLib;
using ScriptableObjectScripts;

namespace AiPlayerIntel.Patches;

// G — Catch-up cost-of-time multiplier (price-space). Scales the TIME contribution of every per-company
// cost at the DIY basis (CalcCostMagnitude). Because every ceiling/bid/sell-floor AND the internal
// Min(mine,refine,build,buy,deliver) path selection read this magnitude, a trailing AI values time more
// everywhere — favouring fast buy/deliver over slow mine/refine, bidding higher, raising its sell floor.
//
// Postfix is correct even though CalcCostMagnitude returns a scalar: Harmony injects the raw CompanyCost
// argument and __instance (with costMultiplier + costCalcType), so the time term (companyCost.Time *
// costMultiplier.Time) is reconstructed from the inputs and the result adjusted exactly as if time were
// scaled before the fold — the fold is reproduced, not lost. Point-of-use only — never mutates the SO
// field. Leader → factor 1.0 → early return → byte-identical. Fail-open on any error.
[HarmonyPatch(typeof(CompanyDefinition.CompanyAIConfig), nameof(CompanyDefinition.CompanyAIConfig.CalcCostMagnitude))]
static class CatchUpMultiplier {
    [HarmonyPostfix]
    static void Postfix(CompanyDefinition.CompanyAIConfig __instance, CompanyCost companyCost, ref double __result) {
        try {
            var cfg = Services.Cfg;
            if (cfg == null || !cfg.MasterEnable.Value || !cfg.CatchUpEnable.Value) { return; }
            var cb = companyCost.Company;
            if (cb == null) { return; }
            var company = cb.Company;
            if (company == null) { return; }

            double catchUp = Services.Willingness.CostOfTimeFactor(company);
            if (catchUp <= 1.0) { return; }   // leader / no gap → leave __result untouched

            double m = companyCost.Money * __instance.costMultiplier.Money;
            double t = companyCost.Time * __instance.costMultiplier.Time;
            switch (__instance.costCalcType) {
                case CompanyDefinition.CompanyAIConfig.CostCalcType.Sum:
                    __result += (catchUp - 1.0) * t;   // exact time-term add
                    break;
                case CompanyDefinition.CompanyAIConfig.CostCalcType.Magnitude:
                    __result = cfg.CatchUpTimeOnly.Value
                        ? Math.Sqrt(m * m + catchUp * catchUp * t * t)   // scale only the time term
                        : __result * catchUp;
                    break;
                default:   // Max / Min — exact time-only re-collapse is ill-defined; scale whole magnitude
                    __result *= catchUp;
                    break;
            }
        } catch (Exception ex) {
            Plugin.Log.LogError($"CatchUpMultiplier.Postfix failed: {ex}");
        }
    }
}
