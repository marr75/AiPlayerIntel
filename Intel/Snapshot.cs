using System;
using System.Collections.Generic;
using Game.Info;
using ScriptableObjectScripts;
using UnityEngine;

namespace AiPlayerIntel.Intel;

enum ResourceState { None, Stocked, Acquiring, Needed }

enum ObjSegmentKind { Text, Icon }

sealed class ObjSegment {
    public ObjSegmentKind Kind { get; init; }
    public string Text { get; init; } = "";
    public Sprite? Icon { get; init; }
}

sealed class ResourceLine {
    public string Resource { get; init; } = "";
    public ResourceDefinition? Rd { get; init; }
    public Sprite? ResourceIcon { get; init; }
    public string Location { get; init; } = "";
    public bool IsBom { get; init; }
    public string Provenance { get; init; } = "";
    public double PrimaryQty { get; init; }
    public double Demand { get; init; }
    public double Reservation { get; init; }
    public double Have { get; init; }
    public double? Rate { get; init; }
    public double Deficit { get; init; }
    public double? EtaDays { get; init; }
    public ResourceState State { get; init; }
    public double? PostedPrice { get; init; }
    public bool? PostedIsBuy { get; init; }
    public double PostedCountLeft { get; init; }
    public double? DiyPerUnit { get; init; }
    public double? MaxBid { get; init; }
    public double PriceQty { get; init; }
    public double? DiyMoneyPerUnit { get; init; }
    public double? DiyTotalDays { get; init; }
}

sealed class ObjectiveLine {
    public string ContractTitle { get; init; } = "";
    public string CurrentStepText { get; init; } = "";
    public IReadOnlyList<ObjSegment> Segments { get; init; } = Array.Empty<ObjSegment>();
    public string Type { get; init; } = "";
    public string State { get; init; } = "";
    public double HowMuch { get; init; }
    public double HowMuchCurrent { get; init; }
}

sealed class CompanyIntel {
    public string CompanyKey { get; init; } = "";
    public string CompanyName { get; init; } = "";
    public Sprite? CompanyIcon { get; init; }
    public bool IsWorldGovernment { get; init; }
    public double TimeValuePerDay { get; init; }
    public string CostCalcType { get; init; } = "";
    public ObjectiveLine? Current { get; init; }
    public IReadOnlyList<ObjectiveLine> Others { get; init; } = Array.Empty<ObjectiveLine>();
}

sealed class IntelRow {
    public string CompanyKey { get; init; } = "";
    public int BodyId { get; init; }
    public string BodyName { get; init; } = "";
    public Sprite? BodyIcon { get; init; }
    public ObjectInfo? Body { get; init; }
    public bool IsHq { get; init; }
    public ResourceLine Line { get; init; } = new();
}

sealed class IntelSnapshot {
    public IReadOnlyList<CompanyIntel> Companies { get; init; } = Array.Empty<CompanyIntel>();
    public IReadOnlyList<IntelRow> Rows { get; init; } = Array.Empty<IntelRow>();
    public float BuiltAt { get; init; }
    public bool DiyActive { get; init; }
}
