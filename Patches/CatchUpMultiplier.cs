using System;
using AI;
using AiPlayerIntel.Core;
using HarmonyLib;
using ScriptableObjectScripts;

namespace AiPlayerIntel.Patches;

// G — Catch-up cost-of-time multiplier (price-space): scales the TIME term of every per-company DIY cost
// (CalcCostMagnitude), so a trailing AI values speed everywhere. Leader → 1.0 → untouched. Fails open.
// Postfix is exact despite CalcCostMagnitude returning a scalar: Harmony injects the raw CompanyCost arg and
// __instance (costMultiplier + costCalcType), so the time term (companyCost.Time * costMultiplier.Time) is
// reconstructed and the result adjusted as if time were scaled before the fold — the fold is reproduced, not lost.
[HarmonyPatch(typeof(CompanyDefinition.CompanyAIConfig), nameof(CompanyDefinition.CompanyAIConfig.CalcCostMagnitude))]
static class CatchUpMultiplier {
    static bool Prepare() => Services.Config.MasterEnable.Value && Services.Config.CatchUpEnable.Value;

    [HarmonyPostfix]
    static void Postfix(CompanyDefinition.CompanyAIConfig __instance, CompanyCost companyCost, ref double __result) {
        try {
            var companyBehaviour = companyCost.Company;
            if (companyBehaviour == null) { return; }
            var company = companyBehaviour.Company;
            if (company == null) { return; }

            var catchUp = Services.Willingness.CostOfTimeFactor(company);
            if (catchUp <= 1.0) { return; } // leader / no gap → leave __result untouched

            var money = companyCost.Money * __instance.costMultiplier.Money;
            var time = companyCost.Time * __instance.costMultiplier.Time;
            switch (__instance.costCalcType) {
                case CompanyDefinition.CompanyAIConfig.CostCalcType.Sum:
                    __result += (catchUp - 1.0) * time; // exact time-term add
                    break;
                case CompanyDefinition.CompanyAIConfig.CostCalcType.Magnitude:
                    __result = Services.Config.CatchUpTimeOnly.Value
                        ? Math.Sqrt(money * money + catchUp * catchUp * time * time) // scale only the time term
                        : __result * catchUp;
                    break;
                default
                    : // Max/Min or any future enum: time-only recollapse is ill-defined, so scale the whole magnitude
                    __result *= catchUp;
                    break;
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"CatchUpMultiplier.Postfix failed: {ex}"); }
    }
}
