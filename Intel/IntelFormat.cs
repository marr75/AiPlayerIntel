using System;
using System.Collections.Generic;
using UnityEngine;

namespace AiPlayerIntel.Intel;

static class IntelFormat {
    internal const string PriceLegend =
        "Self-source = AI's own per-unit make/mine cost ($materials + delay days, each day priced at its time value). "
        + "Max buy = most it'll pay per unit for a batch that size (10% under self-source). "
        + "Priced at the row's need qty (bigger batches lower the per-unit ceiling as fixed costs amortize); "
        + "a sell offer priced above max buy is ignored.";

    internal const string BehaviorLegend =
        "AIs also buy any resource offered below their Max buy price even with no listed need, "
        + "and rarely post buy-bids (mostly buy reactively). Bodies tagged (HQ) are self-sourced — the AI never buys there.";

    internal static readonly GUIContent HqTag = new(
        "  (HQ — won't buy here)",
        "The AI self-sources at its main object and never buys market offers here."
    );

    // Columnar cell strings for the UGUI leaf row (one value per Cols[] slot).
    internal static string Have(ResourceLine line) => Magnitude(line.Have);

    internal static string Want(ResourceLine line) => line.PrimaryQty > 0 ? Magnitude(line.PrimaryQty) : "";

    internal static string RateEta(ResourceLine line) {
        var parts = new List<string>();
        if (line.Rate is { } rate && Math.Abs(rate) >= 0.05) {
            parts.Add($"{(rate >= 0 ? "+" : "")}{Magnitude(rate)}/day");
        }
        if (line.EtaDays is { } eta) { parts.Add($"ETA ~{eta:0} d"); }
        return string.Join(" · ", parts);
    }

    internal static string MaxBuy(ResourceLine line) =>
        line.MaxBid is { } maxBid ? $"{Magnitude(line.PriceQty)}u @ {Money(maxBid)}/u" : "";

    internal static string Magnitude(double value) {
        var magnitude = Math.Abs(value);
        if (magnitude < 10) { return $"{value:0.#}"; }
        if (magnitude < 1000) { return $"{value:0}"; }
        if (magnitude < 1_000_000) { return $"{value / 1e3:0.#}K"; }
        if (magnitude < 1_000_000_000) { return $"{value / 1e6:0.#}M"; }
        return $"{value / 1e9:0.#}B";
    }

    internal static string Money(double value) => $"${Magnitude(value)}";
}
