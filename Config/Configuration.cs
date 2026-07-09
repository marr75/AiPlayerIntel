using BepInEx.Configuration;
using UnityEngine;

namespace AiPlayerIntel.Config;

enum MarketBuyOrder { Vanilla, ContractFirst, ContractOnly }

enum MarketBuySequence { FarthestBehind, PriceAscending, PriceDescending }

enum RecomputeCadence { Daily, OnContractComplete }

sealed class Configuration {
    public readonly ConfigEntry<bool> CatchUpEnable;
    public readonly ConfigEntry<float> CatchUpK;
    public readonly ConfigEntry<float> CatchUpMax;
    public readonly ConfigEntry<RecomputeCadence> CatchUpRecomputeCadence;
    public readonly ConfigEntry<float> CatchUpStandingSpan;
    public readonly ConfigEntry<bool> CatchUpTimeOnly;
    public readonly ConfigEntry<bool> ClampBuyQuantity;
    public readonly ConfigEntry<float> GrantLeaseDays;
    public readonly ConfigEntry<MarketBuyOrder> MarketBuyOrder;
    public readonly ConfigEntry<MarketBuySequence> MarketBuySequence;

    public readonly ConfigEntry<bool> MasterEnable;
    public readonly ConfigEntry<bool> NeedPremiumApplyToAccepts;
    public readonly ConfigEntry<bool> NeedPremiumApplyToPostedBids;
    public readonly ConfigEntry<bool> NeedPremiumApplyToProactiveObtain;
    public readonly ConfigEntry<bool> NeedPremiumCapToDeficit;

    public readonly ConfigEntry<bool> NeedPremiumEnable;
    public readonly ConfigEntry<float> NeedPremiumFraction;

    public readonly ConfigEntry<bool> ObserveLogFills;

    public readonly ConfigEntry<bool> PostBidsEnable;
    public readonly ConfigEntry<float> PostBidsTimeThreshold;
    public readonly ConfigEntry<bool> PremiumOrdering;
    public readonly ConfigEntry<float> PriorityWindowDays;
    public readonly ConfigEntry<float> RefreshSeconds;

    public readonly ConfigEntry<bool> ShowAllMarkets;
    public readonly ConfigEntry<float> StuckDays;
    public readonly ConfigEntry<KeyCode> ToggleKey;

    public readonly ConfigEntry<bool> UnstickEnable;

