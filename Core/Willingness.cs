using System;
using AI;
using AiPlayerIntel.Config;
using Game;
using Game.Info;
using ScriptableObjectScripts;

namespace AiPlayerIntel.Core;

// Owns every price factor as a dimensionless multiplier over base DIY cost, never a from-scratch price (design §8.5).
sealed class Willingness {
    readonly Configuration _config;
    readonly DeficitService _deficit;
    readonly StandingService _standing;

    public Willingness(Configuration config, DeficitService deficit, StandingService standing) {
        _config = config;
        _deficit = deficit;
        _standing = standing;
    }

    // Ceiling on the need premium, derived from the live CatchUpMax so buyMult(0.9) * catchUp * (1+f) stays sane.
    double MaxFraction { get => 3.0 / (_config.CatchUpMax.Value * 0.9) - 1.0; }

    // (1 + needPremium) for a needed good, else 1.0; composes multiplicatively with catch-up at each call site.
    public double NeedFactor(
        CompanyBehaviour? companyBehaviour,
        ObjectInfo? where,
        ResourceDefinition? resourceDefinition,
        double howMuch,
        bool surfaceOn
    ) {
        if (_config == null || !_config.MasterEnable.Value || !_config.NeedPremiumEnable.Value || !surfaceOn) {
            return 1.0;
        }
        if (companyBehaviour == null || where == null || resourceDefinition == null) { return 1.0; }

        var deficit = _deficit.Evaluate(companyBehaviour, where, resourceDefinition);
        if (deficit.Class != NeedClass.ContractLinked) { return 1.0; } // non-needed → no premium

        var cap = Math.Max(MaxFraction, Configuration.NeedPremiumFractionCeiling);
        var fraction = Math.Min(_config.NeedPremiumFraction.Value, cap);
        if (fraction <= 0.0) { return 1.0; }
        if (_config.NeedPremiumCapToDeficit.Value && howMuch > 0.0) {
            var premiumQuantity = Math.Min(howMuch, deficit.UnmetVsNeed);
            if (premiumQuantity <= 0.0) { return 1.0; }
            fraction *= premiumQuantity / howMuch;
        }
        return 1.0 + fraction;
    }

    // Cost-of-time multiplier for a trailing company; leader → 1.0 (design §8.4).
    public double CostOfTimeFactor(Company company) => _standing.CatchUpFactor(company);

    // Multiplier-only by design: the base DIY oracle (ObtainResourcePriorityGate.Calc) is async — it awaits four
    // sub-gates (ObtainResourcePriorityGate.cs:113/117/121/125) — so folding it into the arbiter's sync OrderBy sort key would deadlock (design §5.3).
    public double WhatWillPay(
        CompanyBehaviour? companyBehaviour,
        ObjectInfo? where,
        ResourceDefinition? resourceDefinition,
        double quantity
    ) {
        var company = companyBehaviour?.Company;
        var catchUp = company != null ? CostOfTimeFactor(company) : 1.0;
        return catchUp * NeedFactor(companyBehaviour, where, resourceDefinition, quantity, true);
    }
}
