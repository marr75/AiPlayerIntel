using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using AiPlayerIntel.Intel;
using Manager;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AiPlayerIntel.Ui;

// Game-native UGUI panel: dumb emitter over IntelView output. Clone-and-gut the notification
// history donor, then on every change source compute List<RowVm>, string-signature diff, and
// teardown/re-emit column header + spanning/leaf rows through one shared Cols[] width authority.
sealed class IntelPanel : MonoBehaviour {
    internal const float TitleH = 24f;
    internal const float TabH = 20f;
    internal const float FilterH = 22f;

    // indent fixed; data columns flex by ratio so they scale with window resize.
    // resource 3, max buy 3, have 1, want 1, eta 2 — header and leaves share this authority.
    static readonly (float width, float flexWidth)[] Cols = {
        (16, 0), (0, 3), (0, 3), (0, 1), (0, 1), (0, 2),
    };

    static readonly FieldInfo? ShowField =
        typeof(NotificationManager).GetField("showNotificationHistory", BindingFlags.Instance | BindingFlags.NonPublic);

    static readonly FieldInfo? HistoryField =
        typeof(NotificationManager).GetField("notificationHistory", BindingFlags.Instance | BindingFlags.NonPublic);

    static IntelPanel? _instance;
    RectTransform _content = null!;

    IntelController _controller = null!;
    TMP_FontAsset? _font;
    bool _force;

    string _lastSignature = "";
    IntelPanelWidgets.TabButton _otherTab;
    RectTransform _panelRectTransform = null!;
    bool _pending;
    float _pointerHeldUntil;
    ScrollRect? _scroll;
    RectTransform? _showButtonRectTransform;
    IntelPanelWidgets.TabButton _tradeTab;
    IntelPanelWidgets _widgets = null!;

    internal ViewState State => _controller.State;

    void Update() {
        if (AnyPointerHeld()) { _pointerHeldUntil = Time.unscaledTime + 0.25f; }
        if (_pending && Time.unscaledTime >= _pointerHeldUntil) { Rebuild(); }
    }

    void OnEnable() {
        if (_controller == null) { return; }
        _force = _pending = true;
        _controller.ForceRefresh();
        if (_scroll != null) { _scroll.verticalNormalizedPosition = 1f; }
    }

    void OnDestroy() {
        if (_controller != null) { _controller.Changed -= OnChanged; }
    }

