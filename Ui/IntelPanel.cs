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
    static readonly (float w, float flex)[] Cols = {
        (16, 0), (0, 3), (0, 3), (0, 1), (0, 1), (0, 2),
    };

    static readonly FieldInfo? ShowField =
        typeof(NotificationManager).GetField("showNotificationHistory", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo? HistoryField =
        typeof(NotificationManager).GetField("notificationHistory", BindingFlags.Instance | BindingFlags.NonPublic);

    static IntelPanel? _instance;

    IntelController _controller = null!;
    IntelPanelWidgets _w = null!;
    RectTransform _panelRT = null!;
    RectTransform _content = null!;
    ScrollRect? _scroll;
    RectTransform? _showBtnRT;
    TMP_FontAsset? _font;
    IntelPanelWidgets.TabButton _tradeTab;
    IntelPanelWidgets.TabButton _otherTab;

    string _lastSig = "";
    bool _pending;
    bool _force;
    float _pointerHeldUntil;

    internal ViewState State => _controller.State;

    internal static void Inject(NotificationManager nm) {
        try {
            if (ShowField?.GetValue(nm) is not Button showBtn) {
                Plugin.Log.LogWarning("AI Player Intel: notification button not found; UGUI panel skipped.");
                return;
            }
            if (HistoryField?.GetValue(nm) is not GameObject historyGO) {
                Plugin.Log.LogWarning("AI Player Intel: notification history not found; UGUI panel skipped.");
                return;
            }
            var canvas = showBtn.GetComponentInParent<Canvas>();
            if (canvas == null) {
                Plugin.Log.LogWarning("AI Player Intel: HUD canvas not found; UGUI panel skipped.");
                return;
            }

            var font = IntelPanelWidgets.SampleFont(historyGO);
            var panelGO = Instantiate(historyGO, canvas.transform);
            panelGO.name = "aiPlayerIntelPanel";
            panelGO.transform.SetAsLastSibling();
            var rt = (RectTransform)panelGO.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = PanelLayout.Size;
            rt.anchoredPosition = new Vector2(-9999f, -9999f);
            (panelGO.GetComponent<LayoutElement>() ?? panelGO.AddComponent<LayoutElement>()).ignoreLayout = true;

            var theme = IntelPanelWidgets.SampleBackground(panelGO);
            for (int i = rt.childCount - 1; i >= 0; i--) { Destroy(rt.GetChild(i).gameObject); }
            foreach (var cg in panelGO.GetComponents<CanvasGroup>()) { cg.interactable = cg.blocksRaycasts = true; }
            foreach (var s in panelGO.GetComponents<ScrollRect>()) { DestroyImmediate(s); }
            foreach (var l in panelGO.GetComponents<LayoutGroup>()) { DestroyImmediate(l); }
            if (panelGO.GetComponent<ContentSizeFitter>() is { } f) { DestroyImmediate(f); }

            var content = IntelPanelWidgets.BuildScrollView(panelGO, theme, TitleH + TabH + FilterH + 8f);
            panelGO.SetActive(false);
            var panel = panelGO.AddComponent<IntelPanel>();
            panel.Init(showBtn, content, font);
            _instance = panel;
            Plugin.Log.LogInfo("AI Player Intel UGUI panel injected.");
        } catch (Exception e) {
            Plugin.Log.LogWarning($"AI Player Intel: UGUI panel injection failed: {e}");
        }
    }

    void Init(Button showBtn, RectTransform content, TMP_FontAsset? font) {
        _panelRT = (RectTransform)transform;
        _showBtnRT = showBtn.GetComponent<RectTransform>();
        _content = content;
        _font = font;
        _scroll = GetComponent<ScrollRect>();
        _w = new IntelPanelWidgets(font);
        _controller = IntelController.Instance!;
        BuildChrome();
        _controller.Changed += OnChanged;
    }

    internal static void Toggle() {
        if (_instance == null) { return; }
        var go = _instance.gameObject;
        go.SetActive(!go.activeSelf);
        if (go.activeSelf) { go.transform.SetAsLastSibling(); }
    }

    internal static bool HandleEscape() {
        if (_instance == null || !_instance.gameObject.activeSelf) { return false; }
        _instance.gameObject.SetActive(false);
        return true;
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

    void OnChanged() => _pending = true;

    void Update() {
        if (AnyPointerHeld()) { _pointerHeldUntil = Time.unscaledTime + 0.25f; }
        if (_pending && Time.unscaledTime >= _pointerHeldUntil) { Rebuild(); }
    }

    internal void CycleSort(SortCol col) {
        var st = State;
        if (st.Sort != col) { st.Sort = col; st.Desc = false; }
        else if (!st.Desc) { st.Desc = true; }
        else { st.Sort = SortCol.Default; st.Desc = false; }
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
        var ex = State.Expanded;
        if (!ex.Remove(key)) { ex.Add(key); }
        _force = _pending = true;
    }

    void OnFilterChanged(string value) {
        State.Filter = value ?? "";
        _force = _pending = true;
    }

    void Rebuild() {
        _pending = false;
        var rows = IntelView.Build(_controller.Current, State);
        var sig = Signature(rows);
        if (!_force && sig == _lastSig) { return; }
        _force = false;
        _lastSig = sig;
        for (int i = _content.childCount - 1; i >= 0; i--) { Destroy(_content.GetChild(i).gameObject); }
        Emit(rows);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
    }

    void Emit(List<RowVm> rows) {
        EmitColumnHeader();
        foreach (var vm in rows) {
            switch (vm.Kind) {
                case RowKind.BodyHeader:
                case RowKind.CompanyHeader:
                    var key = vm.Key;
                    _w.AddSpanRow(_content, vm, () => ToggleExpand(key));
                    break;
                case RowKind.Objective:
                case RowKind.Note:
                    _w.AddSpanRow(_content, vm, null);
                    break;
                case RowKind.Leaf:
                    EmitLeaf(vm.Detail!);
                    break;
            }
        }
    }

    void EmitColumnHeader() {
        var h = _w.MakeRow(_content, 18f);
        _w.AddColumn(h, Cols[0], "");
        _w.AddHeaderCell(h, Cols[1], "Resource", SortCol.Resource, this);
        _w.AddHeaderCell(h, Cols[2], "Max buy", SortCol.MaxBuy, this);
        _w.AddHeaderCell(h, Cols[3], "Have", SortCol.Have, this);
        _w.AddHeaderCell(h, Cols[4], "Want", SortCol.Want, this);
        _w.AddHeaderCell(h, Cols[5], "ETA", SortCol.Eta, this);
    }

    void EmitLeaf(IntelRow r) {
        var row = _w.MakeRow(_content, 18f);
        _w.AddColumn(row, Cols[0], "");
        _w.AddColumn(row, Cols[1], r.Line.Resource, r.Line.ResourceIcon);
        _w.AddColumn(row, Cols[2], IntelFormat.MaxBuy(r.Line));
        _w.AddColumn(row, Cols[3], IntelFormat.Have(r.Line));
        _w.AddColumn(row, Cols[4], IntelFormat.Want(r.Line));
        _w.AddColumn(row, Cols[5], IntelFormat.RateEta(r.Line));
        if (r.Line.MaxBid is { } bid && r.Line.Rd is { } rd && r.Body is { } body) {
            IntelPanelWidgets.MakeClickable(row, () => IntelActions.OpenOffer(body, rd, bid, r.Line.PriceQty));
        } else {
            IntelPanelWidgets.MakeClickable(row, () => IntelActions.OpenMarket(r.Body));
        }
    }

    string Signature(List<RowVm> rows) {
        var sb = new StringBuilder(1024);
        var st = State;
        sb.Append((int)st.Sort).Append(st.Desc ? 'D' : 'A').Append('|')
            .Append((int)st.Tab).Append('|').Append(st.Filter).Append('\n');
        foreach (var vm in rows) {
            sb.Append((int)vm.Kind).Append(vm.Depth).Append(vm.Expanded ? '+' : '-').Append(vm.LeafCount).Append('|');
            if (vm.Kind == RowKind.Leaf && vm.Detail is { } r) {
                var l = r.Line;
                sb.Append(l.Resource).Append('|')
                    .Append(IntelFormat.MaxBuy(l)).Append('|').Append(IntelFormat.Have(l)).Append('|')
                    .Append(IntelFormat.Want(l)).Append('|').Append(IntelFormat.RateEta(l)).Append('|')
                    .Append(l.ResourceIcon != null ? l.ResourceIcon.name : "");
            } else {
                sb.Append(vm.Label).Append('|').Append(vm.IsHq ? 'H' : '-');
                if (vm.Kind == RowKind.CompanyHeader) {
                    sb.Append(vm.TimeValuePerDay.ToString("0.##")).Append(vm.CostCalcType);
                }
                if (vm.Objective is { } o) { sb.Append(o.ContractTitle).Append(o.CurrentStepText).Append(o.Type); }
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    static bool AnyPointerHeld() =>
        Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);

    // ---- persistent chrome: title bar (drag + buttons), filter bar, resize handle ----

    void BuildChrome() {
        var title = new GameObject("TitleBar", typeof(RectTransform));
        title.transform.SetParent(transform, false);
        var trt = title.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.sizeDelta = new Vector2(0f, TitleH);
        trt.anchoredPosition = Vector2.zero;
        var titleImg = title.AddComponent<Image>();
        titleImg.color = new Color(0f, 0f, 0f, 0.25f);
        var mover = title.AddComponent<DraggableMover>();
        mover.Panel = _panelRT;
        mover.ShowBtn = _showBtnRT;

        ChromeLabel(title, "AI PLAYER INTEL");
        ChromeButton(title, "X", 6f, 22f, () => gameObject.SetActive(false));
        ChromeButton(title, "Refresh", 32f, 62f, () => _controller.ForceRefresh());

        var tabs = new GameObject("TabBar", typeof(RectTransform));
        tabs.transform.SetParent(transform, false);
        var tabrt = tabs.GetComponent<RectTransform>();
        tabrt.anchorMin = new Vector2(0f, 1f);
        tabrt.anchorMax = new Vector2(1f, 1f);
        tabrt.pivot = new Vector2(0.5f, 1f);
        tabrt.sizeDelta = new Vector2(0f, TabH);
        tabrt.anchoredPosition = new Vector2(0f, -TitleH - 1f);
        tabs.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.15f);
        _tradeTab = _w.AddTabButton(tabs, "Trade", 8f, 70f, () => SetTab(Tab.Trade));
        _otherTab = _w.AddTabButton(tabs, "Other", 82f, 70f, () => SetTab(Tab.Other));
        StyleTabs();

        var filter = new GameObject("FilterBar", typeof(RectTransform));
        filter.transform.SetParent(transform, false);
        var frt = filter.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0f, 1f);
        frt.anchorMax = new Vector2(1f, 1f);
        frt.pivot = new Vector2(0.5f, 1f);
        frt.sizeDelta = new Vector2(0f, FilterH);
        frt.anchoredPosition = new Vector2(0f, -TitleH - TabH - 2f);
        ChromeLabel(filter, "Filter:").rectTransform.sizeDelta = new Vector2(48f, 0f);
        var input = _w.BuildInputField(filter, "resource / body / company…");
        var irt = ((RectTransform)input.transform);
        irt.anchorMin = new Vector2(0f, 0f);
        irt.anchorMax = new Vector2(1f, 1f);
        irt.offsetMin = new Vector2(56f, 2f);
        irt.offsetMax = new Vector2(-8f, -2f);
        input.text = State.Filter;
        input.onValueChanged.AddListener(OnFilterChanged);

        var handle = new GameObject("ResizeHandle", typeof(RectTransform));
        handle.transform.SetParent(transform, false);
        var hrt = handle.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0f, 0f);
        hrt.anchorMax = new Vector2(1f, 0f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.sizeDelta = new Vector2(0f, 10f);
        hrt.anchoredPosition = Vector2.zero;
        handle.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        handle.AddComponent<ResizeHandle>().Panel = _panelRT;
    }

    TextMeshProUGUI ChromeLabel(GameObject parent, string text) {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(200f, 0f);
        rt.anchoredPosition = new Vector2(8f, 0f);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) { tmp.font = _font; }
        tmp.text = text;
        tmp.fontSize = IntelPanelWidgets.RowFontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = IntelPanelWidgets.TextColor;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.raycastTarget = false;
        return tmp;
    }

    void ChromeButton(GameObject parent, string label, float rightOffset, float width, Action onClick) {
        var go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(width, TitleH - 6f);
        rt.anchoredPosition = new Vector2(-rightOffset, 0f);
        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.12f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        var t = new GameObject("T", typeof(RectTransform));
        t.transform.SetParent(go.transform, false);
        var trt = t.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        var tmp = t.AddComponent<TextMeshProUGUI>();
        if (_font != null) { tmp.font = _font; }
        tmp.text = label;
        tmp.fontSize = IntelPanelWidgets.RowFontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = IntelPanelWidgets.TextColor;
        tmp.raycastTarget = false;
    }
}
