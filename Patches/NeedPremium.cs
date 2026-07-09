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

// H — Need premium (price-space): AIs pay a configurable premium for goods they NEED, so a needing company
// outbids a non-needing one. Two transpilers inject a scale into async-MoveNext willingness locals. Both fail open.
static class NeedPremiumHooks {
    // Accept ceiling: scale companyCost2 (the buy-side ceiling) for a needed offer.
    public static CompanyCost PremiumAccept(CompanyCost cost, IsOfferViable task) {
        try {
            var companyBehaviour = task.CompanyBehaviour;
            var where = task.where.Value?.Object;
            var resourceDefinition = task.what.Value as ResourceDefinition;
            return cost
                * Services.Willingness.NeedFactor(
                    companyBehaviour,
                    where,
                    resourceDefinition,
                    task.howMuch.Value,
                    Services.Config.NeedPremiumApplyToAccepts.Value
                );
        }
        catch (Exception ex) {
            Plugin.Log.LogError($"NeedPremium.PremiumAccept failed: {ex}");
            return cost;
        }
    }

    // Posted bid: scale the price term before the ceil(max(...)) at :56; the deliveryCost.Money hard floor stays untouched.
    public static double PremiumBid(double price, MakeOfferPriorityGate gate) {
        try {
            var companyBehaviour = gate.CompanyBehaviour;
            var where = gate.where.Value?.Object;
            var resourceDefinition = gate.what.Value as ResourceDefinition;
            return price
                * Services.Willingness.NeedFactor(
                    companyBehaviour,
                    where,
                    resourceDefinition,
                    gate.howMuch.Value,
                    Services.Config.NeedPremiumApplyToPostedBids.Value
                );
        }
        catch (Exception ex) {
            Plugin.Log.LogError($"NeedPremium.PremiumBid failed: {ex}");
            return price;
        }
    }

    // Proactive obtain: scale the buy-from-offers ceiling. Distinct action from accept (never via IsOfferViable) → no double-apply.
    public static double PremiumObtain(
        double magnitude,
        CompanyBehaviour companyBehaviour,
        ObjectInfo where,
        MyIDScriptableObject what,
        SharedFloat howMuch
    ) {
        try {
            var resourceDefinition = what as ResourceDefinition;
            var quantity = howMuch != null ? howMuch.Value : 0.0;
            return magnitude
                * Services.Willingness.NeedFactor(
                    companyBehaviour,
                    where,
                    resourceDefinition,
                    quantity,
                    Services.Config.NeedPremiumApplyToProactiveObtain.Value
                );
        }
        catch (Exception ex) {
            Plugin.Log.LogError($"NeedPremium.PremiumObtain failed: {ex}");
            return magnitude;
        }
    }

    internal static MethodBase MoveNextOf(Type declaring, string asyncMethod) {
        var method = AccessTools.Method(declaring, asyncMethod);
        var stateMachineType = method?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        return stateMachineType != null
            ? AccessTools.Method(stateMachineType, nameof(IAsyncStateMachine.MoveNext))
            : null!;
    }
}

// H-accepts — IsOfferViable.TaskFunction. Match the buyer-branch store companyCost2 = companyCost1 *
// takeOfferBuyUnitCostMultiplier (ldfld takeOfferBuyUnitCostMultiplier + op_Multiply(CompanyCost,CompanyCost))
// and inject PremiumAccept between the multiply and the store.
[HarmonyPatch]
static class NeedPremiumAccept {
    static readonly FieldInfo buyMultiplierField = AccessTools.Field(
        typeof(CompanyDefinition.CompanyAIConfig),
        nameof(CompanyDefinition.CompanyAIConfig.takeOfferBuyUnitCostMultiplier)
    );

    static readonly MethodInfo multiplyOperator = AccessTools.Method(
        typeof(CompanyCost),
        "op_Multiply",
        new[] { typeof(CompanyCost), typeof(CompanyCost) }
    );

    static readonly MethodInfo Helper = AccessTools.Method(
        typeof(NeedPremiumHooks),
        nameof(NeedPremiumHooks.PremiumAccept)
    );

    static bool Prepare() => Services.Config.MasterEnable.Value && Services.Config.NeedPremiumEnable.Value;

