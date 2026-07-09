using System.Collections.Generic;
using Data.ScriptableObject;
using Game;
using Game.ContractsObjectives;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;

namespace AiPlayerIntel.Core;

// Mod-driven heartbeat: ages each company's first-incomplete objective; past StuckDays it credits the
// reservation-netted deficit and cancels the moot BUY offer. Timer is mod-only, save-free (no engine timestamp).
sealed class StuckWatch : MonoBehaviour {
    static StuckWatch? _instance;
    readonly Dictionary<CompanyObjectiveData, int> _firstSeen = new();
    float _accum;
    DeficitService _deficit = null!;

    void Update() {
        if (!Services.Config.UnstickEnable.Value) { return; }
        _accum += Time.deltaTime;
        if (_accum < 1f) { return; } // game days advance far slower than 1s; a 1Hz scan is ample
        _accum = 0f;
        Scan();
    }

    internal static void Ensure(DeficitService deficit) {
        if (_instance != null) { return; }
        var gameObject = new GameObject(nameof(StuckWatch)) { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(gameObject);
        _instance = gameObject.AddComponent<StuckWatch>();
        _instance._deficit = deficit;
        Plugin.Log.LogInfo("AI Player Intel StuckWatch created.");
    }

    void Scan() {
        var gameManager = MonoBehaviourSingleton<GameManager>.Instance;
        var contractManager = MonoBehaviourSingleton<ContractManager>.Instance;
        var timeController = MonoBehaviourSingleton<TimeController>.Instance;
        if (gameManager?.Companies == null || contractManager?.allContracts == null || timeController == null) {
            return;
        }

        var today = timeController.TotalDays;
        var stuckDays = (int)Mathf.Clamp(Services.Config.StuckDays.Value, 5f, 365f);
        var live = new HashSet<CompanyObjectiveData>();

        foreach (var company in gameManager.Companies) {
            if (company == null || company == gameManager.Player) { continue; }
            foreach (var contract in contractManager.allContracts) {
                if (contract == null
                    || contract.ContractStateForCompany(company) != ContractManager.EContractState.Active) { continue; }
                if (!contract.PerCompanyContractData.TryGetValue(company, out var playerContractData)
                    || playerContractData?.ObjectivesDataList == null) { continue; }
                var objectiveData = FirstIncomplete(playerContractData.ObjectivesDataList);
                if (objectiveData == null) { continue; }
                live.Add(objectiveData);
                if (!_firstSeen.TryGetValue(objectiveData, out var seen)) {
                    _firstSeen[objectiveData] = today;
                    continue;
                }
                if (today - seen >= stuckDays) {
                    Zap(company, objectiveData);
                    _firstSeen[objectiveData] = today; // re-arm; another full window before a second zap
                }
            }
        }
        Prune(live);
    }

    static CompanyObjectiveData? FirstIncomplete(List<CompanyObjectiveData> list) {
        foreach (var objectiveData in list) {
            if (objectiveData?.Objective == null) { continue; }
            if (!objectiveData.IsComplete) { return objectiveData; }
        }
        return null;
    }

    void Zap(Company company, CompanyObjectiveData objectiveData) {
        var objective = objectiveData.Objective;
        if (objective == null || Skipped(objective.objectiveType)) { return; }
        var companyBehaviour = company.companyBehaviour;
        var where = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(objective.toID);
        var objectInfoData = where != null ? where.GetObjectInfoData(company) : null;
        if (companyBehaviour == null || where == null || objectInfoData == null) { return; }

        foreach (var line in _deficit.CreditableLines(objective)) {
            var resourceDefinition = line.Rd;
            if (resourceDefinition == null
                || line.Need <= 0
                || objectInfoData.FastGetResource(resourceDefinition) == null) { continue; }
            var deficit = _deficit.Evaluate(companyBehaviour, where, resourceDefinition, line.Need);
            if (deficit.UnmetVsNeed <= 0) { continue; }
            objectInfoData.AddResources(resourceDefinition, deficit.UnmetVsNeed);
            CancelBuyOffer(company, where, resourceDefinition);
            Plugin.Log.LogInfo(
                $"AI Player Intel Unstick: credited {deficit.UnmetVsNeed} {resourceDefinition.name} to "
                + $"{company.name} at {where.name} ({objective.objectiveType})."
            );
        }
    }

    // Cancel a matching live BUY offer so the credited stock is not bought a second time (zap §3).
    static void CancelBuyOffer(Company company, ObjectInfo where, ResourceDefinition resourceDefinition) {
        var marketOfferManager = MonoBehaviourSingleton<MarketOfferManager>.Instance;
        if (marketOfferManager?.Offerts == null) { return; }
        Offer? match = null;
        foreach (var offer in marketOfferManager.Offerts) {
            if (offer is { BuySell: true }
                && offer.Company == company
                && offer.WhereOffer == where
                && offer.Rd == resourceDefinition) {
                match = offer;
                break;
            }
        }
        if (match != null) { marketOfferManager.CancelOffer(match); }
    }

    // These objective types have no creditable resource line, so the skip is not optional.
    static bool Skipped(EObjectiveType t) =>
        t switch {
            EObjectiveType.MakeResearch => true,
            EObjectiveType.ScheduleFly => true,
            EObjectiveType.ScheduleFlyGravityAssist => true,
            _ => false,
        };

    void Prune(HashSet<CompanyObjectiveData> live) {
        if (_firstSeen.Count <= live.Count) { return; }
        var stale = new List<CompanyObjectiveData>();
        foreach (var objectiveData in _firstSeen.Keys) {
            if (!live.Contains(objectiveData)) { stale.Add(objectiveData); }
        }
        foreach (var objectiveData in stale) { _firstSeen.Remove(objectiveData); }
    }
}
