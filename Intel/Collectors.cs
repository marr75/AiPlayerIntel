using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using AI;
using AI.Decorators;
using Cysharp.Threading.Tasks;
using Data.ScriptableObject;
using Game;
using Game.ContractsObjectives;
using Game.Info;
using Game.UI.Windows.Elements.SpaceCraftConstructElements;
using Language;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;

namespace AiPlayerIntel.Intel;

static class Collectors {
    readonly struct BomEntry {
        public readonly ResourceDefinition Rd;
        public readonly double Qty;
        public readonly ObjectInfo? Loc;
        public readonly string LocName;
        public readonly string SourceLabel;
        public BomEntry(ResourceDefinition rd, double qty, ObjectInfo? loc, string locName, string sourceLabel) {
            Rd = rd; Qty = qty; Loc = loc; LocName = locName; SourceLabel = sourceLabel;
        }
    }

    sealed class Row {
        public ResourceDefinition? Rd;
        public string LocName = "";
        public ObjectInfo? Loc;
        public bool IsBom;
        public string SourceObjective = "";
        public double PrimaryQty;
        public double Demand;
        public double Reservation;
        public double Have;
        public double? Rate;
        public double Deficit;
        public double? EtaDays;
        public ResourceState State;
        public double? PostedPrice;
        public bool? PostedIsBuy;
        public double PostedCountLeft;
        public double? DiyPerUnit;
        public double? MaxBid;
        public double PriceQty;
        public double? DiyMoneyPerUnit;
        public double? DiyTotalDays;
    }

    public static async UniTask<IntelSnapshot> Build(bool diyActive) {
        var companies = new List<CompanyIntel>();
        var rows = new List<IntelRow>();
        var gm = MonoBehaviourSingleton<GameManager>.Instance;
        if (gm == null || gm.Companies == null) {
            return new IntelSnapshot { Companies = companies, Rows = rows, BuiltAt = Time.realtimeSinceStartup, DiyActive = diyActive };
        }

        foreach (var company in gm.Companies) {
            if (company == null || company.IsPlayer) { continue; }
            var cb = company.GetComponent<CompanyBehaviour>();
            if (cb == null) { continue; }
            if (!IsParticipating(company, cb)) { continue; }

            var key = company.ID ?? company.name;
            var active = GetActiveObjective(company, cb);
            var (bomObjectives, secondary) = CollectContractDemand(company, cb, active);
            var bom = new List<BomEntry>();
            foreach (var o in bomObjectives) { bom.AddRange(BuildBom(o)); }
            companies.Add(new CompanyIntel {
                CompanyKey = key,
                CompanyName = ResolveCompanyName(company),
                CompanyIcon = company.Definition != null ? company.Definition.LogoImage : null,
                IsWorldGovernment = company.Definition != null && company.Definition.IsWorldGovernment,
                TimeValuePerDay = cb.AIConfig.costMultiplier.Time,
                CostCalcType = cb.AIConfig.costCalcType.ToString(),
                Current = BuildObjectiveLine(active),
                Others = secondary,
            });
            rows.AddRange(await BuildResources(company, cb, diyActive, bom, key));
        }

        return new IntelSnapshot { Companies = companies, Rows = rows, BuiltAt = Time.realtimeSinceStartup, DiyActive = diyActive };
    }

    static bool IsParticipating(Company company, CompanyBehaviour cb) {
        var def = company.Definition;
        return (def != null && def.IsWorldGovernment) || cb.aiEnabled;
    }

    static (Objective o, CompanyObjectiveData od, Contract running, CompanyContractData pcd)? GetActiveObjective(
        Company company, CompanyBehaviour cb) {
        var cm = MonoBehaviourSingleton<ContractManager>.Instance;
        if (cm == null || cm.allContracts == null) { return null; }

        var active = cm.allContracts
            .Where(c => c != null && c.ContractStateForCompany(company) == ContractManager.EContractState.Active)
            .ToList();
        if (active.Count == 0) { return null; }

        var head = cb.newContractsQueue.Count > 0 ? cb.newContractsQueue.Peek() : null;
        var running = active.FirstOrDefault(c => c == head) ?? active[0];

        if (!running.PerCompanyContractData.TryGetValue(company, out var pcd) || pcd == null) { return null; }
        var od = pcd.ObjectivesDataList?.FirstOrDefault(o => o != null && !o.IsComplete);
        if (od == null) { return null; }
        var o = od.Objective;
        if (o == null) { return null; }
        return (o, od, running, pcd);
    }

    static ObjectiveLine? BuildObjectiveLine((Objective o, CompanyObjectiveData od, Contract running, CompanyContractData pcd)? active) {
        return active is { } a ? BuildLine(a.running, a.pcd, a.od) : null;
    }

