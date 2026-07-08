using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using AI;
using AI.Conditionals;
using AI.Decorators;
using AiPlayerIntel.Core;
using BehaviorDesigner.Runtime;
using Data.ScriptableObject;
using Game.Info;
using HarmonyLib;
using ScriptableObjectScripts;

namespace AiPlayerIntel.Patches;

// H — Need premium (price-space). AIs pay a configurable premium for goods they NEED (contract/demand-
// linked + in-BOM at the offer's location) over goods they don't, so a needing company outbids a non-
// needing one and need-scouting pays. Two transpilers inject a scale into the willingness locals that
// live inside async MoveNext state machines (invisible to a source-level postfix). Both fail open: on a
// missed IL match the method is left byte-identical to vanilla; the helpers swallow errors to base price.
static class NeedPremiumHooks {
    // The (1+frac) factor math now lives in Core/Willingness.NeedFactor (design §8.5); the three surface
    // helpers below unpack their state-machine context and delegate. The transpilers + IL-match logic are
    // unchanged — only these bodies shrank.

    // Accept ceiling: scale companyCost2 (the buy-side ceiling) for a needed offer.
    public static CompanyCost PremiumAccept(CompanyCost cost, IsOfferViable task) {
        try {
            var cb = task.CompanyBehaviour;
            var where = task.where.Value?.Object;
            var rd = task.what.Value as ResourceDefinition;
            return cost * Services.Willingness.NeedFactor(cb, where, rd, task.howMuch.Value, Services.Cfg.NeedPremiumApplyToAccepts.Value);
        } catch (Exception ex) {
            Plugin.Log.LogError($"NeedPremium.PremiumAccept failed: {ex}");
            return cost;
        }
    }

    // Posted bid: scale the num term (cost * makeOfferUnitCostMultiplier) before the ceil(max(...)) at :56.
    // deliveryCost.Money stays the untouched hard floor.
    public static double PremiumBid(double num, MakeOfferPriorityGate gate) {
        try {
            var cb = gate.CompanyBehaviour;
            var where = gate.where.Value?.Object;
            var rd = gate.what.Value as ResourceDefinition;
            return num * Services.Willingness.NeedFactor(cb, where, rd, gate.howMuch.Value, Services.Cfg.NeedPremiumApplyToPostedBids.Value);
        } catch (Exception ex) {
            Plugin.Log.LogError($"NeedPremium.PremiumBid failed: {ex}");
            return num;
        }
    }

    // Proactive obtain: scale the willingness Magnitude of the buy-from-offers ceiling for a needed good,
    // before it is divided by count and ceil'd into maxPricePerUnit. Distinct method + market action from
    // the accept path (this one FullFills offers directly, never through IsOfferViable) → no double-apply.
    public static double PremiumObtain(double magnitude, CompanyBehaviour cb, ObjectInfo where, MyIDScriptableObject what, SharedFloat howMuch) {
        try {
            var rd = what as ResourceDefinition;
            double qty = howMuch != null ? howMuch.Value : 0.0;
            return magnitude * Services.Willingness.NeedFactor(cb, where, rd, qty, Services.Cfg.NeedPremiumApplyToProactiveObtain.Value);
        } catch (Exception ex) {
            Plugin.Log.LogError($"NeedPremium.PremiumObtain failed: {ex}");
            return magnitude;
        }
    }

    internal static MethodBase MoveNextOf(Type declaring, string asyncMethod) {
        var m = AccessTools.Method(declaring, asyncMethod);
        var sm = m?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        return sm != null ? AccessTools.Method(sm, nameof(IAsyncStateMachine.MoveNext)) : null!;
    }
}

// H-accepts — IsOfferViable.TaskFunction. Match the buyer-branch store
// `companyCost2 = companyCost1 * takeOfferBuyUnitCostMultiplier` (ldfld takeOfferBuyUnitCostMultiplier +
// op_Multiply(CompanyCost,CompanyCost)) and inject PremiumAccept between the multiply and the store.
[HarmonyPatch]
static class NeedPremiumAccept {
    static readonly FieldInfo BuyMult = AccessTools.Field(
        typeof(CompanyDefinition.CompanyAIConfig), nameof(CompanyDefinition.CompanyAIConfig.takeOfferBuyUnitCostMultiplier));
    static readonly MethodInfo OpMul = AccessTools.Method(
        typeof(CompanyCost), "op_Multiply", new[] { typeof(CompanyCost), typeof(CompanyCost) });
    static readonly MethodInfo Helper = AccessTools.Method(
        typeof(NeedPremiumHooks), nameof(NeedPremiumHooks.PremiumAccept));

