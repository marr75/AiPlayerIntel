using AiPlayerIntel.Config;

namespace AiPlayerIntel.Core;

static class Services {
    internal static Configuration Config { get; private set; } = null!;
    internal static DeficitService Deficit = null!;
    internal static StandingService Standing = null!;
    internal static Willingness Willingness = null!;
    internal static OfferArbiter Arbiter = null!;

    internal static void Init(Configuration config) {
        Config = config;
        Deficit = new DeficitService();
        Standing = new StandingService(config);
        Willingness = new Willingness(config, Deficit, Standing);
        Arbiter = new OfferArbiter(config, Deficit, Standing, Willingness);
    }
}
