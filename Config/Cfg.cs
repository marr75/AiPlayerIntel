using BepInEx.Configuration;

namespace AiPlayerIntel.Config;

sealed class Cfg {
    public readonly ConfigEntry<bool> MasterEnable;
    public readonly ConfigEntry<bool> ZeroDemandGate;
    public readonly ConfigEntry<bool> ClampBuyQuantity;
    public readonly ConfigEntry<bool> StrictMode;

    public readonly ConfigEntry<bool> UnstickEnable;
    public readonly ConfigEntry<float> StuckDays;
    public readonly ConfigEntry<bool> SkipResearch;
    public readonly ConfigEntry<bool> SkipScheduleFly;
    public readonly ConfigEntry<bool> SkipModuleDeliver;

    public readonly ConfigEntry<bool> ShowAllMarkets;

    public Cfg(ConfigFile c) {
        MasterEnable = c.Bind("Gate", "MasterEnable", true,
            "Master kill-switch for all behaviour patches (zero-demand gate + buy-quantity clamp).");
        ZeroDemandGate = c.Bind("Gate", "ZeroDemandGate", true,
            "Reject need-less opportunistic AI buys (flip IsOfferViable to Failure).");
        ClampBuyQuantity = c.Bind("Gate", "ClampBuyQuantity", true,
            "Clamp an AI buy to its outstanding reservation-netted deficit.");
        StrictMode = c.Bind("Gate", "StrictMode", false,
            "Block zero-demand permanently vs admit after Tier-1 exhausted.");

        UnstickEnable = c.Bind("Stuck", "Enabled", true,
            "Credit the reservation-netted deficit to an AI stalled on a resource need "
            + "and cancel its now-moot BUY offer, so its behaviour tree resumes.");
        StuckDays = c.Bind("Stuck", "StuckDays",
            30f, new ConfigDescription(
                "Game-days a first-incomplete objective may stall before a resource-zap.",
                new AcceptableValueRange<float>(5f, 365f)));
        SkipResearch = c.Bind("Stuck", "SkipResearch", true,
            "Leave MakeResearch objectives alone (no single creditable resource).");
        SkipScheduleFly = c.Bind("Stuck", "SkipScheduleFly", true,
            "Leave ScheduleFly / GravityAssist objectives alone (no single creditable resource).");
        SkipModuleDeliver = c.Bind("Stuck", "SkipModuleDeliver", true,
            "Leave module/crew Deliver objectives alone (target is counts, not stock).");

        ShowAllMarkets = c.Bind("Intel", "ShowAllMarkets", true,
            "Compute max-viable-price (Max Buy) for every resource x market, not just BOM/posted rows. "
            + "Multiplies DIY Calc calls; throttled by the same DIY-active gate. Off = vanilla-slice-0 behaviour.");
    }

    public void Validate() { }
}
