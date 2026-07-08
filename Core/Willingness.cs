using System;
using AI;
using AiPlayerIntel.Config;
using Game;
using Game.Info;
using ScriptableObjectScripts;

namespace AiPlayerIntel.Core;

// The one home for every price factor (design §8.5). Owns the need-premium (1+frac) math (moved out of
// NeedPremium.cs) and delegates catch-up to StandingService. Injected deps; the static patch boundary
// reads it through the Services holder. Returns dimensionless multipliers over the game's base DIY cost —
// never a from-scratch price (the base already encodes transit+distance+the Money/Time fold, wtp/transit).
sealed class Willingness {
    // Live clamp on the effective need fraction; keeps the composed ceiling market-sane even with a
    // hand-set Fraction (design §5.4). Derived: assumedMaxCatchUp(2.0)*buyMult(0.9)*(1+f) <= 3.
    const double MaxFraction = 3.0 / (2.0 * 0.9) - 1.0;   // ~0.667

    readonly Cfg _cfg;
    readonly DeficitService _deficit;
    readonly StandingService _standing;

    public Willingness(Cfg cfg, DeficitService deficit, StandingService standing) {
        _cfg = cfg;
        _deficit = deficit;
        _standing = standing;
    }

    // (1 + needPremium) for a needed (contract-linked, deficit-capped) good, else 1.0. Catch-up rides the
    // DIY basis upstream (CalcCostMagnitude), so the two factors compose multiplicatively by call site.
    public double NeedFactor(CompanyBehaviour? cb, ObjectInfo? where, ResourceDefinition? rd, double howMuch, bool surfaceOn) {
        if (_cfg == null || !_cfg.MasterEnable.Value || !_cfg.NeedPremiumEnable.Value || !surfaceOn) { return 1.0; }
        if (cb == null || where == null || rd == null) { return 1.0; }

        var d = _deficit.Evaluate(cb, where, rd);
        if (d.Class != NeedClass.ContractLinked) { return 1.0; }   // non-needed → no premium

        double frac = Math.Min(_cfg.NeedPremiumFraction.Value, MaxFraction);
        if (frac <= 0.0) { return 1.0; }
        if (_cfg.NeedPremiumCapToDeficit.Value && howMuch > 0.0) {
            double premiumQty = Math.Min(howMuch, d.UnmetVsNeed);
            if (premiumQty <= 0.0) { return 1.0; }
            frac *= premiumQty / howMuch;
        }
        return 1.0 + frac;
    }

    // Price-space catch-up (design §8.4): the cost-of-time multiplier for a trailing company; leader → 1.0.
    public double CostOfTimeFactor(Company c) => _standing.CatchUpFactor(c);

    // Composite max unit price the AI will pay, as a multiplier over base DIY (design §8.1). The intel tab
    // (J) is a follow-up on this; at the buy sites the ceilings apply NeedFactor and the CalcCostMagnitude
    // basis Postfix applies CostOfTimeFactor, so the full product is realized by call-site composition.
    public double WhatWillPay(CompanyBehaviour? cb, ObjectInfo? where, ResourceDefinition? rd, double qty) {
        var company = cb?.Company;
        double catchUp = company != null ? CostOfTimeFactor(company) : 1.0;
        return catchUp * NeedFactor(cb, where, rd, qty, true);
    }
}
