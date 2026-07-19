using System;
using BepInEx.Configuration;
using UnityEngine;

namespace AiPlayerIntel.Config;

enum MarketBuyOrder { Vanilla, ContractFirst, ContractOnly }

enum MarketBuySequence { FarthestBehind, PriceAscending, PriceDescending }

enum RecomputeCadence { Daily, OnContractComplete }

sealed class Configuration {
    // Configured ceilings for the two willingness levers; also the live clamp point in Willingness/Validate.
    const float CatchUpMaxCeiling = 10f;
    public const float NeedPremiumFractionCeiling = 10f;

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
    public readonly ConfigEntry<KeyboardShortcut> ToggleKey;

    public readonly ConfigEntry<bool> UnstickEnable;

    public Configuration(ConfigFile c) {
        var toggleKeyDescription = new ConfigDescription("The key that opens and closes the AI Player Intel panel.");
        ToggleKey = c.Bind("General", "PanelHotkey", new KeyboardShortcut(KeyCode.F10), toggleKeyDescription);

        const string refreshSecondsDescription =
            "How often (in seconds) the intel panel recalculates what's shown. Lower updates faster but "
            + "costs a bit more performance.";
        RefreshSeconds = c.Bind(
            "General",
            "PanelRefreshSeconds",
            4f,
            new ConfigDescription(refreshSecondsDescription, new AcceptableValueRange<float>(0.5f, 60f))
        );

        const string masterEnableDescription =
            "Master switch for every AI behavior change in this mod. Off = AI companies act exactly like "
            + "vanilla; the intel panel still works.";
        MasterEnable = c.Bind("Gate", "EnableAiBehaviorChanges", true, masterEnableDescription);

        const string marketBuyOrderDescription =
            "Controls whether AI companies buy for contracts/production needs before buying opportunistically. "
            + "Vanilla = no change. ContractFirst (recommended) = needed purchases go first, browsing purchases "
            + "after. ContractOnly = AIs never buy opportunistically.";
        MarketBuyOrder = c.Bind(
            "Gate",
            "BuyPriorityMode",
            Config.MarketBuyOrder.ContractFirst,
            marketBuyOrderDescription
        );

        const string marketBuySequenceDescription =
            "When several AI companies want the same thing, who gets to buy first: the one furthest behind "
            + "(FarthestBehind, default), the cheapest bidder (PriceAscending), or the highest bidder "
            + "(PriceDescending).";
        MarketBuySequence = c.Bind(
            "Gate",
            "BuyOrderWithinGroup",
            Config.MarketBuySequence.FarthestBehind,
            marketBuySequenceDescription
        );

        const string clampBuyQuantityDescription =
            "Stops an AI from buying more of something than it actually needs right now.";
        ClampBuyQuantity = c.Bind("Gate", "LimitBuyToActualNeed", true, clampBuyQuantityDescription);

        const string priorityWindowDaysDescription =
            "How many in-game days a \"best fit\" AI gets first crack at a sell offer you posted before it "
            + "opens up to everyone. Keeps a short-lived shortage from getting stuck waiting on one AI.";
        PriorityWindowDays = c.Bind(
            "Gate",
            "ContestedOfferPriorityDays",
            3f,
            new ConfigDescription(priorityWindowDaysDescription, new AcceptableValueRange<float>(0.5f, 90f))
        );

        const string grantLeaseDaysDescription =
            "Once an AI wins that priority window, how many days it keeps exclusive dibs before the "
            + "reservation is re-checked. Keep below ContestedOfferPriorityDays.";
        GrantLeaseDays = c.Bind(
            "Gate",
            "PriorityHoldDays",
            2f,
            new ConfigDescription(grantLeaseDaysDescription, new AcceptableValueRange<float>(0.25f, 60f))
        );

        const string premiumOrderingDescription =
            "When ranking AI buyers by price, use what they're actually willing to pay rather than just how much "
            + "they need. Only matters for the price-based buy orders above.";
        PremiumOrdering = c.Bind("Gate", "RankByWillingnessToPay", true, premiumOrderingDescription);

        const string observeLogFillsDescription = "Writes a line to the mod's log every time an AI company completes a "
            + "purchase. Doesn't change gameplay - for troubleshooting only.";
        ObserveLogFills = c.Bind("Debug", "LogAiPurchases", false, observeLogFillsDescription);

        const string catchupEnableDescription = "Trailing AI companies start valuing speed more - they bid higher to "
            + "close the gap with the leader. On by default.";
        CatchUpEnable = c.Bind("CatchUp", "CatchUpEnabled", true, catchupEnableDescription);

        const string catchUpKDescription = "How fast an AI ramps up its urgency as it falls further behind. "
            + "Higher = trailing AIs get aggressive sooner.";
        var catchUpKDescriptionObject = new ConfigDescription(
            catchUpKDescription,
            new AcceptableValueRange<float>(0f, 20f)
        );
        CatchUpK = c.Bind("CatchUp", "CatchUpAggressiveness", 1.0f, catchUpKDescriptionObject);

        const string catchUpMaxDescription =
            "The most a trailing AI's urgency can multiply its costs/bids by. 2.0 = at most double.";
        CatchUpMax = c.Bind(
            "CatchUp",
            "MaxCatchUpFactor",
            2.0f,
            new ConfigDescription(catchUpMaxDescription, new AcceptableValueRange<float>(1f, CatchUpMaxCeiling))
        );

        const string catchUpStandingSpanDescription =
            "The \"gap size\" used to judge how far behind an AI is. 0 (default) auto-picks the leader's "
            + "completed-contract count; a manual number overrides it for a fixed scale.";
        CatchUpStandingSpan = c.Bind(
            "CatchUp",
            "CatchUpNormalization",
            0f,
            new ConfigDescription(catchUpStandingSpanDescription, new AcceptableValueRange<float>(0f, 1000f))
        );

        const string catchUpTimeOnlyDescription =
            "When on, catch-up urgency only makes AIs value speed more (not raw cost). When off, it scales their "
            + "whole cost estimate.";
        CatchUpTimeOnly = c.Bind("CatchUp", "CatchUpAffectsTimeOnly", true, catchUpTimeOnlyDescription);

        const string catchUpRecomputeCadenceDescription =
            "How often the mod re-checks which AI is the leader. Currently always daily regardless of this "
            + "setting - OnContractComplete currently behaves as Daily, reserved for a future update.";
        CatchUpRecomputeCadence = c.Bind(
            "CatchUp",
            "StandingRefreshCadence",
            RecomputeCadence.Daily,
            catchUpRecomputeCadenceDescription
        );

        const string needPremiumEnableDescription =
            "AI companies pay more for resources they genuinely need, letting a needy company outbid one that's "
            + "just shopping around. On by default.";
        NeedPremiumEnable = c.Bind("NeedPremium", "NeedPremiumEnabled", true, needPremiumEnableDescription);

        const string needPremiumFractionDescription =
            "How much extra a needy AI will pay, as a fraction of the base price - 0.25 = 25% more, 0 "
            + "disables the premium. Only applies to the amount it actually needs, not any extra it's buying.";
        NeedPremiumFraction = c.Bind(
            "NeedPremium",
            "NeedPremiumAmount",
            1.0f,
            new ConfigDescription(
                needPremiumFractionDescription,
                new AcceptableValueRange<float>(0f, NeedPremiumFractionCeiling)
            )
        );

        const string needPremiumApplyToAcceptsDescription =
            "Let the need premium raise the top price an AI will accept when buying directly.";
        NeedPremiumApplyToAccepts = c.Bind(
            "NeedPremium",
            "PremiumOnAcceptedOffers",
            true,
            needPremiumApplyToAcceptsDescription
        );

        const string needPremiumApplyToPostedBidsDescription =
            "Let the need premium raise the price an AI advertises on its own standing buy orders.";
        NeedPremiumApplyToPostedBids = c.Bind(
            "NeedPremium",
            "PremiumOnPostedBids",
            true,
            needPremiumApplyToPostedBidsDescription
        );

        const string needPremiumApplyToProactiveObtainDescription =
            "Let the need premium raise what an AI will pay while actively scanning the market for offers to "
            + "fill (separate code path from the two above, so it needs its own switch).";
        NeedPremiumApplyToProactiveObtain = c.Bind(
            "NeedPremium",
            "PremiumOnActiveShopping",
            true,
            needPremiumApplyToProactiveObtainDescription
        );

        const string needPremiumCapToDeficitDescription =
            "Only pay the premium on the amount the AI is actually short - any extra it buys on top is priced "
            + "normally.";
        NeedPremiumCapToDeficit = c.Bind(
            "NeedPremium",
            "PremiumOnlyForShortfall",
            true,
            needPremiumCapToDeficitDescription
        );

        const string unstickEnableDescription =
            "If an AI company is stuck too long trying to get a resource, hand it the resource it's missing and "
            + "cancel the now-pointless buy order, so it can move on. On by default.";
        UnstickEnable = c.Bind("Stuck", "UnstickEnabled", true, unstickEnableDescription);

        const string stuckDaysDescription =
            "How many in-game days an AI can stay stuck on the same step before it gets un-stuck.";
        StuckDays = c.Bind(
            "Stuck",
            "StuckThresholdDays",
            365f,
            new ConfigDescription(stuckDaysDescription, new AcceptableValueRange<float>(1f, 1000f))
        );

        const string postBidsEnableDescription =
            "AI companies post more buy orders on the market instead of always sourcing things themselves, "
            + "giving you more offers to sell into. On by default even though it changes AI behavior.";
        PostBidsEnable = c.Bind("PostBids", "MoreAiBuyOrdersEnabled", true, postBidsEnableDescription);

        const string postBidsTimeThresholdDescription =
            "An AI only posts a buy order once its own \"do it myself\" plan would take at least this many "
            + "days; vanilla's threshold is 365. Lower it so AIs advertise buy orders sooner and more often.";
        PostBidsTimeThreshold = c.Bind(
            "PostBids",
            "SelfSourceTimeGateDays",
            30f,
            new ConfigDescription(postBidsTimeThresholdDescription, new AcceptableValueRange<float>(1f, 365f))
        );

        const string showAllMarketsDescription =
            "Show every resource at every market in the intel panel, not just the ones tied to a contract or an "
            + "active offer. Off shows a shorter, more focused list; on shows the full picture.";
        ShowAllMarkets = c.Bind("Intel", "ShowAllResourcesInPanel", true, showAllMarketsDescription);
    }

    // Keep the composed willingness product market-sane. The sane ceiling now tracks the live CatchUpMax and
    // the configured premium range, mirroring Willingness's clamp, so it stops warning spuriously once the
    // ranges widen; the live clamp still keeps any combination from producing a runaway ceiling.
    public void Validate() {
        const double buyMult = 0.9;
        var ceiling = Math.Max(3.0 / (CatchUpMax.Value * buyMult) - 1.0, NeedPremiumFractionCeiling);
        if (!(NeedPremiumFraction.Value > ceiling)) { return; }
        var worst = CatchUpMax.Value * buyMult * (1.0 + NeedPremiumFraction.Value);
        Plugin.Log.LogWarning(
            $"NeedPremium amount={NeedPremiumFraction.Value} pushes the worst-case willingness stack to "
            + $"{worst:0.00}x; it will be clamped live to keep the market sane."
        );
    }
}
