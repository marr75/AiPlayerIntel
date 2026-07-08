using System;
using System.Collections.Generic;
using UnityEngine;

namespace AiPlayerIntel.Intel;

static class IntelFormat {
    internal const string PriceLegend = "Self-source = AI's own per-unit make/mine cost ($materials + delay days, each day priced at its time value). "
        + "Max buy = most it'll pay per unit for a batch that size (10% under self-source). "
        + "Priced at the row's need qty (bigger batches lower the per-unit ceiling as fixed costs amortize); "
        + "a sell offer priced above max buy is ignored.";
    internal const string BehaviorLegend = "AIs also buy any resource offered below their Max buy price even with no listed need, "
        + "and rarely post buy-bids (mostly buy reactively). Bodies tagged (HQ) are self-sourced — the AI never buys there.";

    internal static readonly GUIContent HqTag = new("  (HQ — won't buy here)", "The AI self-sources at its main object and never buys market offers here.");

    // Columnar cell strings for the UGUI leaf row (one value per Cols[] slot).
    internal static string Have(ResourceLine r) => Mag(r.Have);

    internal static string Want(ResourceLine r) => r.PrimaryQty > 0 ? Mag(r.PrimaryQty) : "";

    internal static string RateEta(ResourceLine r) {
        var parts = new List<string>();
        if (r.Rate is { } rate && Math.Abs(rate) >= 0.05) { parts.Add($"{(rate >= 0 ? "+" : "")}{Mag(rate)}/day"); }
        if (r.EtaDays is { } eta) { parts.Add($"ETA ~{eta:0} d"); }
        return string.Join(" · ", parts);
    }

    internal static string MaxBuy(ResourceLine r) =>
        r.MaxBid is { } mb ? $"{Mag(r.PriceQty)}u @ {Money(mb)}/u" : "";

    internal static string Mag(double v) {
        double a = Math.Abs(v);
        if (a < 10) { return $"{v:0.#}"; }
        if (a < 1000) { return $"{v:0}"; }
        if (a < 1_000_000) { return $"{v / 1e3:0.#}K"; }
        if (a < 1_000_000_000) { return $"{v / 1e6:0.#}M"; }
        return $"{v / 1e9:0.#}B";
    }

    internal static string Money(double v) => $"${Mag(v)}";
}
