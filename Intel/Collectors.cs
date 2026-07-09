using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using AI;
using AI.Decorators;
using AiPlayerIntel.Core;
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
    static readonly Regex LinkRegex = new("<link=\"([^:\"]+):([^\"]+)\">(.*?)</link>", RegexOptions.Singleline);
    static readonly Regex TagRegex = new("<[^>]+>");

    public static async UniTask<IntelSnapshot> Build(bool diyActive) {
        var companies = new List<CompanyIntel>();
        var rows = new List<IntelRow>();
        var gameManager = MonoBehaviourSingleton<GameManager>.Instance;
        if (gameManager == null || gameManager.Companies == null) {
            return new IntelSnapshot
                { Companies = companies, Rows = rows, BuiltAt = Time.realtimeSinceStartup, DiyActive = diyActive };
        }

        foreach (var company in gameManager.Companies) {
            if (company == null || company.IsPlayer) { continue; }
            var companyBehaviour = company.GetComponent<CompanyBehaviour>();
            if (companyBehaviour == null) { continue; }
            if (!IsParticipating(company, companyBehaviour)) { continue; }

            var key = company.ID ?? company.name;
            var activeObjective = GetActiveObjective(company, companyBehaviour);
            var (bomObjectives, secondary) = CollectContractDemand(company, companyBehaviour, activeObjective);
            var headquarters = company.Definition != null ? company.mainObjectInfo : null;
            var bom = new List<BomEntry>();
            foreach (var objective in bomObjectives) { bom.AddRange(BuildBom(objective, headquarters)); }
            companies.Add(
                new CompanyIntel {
                    CompanyKey = key,
                    CompanyName = ResolveCompanyName(company),
                    CompanyIcon = company.Definition != null ? company.Definition.LogoImage : null,
                    IsWorldGovernment = company.Definition != null && company.Definition.IsWorldGovernment,
                    TimeValuePerDay = companyBehaviour.AIConfig.costMultiplier.Time,
                    CostCalcType = companyBehaviour.AIConfig.costCalcType.ToString(),
                    Current = BuildObjectiveLine(activeObjective),
                    Others = secondary,
                }
            );
            rows.AddRange(await BuildResources(company, companyBehaviour, diyActive, bom, key));
        }

        return new IntelSnapshot
            { Companies = companies, Rows = rows, BuiltAt = Time.realtimeSinceStartup, DiyActive = diyActive };
    }

    static bool IsParticipating(Company company, CompanyBehaviour companyBehaviour) {
        var definition = company.Definition;
        return (definition != null && definition.IsWorldGovernment) || companyBehaviour.aiEnabled;
    }

    static (Objective objective, CompanyObjectiveData objectiveData, Contract running, CompanyContractData companyContractData)? GetActiveObjective(
        Company company,
        CompanyBehaviour companyBehaviour
    ) {
        var contractManager = MonoBehaviourSingleton<ContractManager>.Instance;
        if (contractManager == null || contractManager.allContracts == null) { return null; }

        var activeContracts = contractManager.allContracts
            .Where(contract => contract != null && contract.ContractStateForCompany(company) == ContractManager.EContractState.Active)
            .ToList();
        if (activeContracts.Count == 0) { return null; }

        var queueHead = companyBehaviour.newContractsQueue.Count > 0 ? companyBehaviour.newContractsQueue.Peek() : null;
        var running = activeContracts.FirstOrDefault(contract => contract == queueHead) ?? activeContracts[0];

        if (!running.PerCompanyContractData.TryGetValue(company, out var companyContractData) || companyContractData == null) { return null; }
        var objectiveData = companyContractData.ObjectivesDataList?.FirstOrDefault(entry => entry != null && !entry.IsComplete);
        if (objectiveData == null) { return null; }
        var objective = objectiveData.Objective;
        if (objective == null) { return null; }
        return (objective, objectiveData, running, companyContractData);
    }

    static ObjectiveLine? BuildObjectiveLine(
        (Objective objective, CompanyObjectiveData objectiveData, Contract running, CompanyContractData companyContractData)? active
    ) =>
        active is { } activeValue ? BuildLine(activeValue.running, activeValue.companyContractData, activeValue.objectiveData) : null;

    static ObjectiveLine BuildLine(Contract running, CompanyContractData companyContractData, CompanyObjectiveData objectiveData) {
        var (segments, plain) = TokenizeObjective(objectiveData.GetText(false));
        var objective = objectiveData.Objective;
        return new ObjectiveLine {
            ContractTitle = running.ContractDefinition != null ? running.ContractDefinition.TextNameMission : "",
            CurrentStepText = plain,
            Segments = segments,
            Type = objective != null ? objective.objectiveType.ToString() : "",
            State = companyContractData.currentState.ToString(),
            HowMuch = objective != null ? objective.howMuch : 0,
            HowMuchCurrent = objectiveData.howMuchCurrent,
        };
    }

    static (List<Objective> objectives, List<ObjectiveLine> secondary) CollectContractDemand(
        Company company,
        CompanyBehaviour companyBehaviour,
        (Objective objective, CompanyObjectiveData objectiveData, Contract running, CompanyContractData companyContractData)? primary
    ) {
        var objectives = new List<Objective>();
        var secondary = new List<ObjectiveLine>();
        var contractManager = MonoBehaviourSingleton<ContractManager>.Instance;
        if (contractManager?.allContracts == null) { return (objectives, secondary); }
        var running = primary?.running;
        foreach (var contract in contractManager.allContracts) {
            if (contract == null || contract.ContractStateForCompany(company) != ContractManager.EContractState.Active) { continue; }
            if (!contract.PerCompanyContractData.TryGetValue(company, out var companyContractData) || companyContractData?.ObjectivesDataList == null) {
                continue;
            }
            var firstIncomplete = true;
            foreach (var objectiveData in companyContractData.ObjectivesDataList) {
                if (objectiveData == null || objectiveData.IsComplete || objectiveData.Objective == null) { continue; }
                objectives.Add(objectiveData.Objective);
                if (firstIncomplete) {
                    firstIncomplete = false;
                    if (contract != running) { secondary.Add(BuildLine(contract, companyContractData, objectiveData)); }
                }
            }
        }
        return (objectives, secondary);
    }

    static (IReadOnlyList<ObjSegment>, string) TokenizeObjective(string? raw) {
        var segments = new List<ObjSegment>();
        var plain = new StringBuilder();
        if (string.IsNullOrEmpty(raw)) { return (segments, ""); }
        var position = 0;
        foreach (Match match in LinkRegex.Matches(raw)) {
            if (match.Index > position) { AddText(segments, plain, raw!.Substring(position, match.Index - position)); }
            var inner = TagRegex.Replace(match.Groups[3].Value, "").Trim();
            var icon = ResolveLinkIcon(match.Groups[1].Value, match.Groups[2].Value);
            if (icon != null) {
                segments.Add(new ObjSegment { Kind = ObjSegmentKind.Icon, Icon = icon, Text = inner });
                plain.Append(inner);
            } else {
                AddText(segments, plain, inner);
            }
            position = match.Index + match.Length;
        }
        if (position < raw!.Length) { AddText(segments, plain, raw.Substring(position)); }
        return (segments, plain.ToString());
    }

    static void AddText(List<ObjSegment> segments, StringBuilder plain, string chunk) {
        var text = TagRegex.Replace(chunk, "");
        if (text.Length == 0) { return; }
        plain.Append(text);
        if (segments.Count > 0 && segments[^1].Kind == ObjSegmentKind.Text) {
            segments[^1] = new ObjSegment { Kind = ObjSegmentKind.Text, Text = segments[^1].Text + text };
        } else {
            segments.Add(new ObjSegment { Kind = ObjSegmentKind.Text, Text = text });
        }
    }

    static Sprite? ResolveLinkIcon(string className, string rawId) {
        switch (className) {
            case "ObjectInfo": return int.TryParse(rawId, out var objectId) ? ResolveObjectInfo(objectId)?.ImagePlanetUI : null;
            case "ResourceDefinition":
                var assembly = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance;
                var resourceDefinition = assembly?.AllResourceDefinitions?.ListNotEmpty?
                    .FirstOrDefault(candidate => candidate != null && candidate.ID == rawId);
                return resourceDefinition != null ? resourceDefinition.Sprite : null;
            default: return null;
        }
    }

    static IReadOnlyList<BomEntry> BuildBom(Objective? objective, ObjectInfo? headquarters) {
        var bom = new List<BomEntry>();
        if (objective == null) { return bom; }

        // Build types resolve their location from fromID (game: CompanyObjectiveData:777, fromID==-1 =
        // any location, shown against HQ here); Deliver resolves from toID (CompanyObjectiveData:862).
        var isBuild = objective.objectiveType is EObjectiveType.CreateSpaceCraft
            or EObjectiveType.CreateVehicle
            or EObjectiveType.BuildFacility;
        var id = isBuild ? objective.fromID : objective.toID;
        var location = id > 0 ? ResolveObjectInfo(id) : headquarters;
        var locationName = location != null ? location.ObjectName : ResolveObjectName(id);

        switch (objective.objectiveType) {
            case EObjectiveType.CreateSpaceCraft:
            case EObjectiveType.CreateVehicle:
            case EObjectiveType.BuildFacility:
                var source = objective.productItem != null ? Localize(objective.productItem) : objective.objectiveType.ToString();
                var price = ProductPrice(objective.productItem);
                if (price?.ListResources != null) {
                    foreach (var resourceCost in price.ListResources) {
                        var resourceDefinition = resourceCost.ResourceDefinition;
                        if (resourceDefinition == null) { continue; }
                        bom.Add(new BomEntry(resourceDefinition, resourceCost.Price * objective.howMuch, location, locationName, source));
                    }
                }
                break;
            case EObjectiveType.Deliver:
                if (objective.productItem is ResourceDefinition deliverResource) {
                    bom.Add(new BomEntry(deliverResource, objective.howMuch, location, locationName, ""));
                }
                break;
        }
        return bom;
    }

    static ResourcePrice? ProductPrice(MyIDScriptableObject? item) {
        switch (item) {
            case SpacecraftType spacecraft: return spacecraft.spaceCraftConstructDefault?.Price;
            case LaunchVehicleType launchVehicle: return launchVehicle.spaceCraftConstructDefault?.Price;
            case FacilityBaseDescriptor facility: return facility.Price;
            default: return null;
        }
    }

    static async UniTask<IReadOnlyList<IntelRow>> BuildResources(
        Company company,
        CompanyBehaviour companyBehaviour,
        bool diyActive,
        IReadOnlyList<BomEntry> bom,
        string companyKey
    ) {
        var rows = new Dictionary<(ResourceDefinition resourceDefinition, string locationName), Row>();
        var headquarters = company.Definition != null ? company.mainObjectInfo : null;
        var headquartersId = headquarters != null ? headquarters.id : 0;

        Row Get(ResourceDefinition resourceDefinition, string locationName, ObjectInfo? location) {
            var key = (resourceDefinition, locationName);
            if (!rows.TryGetValue(key, out var row)) {
                row = new Row { ResourceDefinition = resourceDefinition, LocationName = locationName, Location = location };
                rows[key] = row;
            }
            if (row.Location == null && location != null) { row.Location = location; }
            return row;
        }

        foreach (var entry in bom) {
            if (entry.ResourceDefinition == null) { continue; }
            var row = Get(entry.ResourceDefinition, entry.LocationName, entry.Location);
            row.IsBom = true;
            row.PrimaryQty += entry.Quantity;
            if (entry.SourceLabel.Length > 0 && !row.SourceObjective.Contains(entry.SourceLabel)) {
                row.SourceObjective = row.SourceObjective.Length == 0
                    ? entry.SourceLabel
                    : $"{row.SourceObjective}, {entry.SourceLabel}";
            }
        }

        var marketOfferManager = MonoBehaviourSingleton<MarketOfferManager>.Instance;
        if (marketOfferManager?.Offerts != null) {
            foreach (var offer in marketOfferManager.Offerts) {
                if (offer == null || offer.Company != company || offer.Rd == null) { continue; }
                var location = offer.WhereOffer;
                var locationName = location != null ? location.ObjectName : "";
                var row = Get(offer.Rd, locationName, location);
                row.PostedPrice = offer.PricePerUnit;
                row.PostedIsBuy = offer.BuySell;
                row.PostedCountLeft = offer.CountLeft;
            }
        }

        var demand = companyBehaviour.aiStateData.resourceDemandPerObject;
        var reservation = companyBehaviour.aiStateData.resourceReservationPerObject;
        if (demand != null) {
            foreach (var demandEntry in demand) {
                var objectKey = demandEntry.Key;
                var demandPerResource = demandEntry.Value;
                if (demandPerResource == null) { continue; }
                var location = objectKey?.Object;
                var locationName = location != null ? location.ObjectName : ResolveObjectName(objectKey?.ID ?? 0);
                foreach (var resourceDemand in demandPerResource) {
                    var resourceDefinition = resourceDemand.Key;
                    if (resourceDefinition == null) { continue; }
                    var demandValue = resourceDemand.Value;
                    double reservationAmount = 0;
                    if (reservation != null
                        && objectKey != null
                        && reservation.TryGetValue(objectKey, out var reservationForObject)
                        && reservationForObject != null
                        && reservationForObject.TryGetValue(resourceDefinition, out var reservationValue)) {
                        reservationAmount = reservationValue;
                    }
                    if (demandValue <= 0 && reservationAmount <= 0) { continue; }
                    var row = Get(resourceDefinition, locationName, location);
                    row.Demand = demandValue;
                    row.Reservation = reservationAmount;
                }
            }
        }

        foreach (var row in rows.Values) {
            var (have, rate) = StockAndRate(company, row.Location, row.ResourceDefinition);
            row.Have = have;
            row.Rate = rate;
            var need = row.PrimaryQty;
            row.Deficit = Math.Max(0, need - have);
            row.EtaDays = rate is { } rateValue && rateValue > 0 && row.Deficit > 0 ? row.Deficit / rateValue : null;
            row.State = need > 0 && have >= need ? ResourceState.Stocked
                : row.Deficit > 0 && row.Demand > 0 ? ResourceState.Acquiring
                : row.Deficit > 0 ? ResourceState.Needed
                : ResourceState.None;
        }

        if (diyActive) {
            var costMultiplier = companyBehaviour.AIConfig.takeOfferBuyUnitCostMultiplier;
            foreach (var row in rows.Values) {
                if (row.ResourceDefinition == null || row.Location == null) { continue; }
                if (!Services.Config.ShowAllMarkets.Value && !row.IsBom && !row.PostedPrice.HasValue) { continue; }
                var quantity = NeedQty(row);
                if (quantity <= 0) { continue; }
                row.PriceQty = quantity;
                var cost = await DiyPerUnit(companyBehaviour, row.Location, row.ResourceDefinition, (float)quantity);
                if (cost is not { } costValue) { continue; }
                var perUnit = costValue / quantity;
                row.DiyPerUnit = FiniteMagnitude(perUnit);
                row.MaxBid = FiniteMagnitude(costValue * costMultiplier / quantity);
                row.DiyMoneyPerUnit = Finite(perUnit.Money);
                row.DiyTotalDays = Finite(costValue.Time);
            }
        }

        return rows.Values
            .OrderByDescending(row => row.IsBom)
            .ThenByDescending(row => row.Demand + row.Reservation)
            .Select(row => new IntelRow {
                    CompanyKey = companyKey,
                    BodyId = row.Location != null ? row.Location.id : 0,
                    BodyName = row.LocationName,
                    BodyIcon = row.Location != null ? row.Location.ImagePlanetUI : null,
                    Body = row.Location,
                    IsHq = headquartersId != 0 && row.Location != null && row.Location.id == headquartersId,
                    Line = new ResourceLine {
                        Resource = Localize(row.ResourceDefinition),
                        Rd = row.ResourceDefinition,
                        ResourceIcon = row.ResourceDefinition != null ? row.ResourceDefinition.Sprite : null,
                        Location = row.LocationName,
                        IsBom = row.IsBom,
                        Provenance = Provenance(row),
                        PrimaryQty = row.PrimaryQty,
                        Demand = row.Demand,
                        Reservation = row.Reservation,
                        Have = row.Have,
                        Rate = row.Rate,
                        Deficit = row.Deficit,
                        EtaDays = row.EtaDays,
                        State = row.State,
                        PostedPrice = row.PostedPrice,
                        PostedIsBuy = row.PostedIsBuy,
                        PostedCountLeft = row.PostedCountLeft,
                        DiyPerUnit = row.DiyPerUnit,
                        MaxBid = row.MaxBid,
                        PriceQty = row.PriceQty,
                        DiyMoneyPerUnit = row.DiyMoneyPerUnit,
                        DiyTotalDays = row.DiyTotalDays,
                    },
                }
            )
            .ToList();
    }

    static (double have, double? rate) StockAndRate(Company company, ObjectInfo? location, ResourceDefinition? resourceDefinition) {
        if (location == null || resourceDefinition == null) { return (0, null); }
        var row = location.GetObjectInfoData(company)?.FastGetResource(resourceDefinition);
        return row != null ? (row.Value, row.Balance) : (0, null);
    }

    static double NeedQty(Row row) {
        if (row.PrimaryQty > 0) { return row.PrimaryQty; }
        if (row.Demand > 0) { return row.Demand; }
        if (row.PostedPrice.HasValue && row.PostedCountLeft > 0) { return row.PostedCountLeft; }
        return 1;
    }

    static string Provenance(Row row) {
        if (row.IsBom) { return row.SourceObjective.Length > 0 ? $"for {row.SourceObjective}" : ""; }
        if (!row.PostedPrice.HasValue && row.Demand > 0) { return "sourcing here (acquiring)"; }
        return "";
    }

    static async UniTask<CompanyCost?> DiyPerUnit(
        CompanyBehaviour companyBehaviour,
        ObjectInfo? where,
        ResourceDefinition resourceDefinition,
        float howMuch
    ) {
        if (where == null || resourceDefinition == null) { return null; }
        try {
            return await ObtainResourcePriorityGate.Calc(
                companyBehaviour,
                null,
                null,
                companyBehaviour.InvalidCost,
                CancellationToken.None,
                where,
                resourceDefinition,
                howMuch,
                true
            );
        } catch (Exception exception) {
            Plugin.Log.LogWarning($"DIY calc failed for {Localize(resourceDefinition)}: {exception.Message}");
            return null;
        }
    }

    static double? FiniteMagnitude(CompanyCost? cost) {
        if (cost is not { } costValue) { return null; }
        return Finite(costValue.Magnitude);
    }

    static double? Finite(double value) => double.IsNaN(value) || double.IsInfinity(value) ? null : value;

    static string ResolveCompanyName(Company company) {
        var translatedName = company.GetTranslationName();
        return string.IsNullOrEmpty(translatedName) ? company.ID ?? company.name : translatedName;
    }

    static string Localize(MyIDScriptableObject? item) {
        if (item == null) { return "?"; }
        var localized = LEManager.Get(item.IDToTranslate);
        return string.IsNullOrEmpty(localized) ? item.ID : localized;
    }

    static ObjectInfo? ResolveObjectInfo(int id) {
        if (id <= 0) { return null; }
        var objectInfoManager = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
        var all = objectInfoManager != null ? objectInfoManager.allObjectInfos : null;
        if (all == null) { return null; }
        foreach (var objectInfo in all) {
            if (objectInfo != null && objectInfo.id == id) { return objectInfo; }
        }
        return null;
    }

    static string ResolveObjectName(int id) {
        var objectInfo = ResolveObjectInfo(id);
        return objectInfo != null ? objectInfo.ObjectName : "";
    }

    readonly struct BomEntry {
        public readonly ResourceDefinition ResourceDefinition;
        public readonly double Quantity;
        public readonly ObjectInfo? Location;
        public readonly string LocationName;
        public readonly string SourceLabel;

        public BomEntry(ResourceDefinition resourceDefinition, double quantity, ObjectInfo? location, string locationName, string sourceLabel) {
            ResourceDefinition = resourceDefinition;
            Quantity = quantity;
            Location = location;
            LocationName = locationName;
            SourceLabel = sourceLabel;
        }
    }

    sealed class Row {
        public double Deficit;
        public double Demand;
        public double? DiyMoneyPerUnit;
        public double? DiyPerUnit;
        public double? DiyTotalDays;
        public double? EtaDays;
        public double Have;
        public bool IsBom;
        public ObjectInfo? Location;
        public string LocationName = "";
        public double? MaxBid;
        public double PostedCountLeft;
        public bool? PostedIsBuy;
        public double? PostedPrice;
        public double PriceQty;
        public double PrimaryQty;
        public double? Rate;
        public ResourceDefinition? ResourceDefinition;
        public double Reservation;
        public string SourceObjective = "";
        public ResourceState State;
    }
}
