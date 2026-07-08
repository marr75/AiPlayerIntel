using AiPlayerIntel.Config;

namespace AiPlayerIntel.Core;

static class Services {
    internal static Cfg Cfg = null!;
    internal static DeficitService Deficit = null!;

    internal static void Init(Cfg cfg) {
        Cfg = cfg;
        Deficit = new DeficitService();
    }
}
