using AiPlayerIntel.Config;

namespace AiPlayerIntel.Core;

static class Services {
    internal static Cfg Cfg = null!;
    internal static DeficitService Deficit = null!;
    internal static StandingService Standing = null!;
    internal static Willingness Willingness = null!;

    internal static void Init(Cfg cfg) {
        Cfg = cfg;
        Deficit = new DeficitService();
        Standing = new StandingService(cfg);
        Willingness = new Willingness(cfg, Deficit, Standing);
    }
}
