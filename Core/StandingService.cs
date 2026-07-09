using System;
using System.Collections.Generic;
using AiPlayerIntel.Config;
using Game;
using Manager;

namespace AiPlayerIntel.Core;

// Completed-contract standing index (wtp §1): a monotone, cross-company-comparable progress measure
// (all AIs traverse the same unlock DAG from one root). Feeds BOTH catch-up expressions (design §2c):
// CatchUpFactor (price-space, this slice) and Trailing (ordering-space, slice 7's FarthestBehind).
// Cached; refreshed once per game day to avoid re-counting allContracts on every Magnitude call.
sealed class StandingService {
    readonly Configuration _cfg;
    readonly Dictionary<string, int> _completed = new();
    int _leader;
    int _lastDay = int.MinValue;

    public StandingService(Configuration cfg) { _cfg = cfg; }

    // leader - CompletedCount(c); 0 for the leader or the player. Ordering-space (slice 7).
    public int Trailing(Company c) {
        if (c == null || c.IsPlayer || c.ID == null) { return 0; }
        EnsureFresh();
        int done = _completed.TryGetValue(c.ID, out var n) ? n : 0;
        return Math.Max(0, _leader - done);
    }

    // clamp(1 + KCatchUp*trailingNorm, 1, MaxCatchUp); leader/player → 1.0. Price-space.
    public double CatchUpFactor(Company c) {
        if (c == null || c.IsPlayer || c.ID == null) { return 1.0; }
        EnsureFresh();
        int done = _completed.TryGetValue(c.ID, out var n) ? n : 0;
        int trailing = Math.Max(0, _leader - done);
        if (trailing <= 0) { return 1.0; }
        double span = _cfg.CatchUpStandingSpan.Value > 0 ? _cfg.CatchUpStandingSpan.Value : Math.Max(1, _leader);
        double trailingNorm = Math.Min(1.0, Math.Max(0.0, trailing / Math.Max(1.0, span)));
        double factor = 1.0 + _cfg.CatchUpK.Value * trailingNorm;
        return Math.Min(factor, Math.Max(1.0, _cfg.CatchUpMax.Value));
    }

    void EnsureFresh() {
        var tc = MonoBehaviourSingleton<TimeController>.Instance;
        int day = tc != null ? tc.TotalDays : 0;
        if (day == _lastDay && _completed.Count > 0) { return; }
        _lastDay = day;
        Recompute();
    }

    // Completed-contract count per AI company over ContractManager.allContracts (wtp §1); leader = max.
    public void Recompute() {
        _completed.Clear();
        _leader = 0;
        var gm = MonoBehaviourSingleton<GameManager>.Instance;
        var cm = MonoBehaviourSingleton<ContractManager>.Instance;
        if (gm?.Companies == null || cm?.allContracts == null) { return; }
        foreach (var c in gm.Companies) {
            if (c == null || c.IsPlayer || c.ID == null) { continue; }
            int done = 0;
            foreach (var k in cm.allContracts) {
                if (k != null && k.ContractStateForCompany(c) == ContractManager.EContractState.Completed) { done++; }
            }
            _completed[c.ID] = done;
            if (done > _leader) { _leader = done; }
        }
    }
}