    static ObjectiveLine BuildLine(Contract running, CompanyContractData pcd, CompanyObjectiveData od) {
        var (segments, plain) = TokenizeObjective(od.GetText(false));
        var o = od.Objective;
        return new ObjectiveLine {
            ContractTitle = running.ContractDefinition != null ? running.ContractDefinition.TextNameMission : "",
            CurrentStepText = plain,
            Segments = segments,
            Type = o != null ? o.objectiveType.ToString() : "",
            State = pcd.currentState.ToString(),
            HowMuch = o != null ? o.howMuch : 0,
            HowMuchCurrent = od.howMuchCurrent,
        };
    }

    static (List<Objective> objectives, List<ObjectiveLine> secondary) CollectContractDemand(
        Company company, CompanyBehaviour cb,
        (Objective o, CompanyObjectiveData od, Contract running, CompanyContractData pcd)? primary) {
        var objectives = new List<Objective>();
        var secondary = new List<ObjectiveLine>();
        var cm = MonoBehaviourSingleton<ContractManager>.Instance;
        if (cm?.allContracts == null) { return (objectives, secondary); }
        var running = primary?.running;
        foreach (var c in cm.allContracts) {
            if (c == null || c.ContractStateForCompany(company) != ContractManager.EContractState.Active) { continue; }
            if (!c.PerCompanyContractData.TryGetValue(company, out var pcd) || pcd?.ObjectivesDataList == null) { continue; }
            bool firstIncomplete = true;
            foreach (var od in pcd.ObjectivesDataList) {
                if (od == null || od.IsComplete || od.Objective == null) { continue; }
                objectives.Add(od.Objective);
                if (firstIncomplete) {
                    firstIncomplete = false;
                    if (c != running) { secondary.Add(BuildLine(c, pcd, od)); }
                }
            }
        }
        return (objectives, secondary);
    }

    static readonly Regex LinkRx = new("<link=\"([^:\"]+):([^\"]+)\">(.*?)</link>", RegexOptions.Singleline);
    static readonly Regex TagRx = new("<[^>]+>");

    static (IReadOnlyList<ObjSegment>, string) TokenizeObjective(string? raw) {
        var segs = new List<ObjSegment>();
        var plain = new StringBuilder();
        if (string.IsNullOrEmpty(raw)) { return (segs, ""); }
        int pos = 0;
        foreach (Match m in LinkRx.Matches(raw)) {
            if (m.Index > pos) { AddText(segs, plain, raw!.Substring(pos, m.Index - pos)); }
            var inner = TagRx.Replace(m.Groups[3].Value, "").Trim();
            var icon = ResolveLinkIcon(m.Groups[1].Value, m.Groups[2].Value);
            if (icon != null) {
                segs.Add(new ObjSegment { Kind = ObjSegmentKind.Icon, Icon = icon, Text = inner });
                plain.Append(inner);
            } else {
                AddText(segs, plain, inner);
            }
            pos = m.Index + m.Length;
        }
        if (pos < raw!.Length) { AddText(segs, plain, raw.Substring(pos)); }
        return (segs, plain.ToString());
    }

    static void AddText(List<ObjSegment> segs, StringBuilder plain, string chunk) {
        var t = TagRx.Replace(chunk, "");
        if (t.Length == 0) { return; }
        plain.Append(t);
        if (segs.Count > 0 && segs[^1].Kind == ObjSegmentKind.Text) {
            segs[^1] = new ObjSegment { Kind = ObjSegmentKind.Text, Text = segs[^1].Text + t };
        } else {
            segs.Add(new ObjSegment { Kind = ObjSegmentKind.Text, Text = t });
        }
    }

    static Sprite? ResolveLinkIcon(string className, string rawId) {
        switch (className) {
            case "ObjectInfo":
                return int.TryParse(rawId, out var oid) ? ResolveObjectInfo(oid)?.ImagePlanetUI : null;
            case "ResourceDefinition":
                var asm = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance;
                var rd = asm?.AllResourceDefinitions?.ListNotEmpty?
                    .FirstOrDefault(d => d != null && d.ID == rawId);
                return rd != null ? rd.Sprite : null;
            default:
                return null;
        }
    }

