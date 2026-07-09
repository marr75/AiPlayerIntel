using System.Collections.Generic;
using AI;
using Data.ScriptableObject;
using Game;
using Game.ContractsObjectives;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;

namespace AiPlayerIntel.Core;

// Mod-driven heartbeat (Ensure()-created like IntelController). Ages each company's first-incomplete
// objective against the game day-clock; on a stall past StuckDays it credits the reservation-netted
// deficit for every creditable BOM line and cancels the now-moot BUY offer. No engine timestamp exists,
// so the stall timer is mod-only and save-free (reset on load; stale stamps pruned each scan).
sealed class StuckWatch : MonoBehaviour {
    static StuckWatch? _instance;
    readonly Dictionary<CompanyObjectiveData, int> _firstSeen = new();
    DeficitService _deficit = null!;
    float _accum;

    internal static void Ensure(DeficitService deficit) {
        if (_instance != null) { return; }
        var go = new GameObject(nameof(StuckWatch)) { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<StuckWatch>();
        _instance._deficit = deficit;
        Plugin.Log.LogInfo("AI Player Intel StuckWatch created.");
    }

    void Update() {
        if (!Services.Config.UnstickEnable.Value) { return; }
        _accum += Time.deltaTime;
        if (_accum < 1f) { return; }   // game days advance far slower than 1s; a 1Hz scan is ample
        _accum = 0f;
        Scan();
    }

    void Scan() {
        var gm = MonoBehaviourSingleton<GameManager>.Instance;
        var cm = MonoBehaviourSingleton<ContractManager>.Instance;
        var tc = MonoBehaviourSingleton<TimeController>.Instance;
        if (gm?.Companies == null || cm?.allContracts == null || tc == null) { return; }

        int today = tc.TotalDays;
        int stuckDays = (int)Mathf.Clamp(Services.Config.StuckDays.Value, 5f, 365f);
        var live = new HashSet<CompanyObjectiveData>();

        foreach (var company in gm.Companies) {
            if (company == null || company == gm.Player) { continue; }
            foreach (var contract in cm.allContracts) {
                if (contract == null
                    || contract.ContractStateForCompany(company) != ContractManager.EContractState.Active) { continue; }
                if (!contract.PerCompanyContractData.TryGetValue(company, out var pcd)
                    || pcd?.ObjectivesDataList == null) { continue; }
                var obj = FirstIncomplete(pcd.ObjectivesDataList);
                if (obj == null) { continue; }
                live.Add(obj);
                if (!_firstSeen.TryGetValue(obj, out int seen)) { _firstSeen[obj] = today; continue; }
                if (today - seen >= stuckDays) {
                    Zap(company, obj);
                    _firstSeen[obj] = today;   // re-arm; another full window before a second zap
                }
            }
        }
        Prune(live);
    }

    static CompanyObjectiveData? FirstIncomplete(List<CompanyObjectiveData> list) {
        foreach (var od in list) {
            if (od?.Objective == null) { continue; }
            if (!od.IsComplete) { return od; }
        }
        return null;
    }

    void Zap(Company company, CompanyObjectiveData obj) {
        var o = obj.Objective;
        if (o == null || Skipped(o.objectiveType)) { return; }
        var cb = company.companyBehaviour;
        var where = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(o.toID);
        var oid = where != null ? where.GetObjectInfoData(company) : null;
        if (cb == null || where == null || oid == null) { return; }

        foreach (var line in _deficit.CreditableLines(o)) {
            var rd = line.Rd;
            if (rd == null || line.Need <= 0 || oid.FastGetResource(rd) == null) { continue; }
            var d = _deficit.Evaluate(cb, where, rd, line.Need);
            if (d.UnmetVsNeed <= 0) { continue; }
            oid.AddResources(rd, d.UnmetVsNeed);
            CancelBuyOffer(company, where, rd);
            Plugin.Log.LogInfo($"AI Player Intel Unstick: credited {d.UnmetVsNeed} {rd.name} to "
                + $"{company.name} at {where.name} ({o.objectiveType}).");
        }
    }

    // Cancel a matching live BUY offer so the credited stock is not bought a second time (zap §3).
    static void CancelBuyOffer(Company company, ObjectInfo where, ResourceDefinition rd) {
        var mm = MonoBehaviourSingleton<MarketOfferManager>.Instance;
        if (mm?.Offerts == null) { return; }
        Offer? match = null;
        foreach (var offer in mm.Offerts) {
            if (offer is { BuySell: true } && offer.Company == company && offer.WhereOffer == where && offer.Rd == rd) {
                match = offer; break;
            }
        }
        if (match != null) { mm.CancelOffer(match); }
    }

    // These objective types have no creditable resource line, so the skip is not optional.
    static bool Skipped(EObjectiveType t) => t switch {
        EObjectiveType.MakeResearch => true,
        EObjectiveType.ScheduleFly => true,
        EObjectiveType.ScheduleFlyGravityAssist => true,
        _ => false,
    };

    void Prune(HashSet<CompanyObjectiveData> live) {
        if (_firstSeen.Count <= live.Count) { return; }
        var stale = new List<CompanyObjectiveData>();
        foreach (var k in _firstSeen.Keys) {
            if (!live.Contains(k)) { stale.Add(k); }
        }
        foreach (var k in stale) { _firstSeen.Remove(k); }
    }
}
