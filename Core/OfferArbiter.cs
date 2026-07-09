using System;
using System.Collections.Generic;
using System.Linq;
using AI;
using AiPlayerIntel.Config;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Manager;
using ScriptableObjectScripts;

namespace AiPlayerIntel.Core;

// In-memory fair-offer allocator for player SELL offers (research-fair-offer-allocation.md §1-4). Keyed by
// Offer.ID, game-time leases, lazy post-load rebuild, ZERO serialized state. Realizes the two-axis model:
// MarketBuyOrder cohorts (ContractFirst tiers contract-linked deficit buyers ahead of opportunists) and
// MarketBuySequence sub-orders within a cohort. FAILS OPEN everywhere: any missing state, null, or exception
// falls back to vanilla (allow the buy). Leases/window bound every defer so the market never stalls.
sealed class OfferArbiter {
    readonly Configuration _config;
    readonly DeficitService _deficit;
    readonly Dictionary<int, Record> _records = new();
    readonly StandingService _standing;
    readonly Willingness _willingness;

    public OfferArbiter(Configuration config, DeficitService deficit, StandingService standing, Willingness willingness) {
        _config = config;
        _deficit = deficit;
        _standing = standing;
        _willingness = willingness;
    }

    bool Active => _config != null && _config.MasterEnable.Value;

    // MarketOfferManager.AddOffer Postfix — build the record for a freshly posted player SELL offer.
    // Early-out on save-load replay (AddOffer skips the OnNewOffer fan-out then; rebuild lazily at the gate).
    public void OnOfferPosted(Offer offer) {
        try {
            if (!Active || offer == null || LoadSaveManager.OnExtractAllFromSaveData) { return; }
            if (!IsTracked(offer)) { return; }
            _records[offer.ID] = BuildRecord(offer);
        } catch (Exception e) {
            Plugin.Log.LogWarning($"OfferArbiter.OnOfferPosted failed (fail-open): {e.Message}");
        }
    }

    // LockOffer.OnUpdate acquire Prefix — true = defer (park at Running), false = let vanilla acquire run.
    public bool ShouldDefer(Offer offer, Company? company) {
        try {
            if (!Active || offer == null || company == null || company.IsPlayer || !IsTracked(offer)) { return false; }
            var now = Now();
            if (now == DateTime.MinValue) { return false; }
            if (!_records.TryGetValue(offer.ID, out var record)) {
                record = BuildRecord(offer);
                _records[offer.ID] = record;
            }
            if (offer.CountLeft <= 0.0 || now >= record.WindowUntil) { return false; } // window closed → vanilla

            if (record.GrantedTo != null) {
                if (now < record.LeaseUntil) { return record.GrantedTo != company; } // live grant: defer everyone but grantee
                if (offer.CountLeft < record.CountLeftAtGrant) {
                    // grantee bought → re-rank on fresh CountLeft
                    record.GrantedTo = null;
                    record.Ranked = Rank(offer);
                } else {
                    // grantee declined → skip it next
                    record.Declined.Add(record.GrantedTo);
                    record.GrantedTo = null;
                }
            }

            var best = record.Ranked.FirstOrDefault(candidate => !record.Declined.Contains(candidate.C));
            if (best == null) { return false; } // nobody ranked → vanilla
            if (best.C != company) { return true; } // not this company's turn → defer
            record.GrantedTo = company; // grant
            record.LeaseUntil = now.AddDays(Math.Max(0.5, _config.GrantLeaseDays.Value));
            record.CountLeftAtGrant = offer.CountLeft;
            return false;
        } catch (Exception e) {
            Plugin.Log.LogWarning($"OfferArbiter.ShouldDefer failed (fail-open): {e.Message}");
            return false;
        }
    }

    // Offer.FullFill Prefix — true = veto (skip original) a non-grantee committing while a grant is live.
    public bool VetoCommit(Offer offer, Company? buyer) {
        try {
            if (!Active || offer == null || buyer == null || buyer.IsPlayer) { return false; }
            if (!_records.TryGetValue(offer.ID, out var record) || record.GrantedTo == null) { return false; }
            if (Now() >= record.LeaseUntil) { return false; } // stale grant → allow
            return record.GrantedTo != buyer;
        } catch (Exception e) {
            Plugin.Log.LogWarning($"OfferArbiter.VetoCommit failed (fail-open): {e.Message}");
            return false;
        }
    }

