using System.Collections.Generic;
using UnityEngine;

namespace AiPlayerIntel.Intel;

enum RowKind { BodyHeader, CompanyHeader, Objective, Leaf, Note }

enum SortCol {
    Default,
    Resource,
    Have,
    Want,
    Eta,
    MaxBuy,
}

enum Tab { Trade, Other }

sealed class ViewState {
    public readonly HashSet<string> Expanded = new(); // "body:<id>", "co:<id>:<companyKey>"
    public bool Desc;
    public string Filter = ""; // matches resource/body/company name
    public SortCol Sort = SortCol.Default;
    public Tab Tab = Tab.Trade;
    public static string BodyKey(int id) => $"body:{id}";
    public static string CompanyRowKey(int id, string companyKey) => $"co:{id}:{companyKey}";
}

readonly struct RowVm {
    public RowKind Kind { get; init; }
    public int Depth { get; init; } // 0 body, 1 company, 2 leaf — drives indent spacer

    // group-header (BodyHeader / CompanyHeader / Objective / Note) — SPANNING, no grid
    public string Key { get; init; } // expand key; "" for leaves/notes
    public string Label { get; init; }
    public Sprite? Icon { get; init; } // planet icon / company logo
    public bool IsHq { get; init; }
    public bool Expanded { get; init; }
    public int LeafCount { get; init; } // header: surviving leaf count
    public ObjectiveLine? Objective { get; init; } // CompanyHeader current / Objective "also:"
    public double TimeValuePerDay { get; init; } // CompanyHeader
    public string CostCalcType { get; init; } // CompanyHeader

    // leaf — COLUMNAR, one shared Cols[] authority
    public IntelRow? Detail { get; init; } // carries ResourceLine + Body/Rd nav refs
}