    static IReadOnlyList<BomEntry> BuildBom(Objective? o) {
        var bom = new List<BomEntry>();
        if (o == null) { return bom; }

        var loc = ResolveObjectInfo(o.toID);
        var locName = loc != null ? loc.ObjectName : ResolveObjectName(o.toID);

        switch (o.objectiveType) {
            case EObjectiveType.CreateSpaceCraft:
            case EObjectiveType.CreateVehicle:
            case EObjectiveType.BuildFacility:
                var src = o.productItem != null ? Localize(o.productItem) : o.objectiveType.ToString();
                var price = ProductPrice(o.productItem);
                if (price?.ListResources != null) {
                    foreach (var one in price.ListResources) {
                        var rd = one.ResourceDefinition;
                        if (rd == null) { continue; }
                        bom.Add(new BomEntry(rd, one.Price * o.howMuch, loc, locName, src));
                    }
                }
                break;
            case EObjectiveType.Deliver:
                if (o.productItem is ResourceDefinition dr) {
                    bom.Add(new BomEntry(dr, o.howMuch, loc, locName, ""));
                }
                break;
        }
        return bom;
    }

    static ResourcePrice? ProductPrice(MyIDScriptableObject? item) {
        switch (item) {
            case SpacecraftType sc: return sc.spaceCraftConstructDefault?.Price;
            case LaunchVehicleType lv: return lv.spaceCraftConstructDefault?.Price;
            case FacilityBaseDescriptor f: return f.Price;
            default: return null;
        }
    }

    static async UniTask<IReadOnlyList<IntelRow>> BuildResources(
        Company company, CompanyBehaviour cb, bool diyActive, IReadOnlyList<BomEntry> bom, string companyKey) {
        var rows = new Dictionary<(ResourceDefinition rd, string loc), Row>();
        var hq = company.Definition != null ? company.mainObjectInfo : null;
        int hqId = hq != null ? hq.id : 0;

        Row Get(ResourceDefinition rd, string locName, ObjectInfo? loc) {
            var key = (rd, locName);
            if (!rows.TryGetValue(key, out var r)) {
                r = new Row { Rd = rd, LocName = locName, Loc = loc };
                rows[key] = r;
            }
            if (r.Loc == null && loc != null) { r.Loc = loc; }
            return r;
        }

        foreach (var b in bom) {
            if (b.Rd == null) { continue; }
            var r = Get(b.Rd, b.LocName, b.Loc);
            r.IsBom = true;
            r.PrimaryQty += b.Qty;
            if (b.SourceLabel.Length > 0 && !r.SourceObjective.Contains(b.SourceLabel)) {
                r.SourceObjective = r.SourceObjective.Length == 0 ? b.SourceLabel : $"{r.SourceObjective}, {b.SourceLabel}";
            }
        }

        var mom = MonoBehaviourSingleton<MarketOfferManager>.Instance;
        if (mom?.Offerts != null) {
            foreach (var o in mom.Offerts) {
                if (o == null || o.Company != company || o.Rd == null) { continue; }
                var loc = o.WhereOffer;
                var locName = loc != null ? loc.ObjectName : "";
                var r = Get(o.Rd, locName, loc);
                r.PostedPrice = o.PricePerUnit;
                r.PostedIsBuy = o.BuySell;
                r.PostedCountLeft = o.CountLeft;
            }
        }

        var demand = cb.aiStateData.resourceDemandPerObject;
        var reservation = cb.aiStateData.resourceReservationPerObject;
        if (demand != null) {
            foreach (var kv in demand) {
                var idForObj = kv.Key;
                var perRes = kv.Value;
                if (perRes == null) { continue; }
                var loc = idForObj?.Object;
                var locName = loc != null ? loc.ObjectName : ResolveObjectName(idForObj?.ID ?? 0);
                foreach (var rkv in perRes) {
                    var rd = rkv.Key;
                    if (rd == null) { continue; }
                    double dem = rkv.Value;
                    double res = 0;
                    if (reservation != null && idForObj != null
                        && reservation.TryGetValue(idForObj, out var rr) && rr != null
                        && rr.TryGetValue(rd, out var rv)) {
                        res = rv;
                    }
                    if (dem <= 0 && res <= 0) { continue; }
                    var r = Get(rd, locName, loc);
                    r.Demand = dem;
                    r.Reservation = res;
                }
            }
        }

        foreach (var r in rows.Values) {
            var (have, rate) = StockAndRate(company, r.Loc, r.Rd);
            r.Have = have;
            r.Rate = rate;
            double need = r.PrimaryQty;
            r.Deficit = Math.Max(0, need - have);
            r.EtaDays = rate is { } rt && rt > 0 && r.Deficit > 0 ? r.Deficit / rt : null;
            r.State = need > 0 && have >= need ? ResourceState.Stocked
                : r.Deficit > 0 && r.Demand > 0 ? ResourceState.Acquiring
                : r.Deficit > 0 ? ResourceState.Needed
                : ResourceState.None;
        }

        if (diyActive) {
            var mult = cb.AIConfig.takeOfferBuyUnitCostMultiplier;
            foreach (var r in rows.Values) {
                if (r.Rd == null || r.Loc == null) { continue; }
                if (!r.IsBom && !r.PostedPrice.HasValue) { continue; }
                double q = NeedQty(r);
                if (q <= 0) { continue; }
                r.PriceQty = q;
                var cost = await DiyPerUnit(cb, r.Loc, r.Rd, (float)q);
                if (cost is not { } c) { continue; }
                var perUnit = c / q;
                r.DiyPerUnit = FiniteMagnitude(perUnit);
                r.MaxBid = FiniteMagnitude((c * mult) / q);
                r.DiyMoneyPerUnit = Finite(perUnit.Money);
                r.DiyTotalDays = Finite(c.Time);
            }
        }

        return rows.Values
            .OrderByDescending(r => r.IsBom)
            .ThenByDescending(r => r.Demand + r.Reservation)
            .Select(r => new IntelRow {
                CompanyKey = companyKey,
                BodyId = r.Loc != null ? r.Loc.id : 0,
                BodyName = r.LocName,
                BodyIcon = r.Loc != null ? r.Loc.ImagePlanetUI : null,
                Body = r.Loc,
                IsHq = hqId != 0 && r.Loc != null && r.Loc.id == hqId,
                Line = new ResourceLine {
                    Resource = Localize(r.Rd),
                    Rd = r.Rd,
                    ResourceIcon = r.Rd != null ? r.Rd.Sprite : null,
                    Location = r.LocName,
                    IsBom = r.IsBom,
                    Provenance = Provenance(r),
                    PrimaryQty = r.PrimaryQty,
                    Demand = r.Demand,
                    Reservation = r.Reservation,
                    Have = r.Have,
                    Rate = r.Rate,
                    Deficit = r.Deficit,
                    EtaDays = r.EtaDays,
                    State = r.State,
                    PostedPrice = r.PostedPrice,
                    PostedIsBuy = r.PostedIsBuy,
                    PostedCountLeft = r.PostedCountLeft,
                    DiyPerUnit = r.DiyPerUnit,
                    MaxBid = r.MaxBid,
                    PriceQty = r.PriceQty,
                    DiyMoneyPerUnit = r.DiyMoneyPerUnit,
                    DiyTotalDays = r.DiyTotalDays,
                },
            })
            .ToList();
    }