    static bool IsTracked(Offer offer) {
        var gameManager = MonoBehaviourSingleton<GameManager>.Instance;
        return offer != null && !offer.BuySell && gameManager != null && offer.Company == gameManager.Player;
    }

    static DateTime Now() {
        var timeController = MonoBehaviourSingleton<TimeController>.Instance;
        return timeController != null ? timeController.CurrentTime : DateTime.MinValue;
    }

    Record BuildRecord(Offer offer) =>
        new() {
            Ranked = Rank(offer),
            WindowUntil = Now().AddDays(Math.Max(1.0, _config.PriorityWindowDays.Value)),
        };

    // Two passes: MarketBuyOrder cohort (Tier), then MarketBuySequence sub-order within cohort.
    List<Candidate> Rank(Offer offer) {
        var order = _config.MarketBuyOrder.Value;
        var sequence = _config.MarketBuySequence.Value;
        var where = offer.WhereOffer;
        var resourceDefinition = offer.Rd;
        var gameManager = MonoBehaviourSingleton<GameManager>.Instance;
        var list = new List<Candidate>();
        if (where == null || resourceDefinition == null || gameManager?.Companies == null) { return list; }

        foreach (var company in gameManager.Companies) {
            if (company == null || company == offer.Company) { continue; }
            var companyBehaviour = company.companyBehaviour;
            if (companyBehaviour == null || !companyBehaviour.aiEnabled) { continue; }
            if (!companyBehaviour.AIConfig.takeOffersFromOtherAIs && offer.Company != gameManager.Player) { continue; }

            var deficit = _deficit.Evaluate(companyBehaviour, where, resourceDefinition);
            var tier1 = deficit.UnmetVsDemand > 0.0 || deficit.InBom; // contract-linked/transient deficit cohort
            if (order == MarketBuyOrder.ContractOnly && !tier1) { continue; } // opportunists dropped
            var tier = order == MarketBuyOrder.Vanilla ? 1 : tier1 ? 1 : 2;
            var claimCap = tier1 ? Math.Min(offer.CountLeft, deficit.UnmetVsNeed) : offer.CountLeft;

            list.Add(
                new Candidate {
                    C = company,
                    Tier = tier,
                    ClaimCap = claimCap,
                    SortKey = SortKey(sequence, companyBehaviour, where, resourceDefinition, deficit, claimCap),
                }
            );
        }
        return list.OrderBy(candidate => candidate.Tier).ThenBy(candidate => candidate.SortKey).ToList();
    }

    // Ascending SortKey = correct within-cohort order for the chosen sub-order.
    double SortKey(
        MarketBuySequence sequence,
        CompanyBehaviour companyBehaviour,
        ObjectInfo where,
        ResourceDefinition resourceDefinition,
        Deficit deficit,
        double claimCap
    ) {
        switch (sequence) {
            case MarketBuySequence.FarthestBehind: return -_standing.Trailing(companyBehaviour.Company); // largest standing gap first
            case MarketBuySequence.PriceAscending:
                return _config.PremiumOrdering.Value ? _willingness.WhatWillPay(companyBehaviour, where, resourceDefinition, claimCap) : -deficit.UnmetVsDemand;
            case MarketBuySequence.PriceDescending:
                return _config.PremiumOrdering.Value ? -_willingness.WhatWillPay(companyBehaviour, where, resourceDefinition, claimCap) : -deficit.UnmetVsDemand;
            default: return 0.0;
        }
    }

    sealed class Candidate {
        public Company C = null!;
        public double ClaimCap;
        public double SortKey;
        public int Tier;
    }

    sealed class Record {
        public readonly HashSet<Company> Declined = new();
        public double CountLeftAtGrant;
        public Company? GrantedTo;
        public DateTime LeaseUntil;
        public List<Candidate> Ranked = new();
        public DateTime WindowUntil;
    }
}
