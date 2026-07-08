using System;
using System.Collections.Generic;
using AI;
using Data.ScriptableObject;
using Game;
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
    public BomLine(ResourceDefinition rd, double need) { Rd = rd; Need = need; }
}

struct Deficit {
    public double Stock, Demand, Reservation, Available;
    public NeedClass Class;
    public bool InBom;
    public double UnmetVsDemand;   // demand - available; > 0 admits (gate/rank predicate)
    public double UnmetVsNeed;     // ceil(max(0, target - available)); cap/credit quantity
}

sealed class DeficitService {
    // One live pass populating both targets so no consumer re-reads the ledger.
    // Live formula only (research.md §3.1) — never a raw resourceDemandPerObject read.
    public Deficit Evaluate(CompanyBehaviour cb, ObjectInfo where, ResourceDefinition rd, double howMuch = 0) {
        var company = cb != null ? cb.Company : null;
        double stock = 0;
        if (where != null && rd != null && company != null) {
            var row = where.GetObjectInfoData(company)?.FastGetResource(rd);
            if (row != null) { stock = row.Value; }
        }
        double demand = cb != null && where != null && rd != null ? cb.GetResourceDemandOnObject(where, rd) : 0;
        double reservation = cb != null && where != null && rd != null ? cb.GetResourceReservationOnObject(where, rd) : 0;
        double available = Math.Max(0, stock - reservation);
        double target = howMuch > 0 ? howMuch : demand;
        bool inBom = IsInActiveBom(cb, where, rd);
        double unmetVsDemand = demand - available;
        var cls = unmetVsDemand > 0 ? (inBom ? NeedClass.ContractLinked : NeedClass.Transient) : NeedClass.NeedLess;
        return new Deficit {
            Stock = stock,
            Demand = demand,
            Reservation = reservation,
            Available = available,
            InBom = inBom,
            Class = cls,
            UnmetVsDemand = unmetVsDemand,
            UnmetVsNeed = Math.Ceiling(Math.Max(0, target - available)),
        };
    }

    // §3.2 discriminator: does rd match a live active-objective BOM line for c at where?
    // BOM membership SURVIVES a transient teardown → admit `UnmetVsDemand>0 OR IsInActiveBom`.
    public bool IsInActiveBom(CompanyBehaviour? cb, ObjectInfo? where, ResourceDefinition? rd) {
        var company = cb != null ? cb.Company : null;
        var cm = MonoBehaviourSingleton<ContractManager>.Instance;
        if (company == null || where == null || rd == null || cm?.allContracts == null) { return false; }
        int whereId = where.id;
        foreach (var c in cm.allContracts) {
            if (c == null || c.ContractStateForCompany(company) != ContractManager.EContractState.Active) { continue; }
            if (!c.PerCompanyContractData.TryGetValue(company, out var pcd) || pcd?.ObjectivesDataList == null) { continue; }
            foreach (var od in pcd.ObjectivesDataList) {
                if (od == null || od.IsComplete || od.Objective == null) { continue; }
                if (BomMatches(od.Objective, rd, whereId)) { return true; }
            }
        }
        return false;
    }

    static bool BomMatches(Objective o, ResourceDefinition rd, int whereId) {
        if (o.toID != whereId) { return false; }
        foreach (var line in Lines(o)) {
            if (line.Rd == rd) { return true; }
        }
        return false;
    }

    // Creditable BOM lines (resource + outstanding quantity) for an objective; empty for the
    // non-creditable types (MakeResearch, ScheduleFly, module/crew Deliver — zap §1b).
    public IEnumerable<BomLine> CreditableLines(Objective o) => Lines(o);

    static IEnumerable<BomLine> Lines(Objective o) {
        switch (o.objectiveType) {
            case EObjectiveType.CreateSpaceCraft:
            case EObjectiveType.CreateVehicle:
            case EObjectiveType.BuildFacility:
                var price = ProductPrice(o.productItem);
                if (price?.ListResources != null) {
                    foreach (var one in price.ListResources) {
                        if (one?.ResourceDefinition != null) {
                            yield return new BomLine(one.ResourceDefinition, one.Price * o.howMuch);
                        }
                    }
                }
                break;
            case EObjectiveType.Deliver:
                if (o.productItem is ResourceDefinition dr) { yield return new BomLine(dr, o.howMuch); }
                break;
        }
    }

    static ResourcePrice? ProductPrice(MyIDScriptableObject? item) {
        switch (item) {
            case SpacecraftType sc: return sc.spaceCraftConstructDefault?.Price;
            case LaunchVehicleType lv: return lv.spaceCraftConstructDefault?.Price;
            case FacilityBaseDescriptor f: return f.Price;
            default: return null;
        }
    }
}