    static (double have, double? rate) StockAndRate(Company company, ObjectInfo? loc, ResourceDefinition? rd) {
        if (loc == null || rd == null) { return (0, null); }
        var row = loc.GetObjectInfoData(company)?.FastGetResource(rd);
        return row != null ? (row.Value, row.Balance) : (0, null);
    }

    static double NeedQty(Row r) {
        if (r.PrimaryQty > 0) { return r.PrimaryQty; }
        if (r.Demand > 0) { return r.Demand; }
        if (r.PostedPrice.HasValue && r.PostedCountLeft > 0) { return r.PostedCountLeft; }
        return 1;
    }

    static string Provenance(Row r) {
        if (r.IsBom) { return r.SourceObjective.Length > 0 ? $"for {r.SourceObjective}" : ""; }
        if (!r.PostedPrice.HasValue && r.Demand > 0) { return "sourcing here (acquiring)"; }
        return "";
    }

    static async UniTask<CompanyCost?> DiyPerUnit(CompanyBehaviour cb, ObjectInfo? where, ResourceDefinition rd, float howMuch) {
        if (where == null || rd == null) { return null; }
        try {
            return await ObtainResourcePriorityGate.Calc(
                cb, null, null, cb.InvalidCost, CancellationToken.None,
                where, rd, howMuch, cleanCalc: true);
        } catch (Exception e) {
            Plugin.Log.LogWarning($"DIY calc failed for {Localize(rd)}: {e.Message}");
            return null;
        }
    }

    static double? FiniteMagnitude(CompanyCost? cost) {
        if (cost is not { } c) { return null; }
        return Finite(c.Magnitude);
    }

    static double? Finite(double v) => double.IsNaN(v) || double.IsInfinity(v) ? null : v;

    static string ResolveCompanyName(Company company) {
        var s = company.GetTranslationName();
        return string.IsNullOrEmpty(s) ? company.ID ?? company.name : s;
    }

    static string Localize(MyIDScriptableObject? item) {
        if (item == null) { return "?"; }
        var s = LEManager.Get(item.IDToTranslate);
        return string.IsNullOrEmpty(s) ? item.ID : s;
    }

    static ObjectInfo? ResolveObjectInfo(int id) {
        if (id <= 0) { return null; }
        var oim = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
        var all = oim != null ? oim.allObjectInfos : null;
        if (all == null) { return null; }
        foreach (var oi in all) {
            if (oi != null && oi.id == id) { return oi; }
        }
        return null;
    }

    static string ResolveObjectName(int id) {
        var oi = ResolveObjectInfo(id);
        return oi != null ? oi.ObjectName : "";
    }
}
