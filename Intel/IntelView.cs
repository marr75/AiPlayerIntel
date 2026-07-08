using System;
using System.Collections.Generic;
using System.Linq;

namespace AiPlayerIntel.Intel;

// Unity-free transform: snapshot + view state -> flat, homogeneous row list.
// filter -> group(body -> company) -> within-parent sort -> prune empties -> flatten.
static class IntelView {
    public static List<RowVm> Build(IntelSnapshot snap, ViewState st) {
        var rows = new List<RowVm>();
        var worldGov = new HashSet<string>(
            snap.Companies.Where(c => c.IsWorldGovernment).Select(c => c.CompanyKey));
        var kept = snap.Rows
            .Where(r => TabOf(r, worldGov) == st.Tab && Matches(r, st.Filter))
            .ToList();
        var byBody = kept.GroupBy(r => r.BodyId)
                         .OrderBy(g => g.First().BodyName, StringComparer.OrdinalIgnoreCase);

        foreach (var body in byBody) {
            var bodyKey = ViewState.BodyKey(body.Key);
            var companies = body.GroupBy(r => r.CompanyKey)
                                .OrderBy(g => CompanyName(snap, g.Key), StringComparer.OrdinalIgnoreCase)
                                .ToList();
            if (companies.Count == 0) { continue; }
            bool bodyOpen = st.Expanded.Contains(bodyKey);
            rows.Add(BodyHeader(body, bodyKey, bodyOpen, body.Count()));
            if (!bodyOpen) { continue; }

            foreach (var co in companies) {
                var coKey = ViewState.CoKey(body.Key, co.Key);
                var ci = snap.Companies.FirstOrDefault(c => c.CompanyKey == co.Key);
                if (ci == null) { continue; }
                bool coOpen = st.Expanded.Contains(coKey);
                rows.Add(CompanyHeader(ci, co, coKey, coOpen, co.Count()));
                if (!coOpen) { continue; }
                if (ci.Current is { } cur) { rows.Add(ObjectiveRow(cur)); }
                var leaves = st.Sort == SortCol.Default
                    ? co.AsEnumerable()
                    : SortWithin(co, st.Sort, st.Desc);
                foreach (var r in leaves) { rows.Add(Leaf(r)); }
            }
        }
        if (rows.Count == 0) { rows.Add(EmptyNote(snap)); }
        return rows;
    }

    static Tab TabOf(IntelRow r, HashSet<string> worldGov) {
        bool other = worldGov.Contains(r.CompanyKey)
            || (!r.Line.IsBom && !r.Line.PostedPrice.HasValue);
        return other ? Tab.Other : Tab.Trade;
    }

    static bool Matches(IntelRow r, string filter) {
        if (string.IsNullOrEmpty(filter)) { return true; }
        return Has(r.Line.Resource, filter) || Has(r.BodyName, filter) || Has(r.CompanyKey, filter);
    }

    static bool Has(string s, string term) => s.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

    static string CompanyName(IntelSnapshot snap, string key) {
        var c = snap.Companies.FirstOrDefault(x => x.CompanyKey == key);
        return c != null ? c.CompanyName : key;
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

    static RowVm CompanyHeader(CompanyIntel ci, IGrouping<string, IntelRow> co, string key, bool open, int leafCount) => new() {
        Kind = RowKind.CompanyHeader,
        Depth = 1,
        Key = key,
        Label = ci.CompanyName,
        Icon = ci.CompanyIcon,
        IsHq = co.Any(r => r.IsHq),
        Expanded = open,
        LeafCount = leafCount,
        Objective = ci.Current,
        TimeValuePerDay = ci.TimeValuePerDay,
        CostCalcType = ci.CostCalcType,
    };

    static RowVm ObjectiveRow(ObjectiveLine o) => new() {
        Kind = RowKind.Objective,
        Depth = 2,
        Label = o.ContractTitle.Length > 0 ? o.ContractTitle : o.Type,
        Objective = o,
    };

    static RowVm Leaf(IntelRow r) => new() {
        Kind = RowKind.Leaf,
        Depth = 2,
        Detail = r,
    };

    static RowVm EmptyNote(IntelSnapshot snap) => new() {
        Kind = RowKind.Note,
        Depth = 0,
        Label = snap.Companies.Count == 0
            ? "No AI companies found (not in a running game yet?)."
            : "(no standing demand)",
    };

    static IEnumerable<IntelRow> SortWithin(IEnumerable<IntelRow> co, SortCol sort, bool desc) {
        Func<IntelRow, IComparable> key = sort switch {
            SortCol.Resource => r => r.Line.Resource,
            SortCol.Have => r => r.Line.Have,
            SortCol.Want => r => r.Line.PrimaryQty,
            SortCol.Eta => r => r.Line.EtaDays ?? double.MaxValue,
            SortCol.MaxBuy => r => r.Line.MaxBid ?? double.MinValue,
            _ => r => 0,
        };
        return desc ? co.OrderByDescending(key) : co.OrderBy(key);
    }
}