    internal static void Inject(NotificationManager notificationManager) {
        try {
            if (ShowField?.GetValue(notificationManager) is not Button showButton) {
                Plugin.Log.LogWarning("AI Player Intel: notification button not found; UGUI panel skipped.");
                return;
            }
            if (HistoryField?.GetValue(notificationManager) is not GameObject historyGameObject) {
                Plugin.Log.LogWarning("AI Player Intel: notification history not found; UGUI panel skipped.");
                return;
            }
            var canvas = showButton.GetComponentInParent<Canvas>();
            if (canvas == null) {
                Plugin.Log.LogWarning("AI Player Intel: HUD canvas not found; UGUI panel skipped.");
                return;
            }

            var font = IntelPanelWidgets.SampleFont(historyGameObject);
            var panelGameObject = Instantiate(historyGameObject, canvas.transform);
            panelGameObject.name = "aiPlayerIntelPanel";
            panelGameObject.transform.SetAsLastSibling();
            var rectTransform = (RectTransform)panelGameObject.transform;
            rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0f, 1f);
            rectTransform.sizeDelta = PanelLayout.Size;
            rectTransform.anchoredPosition = new Vector2(-9999f, -9999f);
            (panelGameObject.GetComponent<LayoutElement>() ?? panelGameObject.AddComponent<LayoutElement>()).ignoreLayout = true;

            var theme = IntelPanelWidgets.SampleBackground(panelGameObject);
            for (var i = rectTransform.childCount - 1; i >= 0; i--) { Destroy(rectTransform.GetChild(i).gameObject); }
            foreach (var canvasGroup in panelGameObject.GetComponents<CanvasGroup>()) { canvasGroup.interactable = canvasGroup.blocksRaycasts = true; }
            foreach (var scrollRect in panelGameObject.GetComponents<ScrollRect>()) { DestroyImmediate(scrollRect); }
            foreach (var layoutGroup in panelGameObject.GetComponents<LayoutGroup>()) { DestroyImmediate(layoutGroup); }
            if (panelGameObject.GetComponent<ContentSizeFitter>() is { } sizeFitter) { DestroyImmediate(sizeFitter); }

            var content = IntelPanelWidgets.BuildScrollView(panelGameObject, theme, TitleH + TabH + FilterH + 8f);
            panelGameObject.SetActive(false);
            var panel = panelGameObject.AddComponent<IntelPanel>();
            panel.Init(showButton, content, font);
            _instance = panel;
            Plugin.Log.LogInfo("AI Player Intel UGUI panel injected.");
        } catch (Exception exception) {
            Plugin.Log.LogWarning($"AI Player Intel: UGUI panel injection failed: {exception}");
        }
    }

    void Init(Button showButton, RectTransform content, TMP_FontAsset? font) {
        _panelRectTransform = (RectTransform)transform;
        _showButtonRectTransform = showButton.GetComponent<RectTransform>();
        _content = content;
        _font = font;
        _scroll = GetComponent<ScrollRect>();
        _widgets = new IntelPanelWidgets(font);
        _controller = IntelController.Instance!;
        BuildChrome();
        _controller.Changed += OnChanged;
    }

    internal static void Toggle() {
        if (_instance == null) { return; }
        var gameObject = _instance.gameObject;
        gameObject.SetActive(!gameObject.activeSelf);
        if (gameObject.activeSelf) { gameObject.transform.SetAsLastSibling(); }
    }

    internal static bool HandleEscape() {
        if (_instance == null || !_instance.gameObject.activeSelf) { return false; }
        _instance.gameObject.SetActive(false);
        return true;
    }

    void OnChanged() => _pending = true;

    internal void CycleSort(SortCol column) {
        var state = State;
        if (state.Sort != column) {
            state.Sort = column;
            state.Desc = false;
        } else if (!state.Desc) { state.Desc = true; } else {
            state.Sort = SortCol.Default;
            state.Desc = false;
        }
        _force = _pending = true;
    }

    void SetTab(Tab tab) {
        if (State.Tab == tab) { return; }
        State.Tab = tab;
        StyleTabs();
        _force = _pending = true;
    }

    void StyleTabs() {
        IntelPanelWidgets.StyleTab(_tradeTab, State.Tab == Tab.Trade);
        IntelPanelWidgets.StyleTab(_otherTab, State.Tab == Tab.Other);
    }

    void ToggleExpand(string key) {
        var expanded = State.Expanded;
        if (!expanded.Remove(key)) { expanded.Add(key); }
        _force = _pending = true;
    }

    void OnFilterChanged(string value) {
        State.Filter = value ?? "";
        _force = _pending = true;
    }

    void Rebuild() {
        _pending = false;
        var rows = IntelView.Build(_controller.Current, State);
        var signature = Signature(rows);
        if (!_force && signature == _lastSignature) { return; }
        _force = false;
        _lastSignature = signature;
        for (var i = _content.childCount - 1; i >= 0; i--) { Destroy(_content.GetChild(i).gameObject); }
        Emit(rows);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
    }

    void Emit(List<RowVm> rows) {
        EmitColumnHeader();
        foreach (var rowViewModel in rows) {
            switch (rowViewModel.Kind) {
                case RowKind.BodyHeader:
                case RowKind.CompanyHeader:
                    var key = rowViewModel.Key;
                    _widgets.AddSpanRow(_content, rowViewModel, () => ToggleExpand(key));
                    break;
                case RowKind.Objective:
                case RowKind.Note:
                    _widgets.AddSpanRow(_content, rowViewModel, null);
                    break;
                case RowKind.Leaf: EmitLeaf(rowViewModel.Detail!); break;
            }
        }
    }

    void EmitColumnHeader() {
        var headerRow = _widgets.MakeRow(_content, 18f);
        _widgets.AddColumn(headerRow, Cols[0], "");
        _widgets.AddHeaderCell(headerRow, Cols[1], "Resource", SortCol.Resource, this);
        _widgets.AddHeaderCell(headerRow, Cols[2], "Max buy", SortCol.MaxBuy, this);
        _widgets.AddHeaderCell(headerRow, Cols[3], "Have", SortCol.Have, this);
        _widgets.AddHeaderCell(headerRow, Cols[4], "Want", SortCol.Want, this);
        _widgets.AddHeaderCell(headerRow, Cols[5], "Rate", SortCol.Eta, this);
    }

    void EmitLeaf(IntelRow intelRow) {
        var row = _widgets.MakeRow(_content, 18f);
        _widgets.AddColumn(row, Cols[0], "");
        _widgets.AddColumn(row, Cols[1], intelRow.Line.Resource, intelRow.Line.ResourceIcon);
        _widgets.AddColumn(row, Cols[2], IntelFormat.MaxBuy(intelRow.Line));
        _widgets.AddColumn(row, Cols[3], IntelFormat.Have(intelRow.Line));
        _widgets.AddColumn(row, Cols[4], IntelFormat.Want(intelRow.Line));
        _widgets.AddColumn(row, Cols[5], IntelFormat.RateEta(intelRow.Line));
        if (intelRow.Line.MaxBid is { } bid && intelRow.Line.Rd is { } resourceDefinition && intelRow.Body is { } body) {
            IntelPanelWidgets.MakeClickable(row, () => IntelActions.OpenOffer(body, resourceDefinition, bid, intelRow.Line.PriceQty));
        } else {
            IntelPanelWidgets.MakeClickable(row, () => IntelActions.OpenMarket(intelRow.Body));
        }
    }

    string Signature(List<RowVm> rows) {
        var builder = new StringBuilder(1024);
        var state = State;
        builder.Append((int)state.Sort)
            .Append(state.Desc ? 'D' : 'A')
            .Append('|')
            .Append((int)state.Tab)
            .Append('|')
            .Append(state.Filter)
            .Append('\n');
        foreach (var rowViewModel in rows) {
            builder.Append((int)rowViewModel.Kind).Append(rowViewModel.Depth).Append(rowViewModel.Expanded ? '+' : '-').Append(rowViewModel.LeafCount).Append('|');
            if (rowViewModel.Kind == RowKind.Leaf && rowViewModel.Detail is { } intelRow) {
                var line = intelRow.Line;
                builder.Append(line.Resource)
                    .Append('|')
                    .Append(IntelFormat.MaxBuy(line))
                    .Append('|')
                    .Append(IntelFormat.Have(line))
                    .Append('|')
                    .Append(IntelFormat.Want(line))
                    .Append('|')
                    .Append(IntelFormat.RateEta(line))
                    .Append('|')
                    .Append(line.ResourceIcon != null ? line.ResourceIcon.name : "");
            } else {
                builder.Append(rowViewModel.Label).Append('|').Append(rowViewModel.IsHq ? 'H' : '-');
                if (rowViewModel.Kind == RowKind.CompanyHeader) {
                    builder.Append(rowViewModel.TimeValuePerDay.ToString("0.##")).Append(rowViewModel.CostCalcType);
                }
                if (rowViewModel.Objective is { } objective) { builder.Append(objective.ContractTitle).Append(objective.CurrentStepText).Append(objective.Type); }
            }
            builder.Append('\n');
        }
        return builder.ToString();
    }

    static bool AnyPointerHeld() => Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);

    // ---- persistent chrome: title bar (drag + buttons), filter bar, resize handle ----

    void BuildChrome() {
        var title = new GameObject("TitleBar", typeof(RectTransform));
        title.transform.SetParent(transform, false);
        var titleRectTransform = title.GetComponent<RectTransform>();
        titleRectTransform.anchorMin = new Vector2(0f, 1f);
        titleRectTransform.anchorMax = new Vector2(1f, 1f);
        titleRectTransform.pivot = new Vector2(0.5f, 1f);
        titleRectTransform.sizeDelta = new Vector2(0f, TitleH);
        titleRectTransform.anchoredPosition = Vector2.zero;
        var titleImage = title.AddComponent<Image>();
        titleImage.color = new Color(0f, 0f, 0f, 0.25f);
        var mover = title.AddComponent<DraggableMover>();
        mover.Panel = _panelRectTransform;
        mover.ShowBtn = _showButtonRectTransform;

        ChromeLabel(title, "AI PLAYER INTEL");
        ChromeButton(title, "X", 6f, 22f, () => gameObject.SetActive(false));
        ChromeButton(title, "Refresh", 32f, 62f, () => _controller.ForceRefresh());

        var tabs = new GameObject("TabBar", typeof(RectTransform));
        tabs.transform.SetParent(transform, false);
        var tabRectTransform = tabs.GetComponent<RectTransform>();
        tabRectTransform.anchorMin = new Vector2(0f, 1f);
        tabRectTransform.anchorMax = new Vector2(1f, 1f);
        tabRectTransform.pivot = new Vector2(0.5f, 1f);
        tabRectTransform.sizeDelta = new Vector2(0f, TabH);
        tabRectTransform.anchoredPosition = new Vector2(0f, -TitleH - 1f);
        tabs.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.15f);
        _tradeTab = _widgets.AddTabButton(tabs, "Trade", 8f, 70f, () => SetTab(Tab.Trade));
        _otherTab = _widgets.AddTabButton(tabs, "Other", 82f, 70f, () => SetTab(Tab.Other));
        StyleTabs();

        var filter = new GameObject("FilterBar", typeof(RectTransform));
        filter.transform.SetParent(transform, false);
        var filterRectTransform = filter.GetComponent<RectTransform>();
        filterRectTransform.anchorMin = new Vector2(0f, 1f);
        filterRectTransform.anchorMax = new Vector2(1f, 1f);
        filterRectTransform.pivot = new Vector2(0.5f, 1f);
        filterRectTransform.sizeDelta = new Vector2(0f, FilterH);
        filterRectTransform.anchoredPosition = new Vector2(0f, -TitleH - TabH - 2f);
        ChromeLabel(filter, "Filter:").rectTransform.sizeDelta = new Vector2(48f, 0f);
        var input = _widgets.BuildInputField(filter, "resource / body / company…");
        var inputRectTransform = (RectTransform)input.transform;
        inputRectTransform.anchorMin = new Vector2(0f, 0f);
        inputRectTransform.anchorMax = new Vector2(1f, 1f);
        inputRectTransform.offsetMin = new Vector2(56f, 2f);
        inputRectTransform.offsetMax = new Vector2(-8f, -2f);
        input.text = State.Filter;
        input.onValueChanged.AddListener(OnFilterChanged);

        var handle = new GameObject("ResizeHandle", typeof(RectTransform));
        handle.transform.SetParent(transform, false);
        var handleRectTransform = handle.GetComponent<RectTransform>();
        handleRectTransform.anchorMin = new Vector2(0f, 0f);
        handleRectTransform.anchorMax = new Vector2(1f, 0f);
        handleRectTransform.pivot = new Vector2(0.5f, 0f);
        handleRectTransform.sizeDelta = new Vector2(0f, 10f);
        handleRectTransform.anchoredPosition = Vector2.zero;
        handle.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        handle.AddComponent<ResizeHandle>().Panel = _panelRectTransform;
    }

    TextMeshProUGUI ChromeLabel(GameObject parent, string text) {
        var gameObject = new GameObject("Label", typeof(RectTransform));
        gameObject.transform.SetParent(parent.transform, false);
        var rectTransform = gameObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 0f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 0.5f);
        rectTransform.sizeDelta = new Vector2(200f, 0f);
        rectTransform.anchoredPosition = new Vector2(8f, 0f);
        var label = gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) { label.font = _font; }
        label.text = text;
        label.fontSize = IntelPanelWidgets.RowFontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = IntelPanelWidgets.TextColor;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        return label;
    }

    void ChromeButton(GameObject parent, string label, float rightOffset, float width, Action onClick) {
        var gameObject = new GameObject(label, typeof(RectTransform));
        gameObject.transform.SetParent(parent.transform, false);
        var rectTransform = gameObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(1f, 0.5f);
        rectTransform.pivot = new Vector2(1f, 0.5f);
        rectTransform.sizeDelta = new Vector2(width, TitleH - 6f);
        rectTransform.anchoredPosition = new Vector2(-rightOffset, 0f);
        var image = gameObject.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.12f);
        var button = gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());

        var labelGameObject = new GameObject("T", typeof(RectTransform));
        labelGameObject.transform.SetParent(gameObject.transform, false);
        var labelRectTransform = labelGameObject.GetComponent<RectTransform>();
        labelRectTransform.anchorMin = Vector2.zero;
        labelRectTransform.anchorMax = Vector2.one;
        labelRectTransform.offsetMin = Vector2.zero;
        labelRectTransform.offsetMax = Vector2.zero;
        var text = labelGameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) { text.font = _font; }
        text.text = label;
        text.fontSize = IntelPanelWidgets.RowFontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = IntelPanelWidgets.TextColor;
        text.raycastTarget = false;
    }
}
