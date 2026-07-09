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
    sealed class Candidate {
        public Company C = null!;
        public int Tier;
        public double SortKey;
        public double ClaimCap;
    }

    sealed class Record {
        public List<Candidate> Ranked = new();
        public Company? GrantedTo;
        public DateTime LeaseUntil;
        public DateTime WindowUntil;
        public double CountLeftAtGrant;
        public readonly HashSet<Company> Declined = new();
    }

    readonly Configuration _cfg;
    readonly DeficitService _deficit;
    readonly StandingService _standing;
    readonly Willingness _willingness;
    readonly Dictionary<int, Record> _records = new();

    public OfferArbiter(Configuration cfg, DeficitService deficit, StandingService standing, Willingness willingness) {
        _cfg = cfg;
        _deficit = deficit;
        _standing = standing;
        _willingness = willingness;
    }

    bool Active => _cfg != null && _cfg.MasterEnable.Value;

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
    public bool ShouldDefer(Offer offer, Company? c) {
        try {
            if (!Active || offer == null || c == null || c.IsPlayer || !IsTracked(offer)) { return false; }
            var now = Now();
            if (now == DateTime.MinValue) { return false; }
            if (!_records.TryGetValue(offer.ID, out var r)) {
                r = BuildRecord(offer);
                _records[offer.ID] = r;
            }
            if (offer.CountLeft <= 0.0 || now >= r.WindowUntil) { return false; }   // window closed → vanilla

            if (r.GrantedTo != null) {
                if (now < r.LeaseUntil) { return r.GrantedTo != c; }   // live grant: defer everyone but grantee
                if (offer.CountLeft < r.CountLeftAtGrant) {            // grantee bought → re-rank on fresh CountLeft
                    r.GrantedTo = null;
                    r.Ranked = Rank(offer);
                } else {                                              // grantee declined → skip it next
                    r.Declined.Add(r.GrantedTo);
                    r.GrantedTo = null;
                }
            }

            var best = r.Ranked.FirstOrDefault(x => !r.Declined.Contains(x.C));
            if (best == null) { return false; }                       // nobody ranked → vanilla
            if (best.C != c) { return true; }                         // not this company's turn → defer
            r.GrantedTo = c;                                          // grant
            r.LeaseUntil = now.AddDays(Math.Max(0.5, _cfg.GrantLeaseDays.Value));
            r.CountLeftAtGrant = offer.CountLeft;
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
            if (!_records.TryGetValue(offer.ID, out var r) || r.GrantedTo == null) { return false; }
            if (Now() >= r.LeaseUntil) { return false; }              // stale grant → allow
            return r.GrantedTo != buyer;
        } catch (Exception e) {
            Plugin.Log.LogWarning($"OfferArbiter.VetoCommit failed (fail-open): {e.Message}");
            return false;
        }
    }

    static bool IsTracked(Offer offer) {
        var gm = MonoBehaviourSingleton<GameManager>.Instance;
        return offer != null && !offer.BuySell && gm != null && offer.Company == gm.Player;
    }

    static DateTime Now() {
        var tc = MonoBehaviourSingleton<TimeController>.Instance;
        return tc != null ? tc.CurrentTime : DateTime.MinValue;
    }

    Record BuildRecord(Offer offer) => new() {
        Ranked = Rank(offer),
        WindowUntil = Now().AddDays(Math.Max(1.0, _cfg.PriorityWindowDays.Value)),
    };

    // Two passes: MarketBuyOrder cohort (Tier), then MarketBuySequence sub-order within cohort.
    List<Candidate> Rank(Offer offer) {
        var order = _cfg.MarketBuyOrder.Value;
        var seq = _cfg.MarketBuySequence.Value;
        var where = offer.WhereOffer;
        var rd = offer.Rd;
        var gm = MonoBehaviourSingleton<GameManager>.Instance;
        var list = new List<Candidate>();
        if (where == null || rd == null || gm?.Companies == null) { return list; }

        foreach (var company in gm.Companies) {
            if (company == null || company == offer.Company) { continue; }
            var cb = company.companyBehaviour;
            if (cb == null || !cb.aiEnabled) { continue; }
            if (!cb.AIConfig.takeOffersFromOtherAIs && offer.Company != gm.Player) { continue; }

            var d = _deficit.Evaluate(cb, where, rd);
            bool tier1 = d.UnmetVsDemand > 0.0 || d.InBom;            // contract-linked/transient deficit cohort
            if (order == Config.MarketBuyOrder.ContractOnly && !tier1) { continue; }   // opportunists dropped
            int tier = order == Config.MarketBuyOrder.Vanilla ? 1 : (tier1 ? 1 : 2);
            double cap = tier1 ? Math.Min(offer.CountLeft, d.UnmetVsNeed) : offer.CountLeft;

            list.Add(new Candidate {
                C = company,
                Tier = tier,
                ClaimCap = cap,
                SortKey = SortKey(seq, cb, where, rd, d, cap),
            });
        }
        return list.OrderBy(x => x.Tier).ThenBy(x => x.SortKey).ToList();
    }

    // Ascending SortKey = correct within-cohort order for the chosen sub-order.
    double SortKey(MarketBuySequence seq, CompanyBehaviour cb, ObjectInfo where, ResourceDefinition rd, Deficit d, double cap) {
        switch (seq) {
            case Config.MarketBuySequence.FarthestBehind:
                return -_standing.Trailing(cb.Company);              // largest standing gap first
            case Config.MarketBuySequence.PriceAscending:
                return _cfg.PremiumOrdering.Value ? _willingness.WhatWillPay(cb, where, rd, cap) : -d.UnmetVsDemand;
            case Config.MarketBuySequence.PriceDescending:
                return _cfg.PremiumOrdering.Value ? -_willingness.WhatWillPay(cb, where, rd, cap) : -d.UnmetVsDemand;
            default:
                return 0.0;
        }
    }
}
