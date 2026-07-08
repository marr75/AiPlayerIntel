using System;
using AI.Conditionals;
using AiPlayerIntel.Core;
using BehaviorDesigner.Runtime.Tasks;
using HarmonyLib;
using ScriptableObjectScripts;

namespace AiPlayerIntel.Patches;

[HarmonyPatch(typeof(IsOfferViable), nameof(IsOfferViable.OnUpdate))]
static class ZeroDemandGate {
    static void Postfix(IsOfferViable __instance, ref TaskStatus __result) {
        var cfg = Services.Cfg;
        if (cfg == null || !cfg.MasterEnable.Value) { return; }
        if (!cfg.ZeroDemandGate.Value && !cfg.ClampBuyQuantity.Value) { return; }
        if (__result != TaskStatus.Success || __instance.isBuy.Value) { return; }   // buyer branch only (!isBuy)

        var cb = __instance.CompanyBehaviour;
        var where = __instance.where.Value?.Object;
        var rd = __instance.what.Value as ResourceDefinition;
        if (cb == null || where == null || rd == null) { return; }

        var d = Services.Deficit.Evaluate(cb, where, rd);
        if (cfg.ZeroDemandGate.Value && d.UnmetVsDemand <= 0 && !d.InBom) {   // need-less → reject (§3.2)
            __result = TaskStatus.Failure;
            return;
        }
        if (cfg.ClampBuyQuantity.Value && d.UnmetVsNeed > 0) {                // clamp take to remaining deficit
            __instance.howMuch.Value = (float)Math.Min(__instance.howMuch.Value, d.UnmetVsNeed);
        }
    }
}
