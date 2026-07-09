using System;
using System.Collections.Generic;
using System.Linq;

namespace AiPlayerIntel.Intel;

// Unity-free transform: snapshot + view state -> flat, homogeneous row list.
// filter -> group(body -> company) -> within-parent sort -> prune empties -> flatten.
static class IntelView {
    public static List<RowVm> Build(IntelSnapshot snapshot, ViewState viewState) {
        var rows = new List<RowVm>();
        var worldGovernmentKeys = new HashSet<string>(
            snapshot.Companies.Where(company => company.IsWorldGovernment).Select(company => company.CompanyKey)
        );
        var kept = snapshot.Rows
            .Where(row => TabOf(row, worldGovernmentKeys) == viewState.Tab && Matches(row, viewState.Filter))
            .ToList();
        var byBody = kept.GroupBy(row => row.BodyId)
            .OrderBy(group => group.First().BodyName, StringComparer.OrdinalIgnoreCase);

        foreach (var body in byBody) {
            var bodyKey = ViewState.BodyKey(body.Key);
            var companies = body.GroupBy(row => row.CompanyKey)
                .OrderBy(group => CompanyName(snapshot, group.Key), StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (companies.Count == 0) { continue; }
            var bodyOpen = viewState.Expanded.Contains(bodyKey);
            rows.Add(BodyHeader(body, bodyKey, bodyOpen, body.Count()));
            if (!bodyOpen) { continue; }

            foreach (var companyGroup in companies) {
                var companyRowKey = ViewState.CompanyRowKey(body.Key, companyGroup.Key);
                var companyIntel = snapshot.Companies.FirstOrDefault(company => company.CompanyKey == companyGroup.Key);
                if (companyIntel == null) { continue; }
                var companyOpen = viewState.Expanded.Contains(companyRowKey);
                rows.Add(CompanyHeader(companyIntel, companyGroup, companyRowKey, companyOpen, companyGroup.Count()));
                if (!companyOpen) { continue; }
                if (companyIntel.Current is { } current) { rows.Add(ObjectiveRow(current)); }
                var leaves = viewState.Sort == SortCol.Default
                    ? companyGroup.AsEnumerable()
                    : SortWithin(companyGroup, viewState.Sort, viewState.Desc);
                foreach (var row in leaves) { rows.Add(Leaf(row)); }
            }
        }
        if (rows.Count == 0) { rows.Add(EmptyNote(snapshot)); }
        return rows;
    }

    static Tab TabOf(IntelRow row, HashSet<string> worldGovernmentKeys) {
        var other = worldGovernmentKeys.Contains(row.CompanyKey)
            || (!row.Line.IsBom && !row.Line.PostedPrice.HasValue);
        return other ? Tab.Other : Tab.Trade;
    }

    static bool Matches(IntelRow row, string filter) {
        if (string.IsNullOrEmpty(filter)) { return true; }
        return Has(row.Line.Resource, filter) || Has(row.BodyName, filter) || Has(row.CompanyKey, filter);
    }

    static bool Has(string text, string term) => text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

    static string CompanyName(IntelSnapshot snapshot, string key) {
        var found = snapshot.Companies.FirstOrDefault(candidate => candidate.CompanyKey == key);
        return found != null ? found.CompanyName : key;
    }

    static RowVm BodyHeader(IGrouping<int, IntelRow> body, string key, bool open, int leafCount) {
        var first = body.First();
        return new RowVm {
            Kind = RowKind.BodyHeader,
            Depth = 0,
            Key = key,
            Label = first.BodyName,
            Icon = first.BodyIcon,
            Expanded = open,
            LeafCount = leafCount,
        };
    }

    static RowVm CompanyHeader(CompanyIntel companyIntel, IGrouping<string, IntelRow> companyGroup, string key, bool open, int leafCount) =>
        new() {
            Kind = RowKind.CompanyHeader,
            Depth = 1,
            Key = key,
            Label = companyIntel.CompanyName,
            Icon = companyIntel.CompanyIcon,
            IsHq = companyGroup.Any(row => row.IsHq),
            Expanded = open,
            LeafCount = leafCount,
            Objective = companyIntel.Current,
            TimeValuePerDay = companyIntel.TimeValuePerDay,
            CostCalcType = companyIntel.CostCalcType,
        };

    static RowVm ObjectiveRow(ObjectiveLine objective) =>
        new() {
            Kind = RowKind.Objective,
            Depth = 2,
            Label = objective.ContractTitle.Length > 0 ? objective.ContractTitle : objective.Type,
            Objective = objective,
        };

    static RowVm Leaf(IntelRow row) =>
        new() {
            Kind = RowKind.Leaf,
            Depth = 2,
            Detail = row,
        };

    static RowVm EmptyNote(IntelSnapshot snapshot) =>
        new() {
            Kind = RowKind.Note,
            Depth = 0,
            Label = snapshot.Companies.Count == 0
                ? "No AI companies found (not in a running game yet?)."
                : "(no standing demand)",
        };

    static IEnumerable<IntelRow> SortWithin(IEnumerable<IntelRow> rows, SortCol sort, bool desc) {
        Func<IntelRow, IComparable> key = sort switch {
            SortCol.Resource => row => row.Line.Resource,
            SortCol.Have => row => row.Line.Have,
            SortCol.Want => row => row.Line.PrimaryQty,
            SortCol.Eta => row => row.Line.EtaDays ?? double.MaxValue,
            SortCol.MaxBuy => row => row.Line.MaxBid ?? double.MinValue,
            _ => row => 0,
        };
        return desc ? rows.OrderByDescending(key) : rows.OrderBy(key);
    }
}
