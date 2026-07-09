using System;
using System.Collections.Generic;
using AiPlayerIntel.Config;
using Game;
using Manager;

namespace AiPlayerIntel.Core;

// Monotone completed-contract standing index feeding both catch-up expressions; refreshed once per game day (wtp §1, design §2c).
sealed class StandingService {
    readonly Dictionary<string, int> _completed = new();
    readonly Configuration _config;
    int _lastDay = int.MinValue;
    int _leader;

    public StandingService(Configuration config) { _config = config; }

    // leader - CompletedCount(c); 0 for the leader or the player. Ordering-space (slice 7).
    public int Trailing(Company company) {
        if (company == null || company.IsPlayer || company.ID == null) { return 0; }
        EnsureFresh();
        var done = _completed.TryGetValue(company.ID, out var count) ? count : 0;
        return Math.Max(0, _leader - done);
    }

    // clamp(1 + KCatchUp*trailingNorm, 1, MaxCatchUp); leader/player → 1.0. Price-space.
    public double CatchUpFactor(Company company) {
        if (company == null || company.IsPlayer || company.ID == null) { return 1.0; }
        EnsureFresh();
        var done = _completed.TryGetValue(company.ID, out var count) ? count : 0;
        var trailing = Math.Max(0, _leader - done);
        if (trailing <= 0) { return 1.0; }
        double span = _config.CatchUpStandingSpan.Value > 0 ? _config.CatchUpStandingSpan.Value : Math.Max(1, _leader);
        var trailingNorm = Math.Min(1.0, Math.Max(0.0, trailing / Math.Max(1.0, span)));
        var factor = 1.0 + _config.CatchUpK.Value * trailingNorm;
        return Math.Min(factor, Math.Max(1.0, _config.CatchUpMax.Value));
    }

    void EnsureFresh() {
        var timeController = MonoBehaviourSingleton<TimeController>.Instance;
        var day = timeController != null ? timeController.TotalDays : 0;
        if (day == _lastDay && _completed.Count > 0) { return; }
        _lastDay = day;
        Recompute();
    }

    // Completed-contract count per AI company over ContractManager.allContracts (wtp §1); leader = max.
    public void Recompute() {
        _completed.Clear();
        _leader = 0;
        var gameManager = MonoBehaviourSingleton<GameManager>.Instance;
        var contractManager = MonoBehaviourSingleton<ContractManager>.Instance;
        if (gameManager?.Companies == null || contractManager?.allContracts == null) { return; }
        foreach (var company in gameManager.Companies) {
            if (company == null || company.IsPlayer || company.ID == null) { continue; }
            var done = 0;
            foreach (var contract in contractManager.allContracts) {
                if (contract != null
                    && contract.ContractStateForCompany(company) == ContractManager.EContractState.Completed) {
                    done++;
                }
            }
            _completed[company.ID] = done;
            if (done > _leader) { _leader = done; }
        }
    }
}