    public Configuration(ConfigFile c) {
        ToggleKey = c.Bind(
            "General",
            "ToggleKey",
            KeyCode.F10,
            "Open/close the AI Player Intel panel."
        );
        RefreshSeconds = c.Bind(
            "General",
            "RefreshSeconds",
            4f,
            "Seconds between snapshot recomputes (clamped 1-30)."
        );

        MasterEnable = c.Bind(
            "Gate",
            "MasterEnable",
            true,
            "Master kill-switch for all behaviour patches (zero-demand gate + buy-quantity clamp)."
        );
        MarketBuyOrder = c.Bind(
            "Gate",
            "MarketBuyOrder",
            Config.MarketBuyOrder.ContractFirst,
            "Vanilla = mod stays out of buy eligibility. ContractFirst (recommended) = contract/demand-linked "
            + "buys prioritised as a group, opportunistic buys allowed after (needs the arbiter, slice 7; until "
            + "then falls back to Vanilla eligibility). ContractOnly = refuse opportunistic buys entirely (the "
            + "shipped Failure-flip; realizable now)."
        );
        MarketBuySequence = c.Bind(
            "Gate",
            "MarketBuySequence",
            Config.MarketBuySequence.FarthestBehind,
            "Within a cohort, who buys first. FarthestBehind (default) = trailing AI first (ordering-space "
            + "catch-up; the price-space catch-up is the separate [CatchUp] lever). PriceAscending = cheapest-"
            + "willing buyer first. PriceDescending = most-willing first. Sequencing is arbiter-gated (slice 7)."
        );
        ClampBuyQuantity = c.Bind(
            "Gate",
            "ClampBuyQuantity",
            true,
            "Clamp an AI buy to its outstanding reservation-netted deficit."
        );
        PriorityWindowDays = c.Bind(
            "Gate",
            "PriorityWindowDays",
            3f,
            new ConfigDescription(
                "Arbiter priority window (game-days) per contested player-sell offer. While open, non-best-fit "
                + "buyers defer to the ranked winner; once it expires the offer falls back to vanilla first-come "
                + "so an offer never stalls. Keep short (a few days) so a transient-demand teardown can't strand it.",
                new AcceptableValueRange<float>(1f, 30f)
            )
        );
        GrantLeaseDays = c.Bind(
            "Gate",
            "GrantLeaseDays",
            2f,
            new ConfigDescription(
                "Arbiter grant lease (game-days). A granted buyer holds exclusive claim for this long; on expiry "
                + "the grant is released (and re-ranked) so a crashed or declining evaluation can't park the others "
                + "forever. Keep below PriorityWindowDays.",
                new AcceptableValueRange<float>(0.5f, 15f)
            )
        );
        PremiumOrdering = c.Bind(
            "Gate",
            "PremiumOrdering",
            true,
            "For the PriceAscending/PriceDescending sub-orders, rank contesters by their per-buyer willingness "
            + "(Willingness.WhatWillPay). Off = cheap fallback: rank by outstanding deficit magnitude. "
            + "FarthestBehind ignores this (it ranks by standing)."
        );

        ObserveLogFills = c.Bind(
            "Observe",
            "LogFills",
            true,
            "Log every committed AI market fill (buyer, resource, quantity, need class) for telemetry. "
            + "Observe-only; never blocks a buy."
        );

        CatchUpEnable = c.Bind(
            "CatchUp",
            "Enabled",
            false,
            "Price-space catch-up: a trailing AI (fewer completed contracts than the leader) weights its "
            + "cost-of-time more heavily at the DIY basis (CalcCostMagnitude), so it values time everywhere "
            + "- favouring fast buy/deliver over slow mine/refine, bidding higher, and raising its sell "
            + "floor. Leader is unaffected (factor 1.0). Default off: it shifts AI path selection, not just "
            + "market bids."
        );
        CatchUpK = c.Bind(
            "CatchUp",
            "KCatchUp",
            1.0f,
            new ConfigDescription(
                "Slope on the normalized trailing gap: factor = clamp(1 + KCatchUp*trailingNorm, 1, MaxCatchUp).",
                new AcceptableValueRange<float>(0f, 5f)
            )
        );
        CatchUpMax = c.Bind(
            "CatchUp",
            "MaxCatchUp",
            2.0f,
            new ConfigDescription(
                "Upper clamp on the catch-up factor. Widest reach (scales every cost); keep modest.",
                new AcceptableValueRange<float>(1f, 2.5f)
            )
        );
        CatchUpStandingSpan = c.Bind(
            "CatchUp",
            "StandingSpan",
            0f,
            new ConfigDescription(
                "Normalization denominator for the trailing gap. 0 = auto (the leader's completed-contract count).",
                new AcceptableValueRange<float>(0f, 100f)
            )
        );
        CatchUpTimeOnly = c.Bind(
            "CatchUp",
            "TimeOnly",
            true,
            "Scale only the time term of the cost (exact for Sum and Magnitude companies). Off = scale the "
            + "whole magnitude. Max/Min companies always scale whole-magnitude (exact time re-collapse is "
            + "ill-defined)."
        );
        CatchUpRecomputeCadence = c.Bind(
            "CatchUp",
            "RecomputeCadence",
            RecomputeCadence.Daily,
            "How often the completed-contract standing cache refreshes. Daily = once per game day. "
            + "OnContractComplete is reserved for slice-7 event wiring; currently daily-throttled either way."
        );

        NeedPremiumEnable = c.Bind(
            "NeedPremium",
            "Enabled",
            true,
            "AI companies pay a premium for goods they NEED (contract/demand-linked + in-BOM at the offer's "
            + "location) over goods they don't. Lets a needing company outbid a non-needing one and makes "
            + "need-scouting profitable. Price-space lever, applies under every MarketBuyOrder including Vanilla."
        );
        NeedPremiumFraction = c.Bind(
            "NeedPremium",
            "Fraction",
            0.25f,
            new ConfigDescription(
                "Premium added to the willingness/bid for a needed good, e.g. 0.25 = +25%. "
                + "Only applies to the deficit-capped quantity (see CapToDeficit); non-needed goods get none.",
                new AcceptableValueRange<float>(0f, 1f)
            )
        );
        NeedPremiumApplyToAccepts = c.Bind(
            "NeedPremium",
            "ApplyToAccepts",
            true,
            "Raise the accept ceiling for offers of needed goods (IsOfferViable.TaskFunction)."
        );
        NeedPremiumApplyToPostedBids = c.Bind(
            "NeedPremium",
            "ApplyToPostedBids",
            true,
            "Raise the posted buy-offer price for needed goods (MakeOfferPriorityGate.InternalGetCost)."
        );
        NeedPremiumApplyToProactiveObtain = c.Bind(
            "NeedPremium",
            "ApplyToProactiveObtain",
            true,
            "Raise the willingness ceiling on the proactive demand-tied buy-from-offers path "
            + "(BuyFromOffersPriorityGate), which scans and fulfils existing offers without routing through "
            + "the accept ceiling. Distinct method/action from ApplyToAccepts, so no double-application."
        );
        NeedPremiumCapToDeficit = c.Bind(
            "NeedPremium",
            "CapToDeficit",
            true,
            "Apply the premium only to min(quantity, outstanding deficit); surplus above the need is priced at base."
        );

        UnstickEnable = c.Bind(
            "Stuck",
            "Enabled",
            true,
            "Credit the reservation-netted deficit to an AI stalled on a resource need "
            + "and cancel its now-moot BUY offer, so its behaviour tree resumes."
        );
        StuckDays = c.Bind(
            "Stuck",
            "StuckDays",
            30f,
            new ConfigDescription(
                "Game-days a first-incomplete objective may stall before a resource-zap.",
                new AcceptableValueRange<float>(5f, 365f)
            )
        );

        PostBidsEnable = c.Bind(
            "PostBids",
            "Enabled",
            false,
            "AI companies post standing BUY offers more readily. Vanilla only advertises a buy bid when its "
            + "cheapest self-source path is at least makeOfferTimeThreshold (365) game-days out; lowering that "
            + "gate (TimeThreshold) makes an AI post buy orders for needs it would otherwise self-source, giving "
            + "the player more orders to fill. Behaviour-changing; default off."
        );
        PostBidsTimeThreshold = c.Bind(
            "PostBids",
            "TimeThreshold",
            365f,
            new ConfigDescription(
                "The self-source-time gate (game-days) at/above which an AI posts a buy bid instead of doing it "
                + "itself. Vanilla is 365. Lower it to make AIs bid sooner and more often; only ever lowers a "
                + "company's gate, never raises it (a no-op at 365). Live-retunable.",
                new AcceptableValueRange<float>(30f, 365f)
            )
        );

        ShowAllMarkets = c.Bind(
            "Intel",
            "ShowAllMarkets",
            true,
            "Compute max-viable-price (Max Buy) for every resource x market, not just BOM/posted rows. "
            + "Multiplies DIY Calc calls; throttled by the same DIY-active gate. Off = vanilla-slice-0 behaviour."
        );
    }

    // Keep the composed willingness product market-sane (design §5.4). The need premium multiplies the DIY
    // ceiling after the vanilla takeOfferBuyUnitCostMultiplier (~0.9) and the [CatchUp] MaxCatchUp factor.
    // Warn if the worst-case stack would exceed maxWillingnessMultiplier (3.0); the Willingness helper also
    // clamps the effective need fraction live, so no combination produces a runaway ceiling.
    public void Validate() {
        const double buyMult = 0.9, maxWillingness = 3.0;
        var worst = CatchUpMax.Value * buyMult * (1.0 + NeedPremiumFraction.Value);
        if (worst > maxWillingness) {
            Plugin.Log.LogWarning(
                $"NeedPremium.Fraction={NeedPremiumFraction.Value} pushes the worst-case willingness stack to "
                + $"{worst:0.00}x (> {maxWillingness}x); it will be clamped live to keep the market sane."
            );
        }
    }
}
