using System;
using System.Collections.Generic;
using AI;
using Data.ScriptableObject;
using Game.ContractsObjectives;
using Game.Info;
using Game.UI.Windows.Elements.SpaceCraftConstructElements;
using Manager;
using ScriptableObjectScripts;

namespace AiPlayerIntel.Core;

enum NeedClass { ContractLinked, Transient, NeedLess }

readonly struct BomLine {
    public readonly ResourceDefinition Rd;
    public readonly double Need;

    public BomLine(ResourceDefinition resourceDefinition, double need) {
        Rd = resourceDefinition;
        Need = need;
    }
}

struct Deficit {
    public double Stock, Demand, Reservation, Available;
    public NeedClass Class;
    public bool InBom;
    public double UnmetVsDemand; // demand - available; > 0 admits (gate/rank predicate)
    public double UnmetVsNeed; // ceil(max(0, target - available)); cap/credit quantity
}

sealed class DeficitService {
    // One live pass populating both targets so no consumer re-reads the ledger.
    // Live formula only (research.md §3.1) — never a raw resourceDemandPerObject read.
    public Deficit Evaluate(CompanyBehaviour companyBehaviour, ObjectInfo where, ResourceDefinition resourceDefinition, double howMuch = 0) {
        var company = companyBehaviour != null ? companyBehaviour.Company : null;
        double stock = 0;
        if (where != null && resourceDefinition != null && company != null) {
            var row = where.GetObjectInfoData(company)?.FastGetResource(resourceDefinition);
            if (row != null) { stock = row.Value; }
        }
        var demand = companyBehaviour != null && where != null && resourceDefinition != null ? companyBehaviour.GetResourceDemandOnObject(where, resourceDefinition) : 0;
        var reservation = companyBehaviour != null && where != null && resourceDefinition != null ? companyBehaviour.GetResourceReservationOnObject(where, resourceDefinition) : 0;
        var available = Math.Max(0, stock - reservation);
        var target = howMuch > 0 ? howMuch : demand;
        var inBom = IsInActiveBom(companyBehaviour, where, resourceDefinition);
        var unmetVsDemand = demand - available;
        var needClass = unmetVsDemand > 0 ? inBom ? NeedClass.ContractLinked : NeedClass.Transient : NeedClass.NeedLess;
        return new Deficit {
            Stock = stock,
            Demand = demand,
            Reservation = reservation,
            Available = available,
            InBom = inBom,
            Class = needClass,
            UnmetVsDemand = unmetVsDemand,
            UnmetVsNeed = Math.Ceiling(Math.Max(0, target - available)),
        };
    }

    // §3.2 discriminator: does rd match a live active-objective BOM line for c at where?
    // BOM membership SURVIVES a transient teardown → admit `UnmetVsDemand>0 OR IsInActiveBom`.
    public bool IsInActiveBom(CompanyBehaviour? companyBehaviour, ObjectInfo? where, ResourceDefinition? resourceDefinition) {
        var company = companyBehaviour != null ? companyBehaviour.Company : null;
        var contractManager = MonoBehaviourSingleton<ContractManager>.Instance;
        if (company == null || where == null || resourceDefinition == null || contractManager?.allContracts == null) { return false; }
        var whereId = where.id;
        foreach (var contract in contractManager.allContracts) {
            if (contract == null || contract.ContractStateForCompany(company) != ContractManager.EContractState.Active) { continue; }
            if (!contract.PerCompanyContractData.TryGetValue(company, out var playerContractData) || playerContractData?.ObjectivesDataList == null) {
                continue;
            }
            foreach (var objectiveData in playerContractData.ObjectivesDataList) {
                if (objectiveData == null || objectiveData.IsComplete || objectiveData.Objective == null) { continue; }
                if (BomMatches(objectiveData.Objective, resourceDefinition, whereId)) { return true; }
            }
        }
        return false;
    }

    static bool BomMatches(Objective objective, ResourceDefinition resourceDefinition, int whereId) {
        if (!LocationMatches(objective, whereId)) { return false; }
        foreach (var line in Lines(objective)) {
            if (line.Rd == resourceDefinition) { return true; }
        }
        return false;
    }

    // Build types resolve their location from fromID (game: CompanyObjectiveData:779, fromID==-1 = any
    // location); Deliver resolves from toID (game: CompanyObjectiveData:318 → CheckDelivery).
    static bool LocationMatches(Objective objective, int whereId) =>
        objective.objectiveType switch {
            EObjectiveType.CreateSpaceCraft or EObjectiveType.CreateVehicle or EObjectiveType.BuildFacility
                => objective.fromID == -1 || objective.fromID == whereId,
            EObjectiveType.Deliver => objective.toID == whereId,
            _ => false,
        };

    // Creditable BOM lines (resource + outstanding quantity) for an objective; empty for the
    // non-creditable types (MakeResearch, ScheduleFly, module/crew Deliver — zap §1b).
    public IEnumerable<BomLine> CreditableLines(Objective objective) => Lines(objective);

    static IEnumerable<BomLine> Lines(Objective objective) {
        switch (objective.objectiveType) {
            case EObjectiveType.CreateSpaceCraft:
            case EObjectiveType.CreateVehicle:
            case EObjectiveType.BuildFacility:
                var price = ProductPrice(objective.productItem);
                if (price?.ListResources != null) {
                    foreach (var one in price.ListResources) {
                        if (one?.ResourceDefinition != null) {
                            yield return new BomLine(one.ResourceDefinition, one.Price * objective.howMuch);
                        }
                    }
                }
                break;
            case EObjectiveType.Deliver:
                if (objective.productItem is ResourceDefinition resourceDefinition) { yield return new BomLine(resourceDefinition, objective.howMuch); }
                break;
        }
    }

    static ResourcePrice? ProductPrice(MyIDScriptableObject? item) {
        switch (item) {
            case SpacecraftType spacecraftType: return spacecraftType.spaceCraftConstructDefault?.Price;
            case LaunchVehicleType launchVehicleType: return launchVehicleType.spaceCraftConstructDefault?.Price;
            case FacilityBaseDescriptor facilityDescriptor: return facilityDescriptor.Price;
            default: return null;
        }
    }
}