    static MethodBase TargetMethod() => NeedPremiumHooks.MoveNextOf(typeof(IsOfferViable), "TaskFunction");

    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase original) {
        var self = AccessTools.Field(original.DeclaringType, "<>4__this");
        var swapped = 0;
        var armed = false;
        foreach (var instruction in instructions) {
            yield return instruction;
            if (instruction.LoadsField(buyMultiplierField)) {
                armed = true;
                continue;
            }
            if (armed && instruction.Calls(multiplyOperator)) {
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
                + "Accept-side need premium inactive; vanilla behavior preserved."
            );
        }
    }
}

// H-bids — MakeOfferPriorityGate.InternalGetCost. Match num = costThreshold.Magnitude *
// (double)makeOfferUnitCostMultiplier (ldfld makeOfferUnitCostMultiplier + mul) and inject PremiumBid between
// the multiply and the store, so the posted pricePerUnit rises but the delivery-cost floor doesn't.
[HarmonyPatch]
static class NeedPremiumBid {
    static readonly FieldInfo makeOfferMultiplierField = AccessTools.Field(
        typeof(CompanyDefinition.CompanyAIConfig),
        nameof(CompanyDefinition.CompanyAIConfig.makeOfferUnitCostMultiplier)
    );

    static readonly MethodInfo Helper = AccessTools.Method(
        typeof(NeedPremiumHooks),
        nameof(NeedPremiumHooks.PremiumBid)
    );

    static bool Prepare() => Services.Config.MasterEnable.Value && Services.Config.NeedPremiumEnable.Value;

    static MethodBase TargetMethod() => NeedPremiumHooks.MoveNextOf(typeof(MakeOfferPriorityGate), "InternalGetCost");

    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase original) {
        var self = AccessTools.Field(original.DeclaringType, "<>4__this");
        var swapped = 0;
        var armed = false;
        foreach (var instruction in instructions) {
            yield return instruction;
            if (instruction.LoadsField(makeOfferMultiplierField)) {
                armed = true;
                continue;
            }
            if (armed && instruction.opcode == OpCodes.Mul) {
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
                + "Bid-side need premium inactive; vanilla behavior preserved."
            );
        }
    }
}

// H-obtain — BuyFromOffersPriorityGate.Calc (proactive demand-tied buy-from-offers path, :91). Match
// (costThreshold * makeOfferUnitCostMultiplier).Magnitude (ldfld makeOfferUnitCostMultiplier + op_Multiply +
// get_Magnitude) and inject PremiumObtain on the Magnitude, before the /count and ceil into maxPricePerUnit.
// Static method → no <>4__this; the hoisted cb/where/what/howMuch fields carry the context.
[HarmonyPatch]
static class NeedPremiumObtain {
    static readonly FieldInfo makeOfferMultiplierField = AccessTools.Field(
        typeof(CompanyDefinition.CompanyAIConfig),
        nameof(CompanyDefinition.CompanyAIConfig.makeOfferUnitCostMultiplier)
    );

    static readonly MethodInfo Magnitude = AccessTools.PropertyGetter(
        typeof(CompanyCost),
        nameof(CompanyCost.Magnitude)
    );

    static readonly MethodInfo Helper = AccessTools.Method(
        typeof(NeedPremiumHooks),
        nameof(NeedPremiumHooks.PremiumObtain)
    );

    static bool Prepare() => Services.Config.MasterEnable.Value && Services.Config.NeedPremiumEnable.Value;

    static MethodBase TargetMethod() => NeedPremiumHooks.MoveNextOf(typeof(BuyFromOffersPriorityGate), "Calc");

    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase original) {
        var companyBehaviour = AccessTools.Field(original.DeclaringType, "cb");
        var where = AccessTools.Field(original.DeclaringType, "where");
        var what = AccessTools.Field(original.DeclaringType, "what");
        var howMuch = AccessTools.Field(original.DeclaringType, "howMuch");
        var swapped = 0;
        var armed = false;
        foreach (var instruction in instructions) {
            yield return instruction;
            if (instruction.LoadsField(makeOfferMultiplierField)) {
                armed = true;
                continue;
            }
            if (armed && instruction.Calls(Magnitude)) {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, companyBehaviour);
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
                + "Proactive-obtain need premium inactive; vanilla behavior preserved."
            );
        }
    }
}