    static MethodBase TargetMethod() => NeedPremiumHooks.MoveNextOf(typeof(IsOfferViable), "TaskFunction");

    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase original) {
        var self = AccessTools.Field(original.DeclaringType, "<>4__this");
        var swapped = 0;
        var armed = false;
        foreach (var ins in instructions) {
            yield return ins;
            if (ins.LoadsField(BuyMult)) { armed = true; continue; }
            if (armed && ins.Calls(OpMul)) {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, self);
                yield return new CodeInstruction(OpCodes.Call, Helper);
                swapped++;
            }
            armed = false;
        }
        if (swapped != 1) {
            Plugin.Log.LogWarning(
                $"NeedPremiumAccept: expected exactly 1 takeOfferBuyUnitCostMultiplier multiply, injected {swapped}. "
                + "Accept-side need premium inactive; vanilla behavior preserved.");
        }
    }
}

// H-bids — MakeOfferPriorityGate.InternalGetCost. Match `num = costThreshold.Magnitude *
// (double)makeOfferUnitCostMultiplier` (ldfld makeOfferUnitCostMultiplier + mul) and inject PremiumBid
// between the multiply and the store, so the posted pricePerUnit rises but the delivery-cost floor doesn't.
[HarmonyPatch]
static class NeedPremiumBid {
    static readonly FieldInfo MakeMult = AccessTools.Field(
        typeof(CompanyDefinition.CompanyAIConfig), nameof(CompanyDefinition.CompanyAIConfig.makeOfferUnitCostMultiplier));
    static readonly MethodInfo Helper = AccessTools.Method(
        typeof(NeedPremiumHooks), nameof(NeedPremiumHooks.PremiumBid));

    static MethodBase TargetMethod() => NeedPremiumHooks.MoveNextOf(typeof(MakeOfferPriorityGate), "InternalGetCost");

    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase original) {
        var self = AccessTools.Field(original.DeclaringType, "<>4__this");
        var swapped = 0;
        var armed = false;
        foreach (var ins in instructions) {
            yield return ins;
            if (ins.LoadsField(MakeMult)) { armed = true; continue; }
            if (armed && ins.opcode == OpCodes.Mul) {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, self);
                yield return new CodeInstruction(OpCodes.Call, Helper);
                swapped++;
            }
            armed = false;
        }
        if (swapped != 1) {
            Plugin.Log.LogWarning(
                $"NeedPremiumBid: expected exactly 1 makeOfferUnitCostMultiplier multiply, injected {swapped}. "
                + "Bid-side need premium inactive; vanilla behavior preserved.");
        }
    }
}

// H-obtain — BuyFromOffersPriorityGate.Calc (the proactive demand-tied buy-from-offers path, :91). Match
// `(costThreshold * makeOfferUnitCostMultiplier).Magnitude` (ldfld makeOfferUnitCostMultiplier + op_Multiply
// + get_Magnitude) and inject PremiumObtain on the Magnitude, before the /count and ceil into maxPricePerUnit.
// Static method → no <>4__this; the hoisted cb/where/what/howMuch fields carry the context.
[HarmonyPatch]
static class NeedPremiumObtain {
    static readonly FieldInfo MakeMult = AccessTools.Field(
        typeof(CompanyDefinition.CompanyAIConfig), nameof(CompanyDefinition.CompanyAIConfig.makeOfferUnitCostMultiplier));
    static readonly MethodInfo Magnitude = AccessTools.PropertyGetter(typeof(CompanyCost), nameof(CompanyCost.Magnitude));
    static readonly MethodInfo Helper = AccessTools.Method(
        typeof(NeedPremiumHooks), nameof(NeedPremiumHooks.PremiumObtain));

    static MethodBase TargetMethod() => NeedPremiumHooks.MoveNextOf(typeof(BuyFromOffersPriorityGate), "Calc");

    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase original) {
        var cb = AccessTools.Field(original.DeclaringType, "cb");
        var where = AccessTools.Field(original.DeclaringType, "where");
        var what = AccessTools.Field(original.DeclaringType, "what");
        var howMuch = AccessTools.Field(original.DeclaringType, "howMuch");
        var swapped = 0;
        var armed = false;
        foreach (var ins in instructions) {
            yield return ins;
            if (ins.LoadsField(MakeMult)) { armed = true; continue; }
            if (armed && ins.Calls(Magnitude)) {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, cb);
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, where);
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, what);
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, howMuch);
                yield return new CodeInstruction(OpCodes.Call, Helper);
                swapped++;
            }
            armed = false;
        }
        if (swapped != 1) {
            Plugin.Log.LogWarning(
                $"NeedPremiumObtain: expected exactly 1 makeOfferUnitCostMultiplier magnitude, injected {swapped}. "
                + "Proactive-obtain need premium inactive; vanilla behavior preserved.");
        }
    }
}
