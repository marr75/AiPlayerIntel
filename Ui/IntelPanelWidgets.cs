using System;
using System.Collections;
using AiPlayerIntel.Intel;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AiPlayerIntel.Ui;

// Concrete UGUI mechanics for the intel panel only — reimplemented fresh from FleetTracker
// patterns (row/column DSL, theme sampling, drag/resize). One shared Cols[] authority lives
// in IntelPanel; this class just realizes rows/cells/chrome from it.
sealed class IntelPanelWidgets {
    internal const float RowFontSize = 11f;
    internal const float IconSize = 16f;
    internal static readonly Color TextColor = new(0.85f, 0.85f, 0.85f, 1f);
    internal static readonly Color HeaderColor = new(0.55f, 0.85f, 0.95f, 1f);

    readonly TMP_FontAsset? _font;

    internal IntelPanelWidgets(TMP_FontAsset? font) => _font = font;

    // ---- theme sampling (sample live donor widgets before gutting) ----

    internal readonly struct PanelTheme {
        public Sprite? Sprite { get; init; }
        public Image.Type Type { get; init; }
        public Color Color { get; init; }
    }

    internal static PanelTheme SampleBackground(GameObject panelGO) {
        Sprite? sprite = null;
        var color = new Color(0.07f, 0.08f, 0.10f, 0.96f);
        var type = Image.Type.Sliced;
        Material? mat = null;
        foreach (var img in panelGO.GetComponentsInChildren<Image>(true)) {
            if (img.sprite != null) {
                sprite = img.sprite;
                color = img.color;
                type = img.type;
                mat = img.material;
                break;
            }
        }
        var root = panelGO.GetComponent<Image>() ?? panelGO.AddComponent<Image>();
        root.sprite = sprite;
        root.color = color;
        root.type = type;
        if (mat != null) { root.material = mat; }
        root.raycastTarget = true;
        return new PanelTheme { Sprite = sprite, Color = color, Type = type };
    }

    internal static TMP_FontAsset? SampleFont(GameObject? historyGO) {
        if (historyGO != null) {
            var t = historyGO.GetComponentInChildren<TextMeshProUGUI>(true);
            if (t?.font != null) { return t.font; }
        }
        return TMP_Settings.defaultFontAsset;
    }

    // ---- scroll view (viewport + content(VLG+CSF) + scrollbar; ScrollRect added last) ----

    internal static RectTransform BuildScrollView(GameObject panelGO, PanelTheme theme, float topInset) {
        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(panelGO.transform, false);
        var vrt = viewport.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(8f, 12f);
        vrt.offsetMax = new Vector2(-22f, -topInset);
        viewport.AddComponent<RectMask2D>();

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = Vector2.zero;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 1f;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var sb = new GameObject("Scrollbar", typeof(RectTransform));
        sb.transform.SetParent(panelGO.transform, false);
        var sbrt = sb.GetComponent<RectTransform>();
        sbrt.anchorMin = new Vector2(1f, 0f);
        sbrt.anchorMax = new Vector2(1f, 1f);
        sbrt.offsetMin = new Vector2(-18f, 12f);
        sbrt.offsetMax = new Vector2(-8f, -topInset);
        var track = sb.AddComponent<Image>();
        track.color = new Color(1f, 1f, 1f, 0.08f);

        var slide = new GameObject("SlidingArea", typeof(RectTransform));
        slide.transform.SetParent(sb.transform, false);
        Stretch(slide.GetComponent<RectTransform>());
        var handle = new GameObject("Handle", typeof(RectTransform));
        handle.transform.SetParent(slide.transform, false);
        Stretch(handle.GetComponent<RectTransform>());
        var handleImg = handle.AddComponent<Image>();
        handleImg.color = new Color(0f, 0.663f, 0.604f, 0.8f);

        var scrollbar = sb.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle.GetComponent<RectTransform>();
        scrollbar.targetGraphic = handleImg;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        var scroll = panelGO.AddComponent<ScrollRect>();
        scroll.viewport = vrt;
        scroll.content = crt;
        scroll.verticalScrollbar = scrollbar;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.scrollSensitivity = 30f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return crt;
    }

    static void Stretch(RectTransform rt) {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ---- row / column DSL ----

    internal GameObject MakeRow(Transform parent, float height) {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = height;
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.childControlWidth = true;
        h.childForceExpandWidth = false;
        h.childControlHeight = true;
        h.childForceExpandHeight = true;
        h.spacing = 4f;
        h.padding = new RectOffset(6, 6, 0, 0);
        return go;
    }

    internal TextMeshProUGUI AddColumn(GameObject row, (float w, float flex) col, string text, Sprite? icon = null) {
        var go = new GameObject("Col", typeof(RectTransform));
        go.transform.SetParent(row.transform, false);
        var le = go.AddComponent<LayoutElement>();
        if (col.w > 0f) { le.preferredWidth = le.minWidth = col.w; }
        le.flexibleWidth = col.flex;
        var tmp = MakeLabel(go.transform, text, RowFontSize, TextColor, FontStyles.Normal);
        if (icon != null) {
            AddIcon(go, icon, IconSize);
            tmp.margin = new Vector4(IconSize + 3f, 0f, 0f, 0f);
        }
        return tmp;
    }

    internal void AddHeaderCell(GameObject row, (float w, float flex) col, string label, SortCol sortCol, IntelPanel panel) {
        var go = new GameObject("HeaderCell", typeof(RectTransform));
        go.transform.SetParent(row.transform, false);
        var le = go.AddComponent<LayoutElement>();
        if (col.w > 0f) { le.preferredWidth = le.minWidth = col.w; }
        le.flexibleWidth = col.flex;
        var st = panel.State;
        var arrow = st.Sort == sortCol ? (st.Desc ? " ▼" : " ▲") : "";
        var tmp = MakeLabel(go.transform, label + arrow, RowFontSize, HeaderColor, FontStyles.Bold);
        tmp.raycastTarget = true;
        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = tmp;
        btn.onClick.AddListener(() => panel.CycleSort(sortCol));
    }

    // ---- tab bar (persistent chrome; two exclusive tabs restyled on switch) ----

    internal readonly struct TabButton {
        public Image Bg { get; init; }
        public TextMeshProUGUI Label { get; init; }
    }

    internal TabButton AddTabButton(GameObject parent, string label, float x, float width, Action onClick) {
        var go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(width, IntelPanel.TabH - 4f);
        rt.anchoredPosition = new Vector2(x, 0f);
        var img = go.AddComponent<Image>();
        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());
        var tmp = MakeLabel(go.transform, label, RowFontSize, TextColor, FontStyles.Bold);
        tmp.alignment = TextAlignmentOptions.Center;
        return new TabButton { Bg = img, Label = tmp };
    }

    internal static void StyleTab(TabButton tab, bool active) {
        tab.Bg.color = active ? new Color(0.55f, 0.85f, 0.95f, 0.25f) : new Color(1f, 1f, 1f, 0.06f);
        tab.Label.color = active ? HeaderColor : new Color(0.6f, 0.6f, 0.6f, 1f);
    }

    internal void AddSpanRow(Transform content, RowVm vm, Action? onToggle) {
        var row = MakeRow(content, vm.Kind == RowKind.Leaf ? 18f : 20f);
        AddSpacer(row, 6f + vm.Depth * 16f);
        if (onToggle != null && vm.Key.Length > 0) { AddCaret(row, vm.Expanded, onToggle); }
        if (vm.Icon != null) { AddIconCell(row, vm.Icon, IconSize); }
        var lbl = MakeLabel(row.transform, SpanLabel(vm), RowFontSize, SpanColor(vm.Kind),
            vm.Kind == RowKind.BodyHeader ? FontStyles.Bold : FontStyles.Normal);
        lbl.enableWordWrapping = true;
        lbl.overflowMode = TextOverflowModes.Overflow;
        var le = lbl.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
    }

    internal static void MakeClickable(GameObject row, Action onClick) {
        var img = row.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0f);
        var btn = row.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());
    }

    static string SpanLabel(RowVm vm) {
        switch (vm.Kind) {
            case RowKind.BodyHeader:
                return $"{vm.Label}   ({vm.LeafCount})";
            case RowKind.CompanyHeader: {
                var s = vm.Label;
                if (vm.IsHq) { s += IntelFormat.HqTag.text; }
                if (vm.TimeValuePerDay > 0) {
                    s += $"    time value {IntelFormat.Money(vm.TimeValuePerDay)}/day ({vm.CostCalcType})";
                }
                return $"{s}   ({vm.LeafCount})";
            }
            case RowKind.Objective: {
                var s = "→ " + vm.Label;
                if (vm.Objective is { } o && o.CurrentStepText.Length > 0) { s += "  —  " + o.CurrentStepText; }
                return s;
            }
            default:
                return vm.Label;
        }
    }

    static Color SpanColor(RowKind kind) => kind switch {
        RowKind.BodyHeader => Color.white,
        RowKind.CompanyHeader => new Color(0.82f, 0.9f, 1f, 1f),
        RowKind.Objective => new Color(0.72f, 0.78f, 0.82f, 1f),
        _ => new Color(0.6f, 0.6f, 0.6f, 1f),
    };

    TextMeshProUGUI MakeLabel(Transform parent, string text, float size, Color color, FontStyles style) {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) { tmp.font = _font; }
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    static void AddSpacer(GameObject row, float width) {
        var go = new GameObject("Spacer", typeof(RectTransform));
        go.transform.SetParent(row.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = width;
    }

    void AddCaret(GameObject row, bool expanded, Action onToggle) {
        var go = new GameObject("Caret", typeof(RectTransform));
        go.transform.SetParent(row.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = 16f;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) { tmp.font = _font; }
        tmp.text = expanded ? "-" : "+";
        tmp.fontSize = RowFontSize;
        tmp.color = TextColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = true;
        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = tmp;
        btn.onClick.AddListener(() => onToggle());
    }

    static void AddIconCell(GameObject row, Sprite icon, float size) {
        var go = new GameObject("Icon", typeof(RectTransform));
        go.transform.SetParent(row.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = size;
        var img = go.AddComponent<Image>();
        img.sprite = icon;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    static void AddIcon(GameObject cell, Sprite sprite, float size) {
        var go = new GameObject("Icon", typeof(RectTransform));
        go.transform.SetParent(cell.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = new Vector2(1f, 0f);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    // ---- filter input field ----

    internal TMP_InputField BuildInputField(GameObject parent, string placeholderText) {
        var go = new GameObject("FilterInput", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var bg = go.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.10f);
        var input = go.AddComponent<TMP_InputField>();

        var area = new GameObject("TextArea", typeof(RectTransform));
        area.transform.SetParent(go.transform, false);
        var art = area.GetComponent<RectTransform>();
        art.anchorMin = Vector2.zero;
        art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(6f, 1f);
        art.offsetMax = new Vector2(-6f, -1f);
        area.AddComponent<RectMask2D>();

        var placeholder = MakeLabel(area.transform, placeholderText, RowFontSize,
            new Color(0.7f, 0.7f, 0.7f, 0.6f), FontStyles.Italic);
        var text = MakeLabel(area.transform, "", RowFontSize, TextColor, FontStyles.Normal);

        input.textViewport = art;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.targetGraphic = bg;
        input.fontAsset = _font;
        input.pointSize = RowFontSize;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.customCaretColor = true;
        input.caretColor = TextColor;
        return input;
    }
}

// Title-bar drag: moves the panel RectTransform; normalized-position persistence across
// canvas resize; two-frame yield before reading the show button's world corners.
sealed class DraggableMover : MonoBehaviour,
    IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler {
    internal RectTransform Panel = null!;
    internal RectTransform? ShowBtn;

    Canvas? _canvas;
    RectTransform? _canvasRT;
    Vector2 _pressScreen;
    Vector2 _dragStart;
    Vector2 _lastCanvasSize;

    void Awake() {
        _canvas = GetComponentInParent<Canvas>();
        _canvasRT = _canvas?.GetComponent<RectTransform>();
    }

    IEnumerator Start() {
        yield return null;
        yield return null;
        if (PanelLayout.Saved) { RestoreNormalized(); } else { PositionUnderButton(); }
        if (_canvasRT != null) { _lastCanvasSize = _canvasRT.rect.size; }
    }

    void Update() {
        if (_canvasRT == null) { return; }
        var size = _canvasRT.rect.size;
        if (size != _lastCanvasSize) {
            _lastCanvasSize = size;
            if (PanelLayout.Saved) { RestoreNormalized(); }
        }
    }

    public void OnPointerDown(PointerEventData e) => _pressScreen = e.position;
    public void OnBeginDrag(PointerEventData e) => _dragStart = Panel.anchoredPosition;

    public void OnDrag(PointerEventData e) {
        var scale = _canvas != null ? _canvas.scaleFactor : 1f;
        Panel.anchoredPosition = _dragStart + (e.position - _pressScreen) / scale;
        Clamp();
    }

    public void OnEndDrag(PointerEventData e) {
        Clamp();
        StoreNormalized();
    }

    void PositionUnderButton() {
        if (ShowBtn == null || _canvasRT == null) { return; }
        var cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
        var corners = new Vector3[4];
        ShowBtn.GetWorldCorners(corners);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, corners[0], cam, out var local)) {
            Panel.anchoredPosition = new Vector2(local.x, local.y - 40f);
            Clamp();
            StoreNormalized();
        }
    }

    void Clamp() {
        if (_canvasRT == null) { return; }
        var rect = _canvasRT.rect;
        var size = Panel.sizeDelta;
        var pos = Panel.anchoredPosition;
        pos.x = Mathf.Clamp(pos.x, rect.xMin, Mathf.Max(rect.xMin, rect.xMax - size.x));
        pos.y = Mathf.Clamp(pos.y, rect.yMin + size.y, rect.yMax);
        Panel.anchoredPosition = pos;
    }

    void StoreNormalized() {
        if (_canvasRT == null) { return; }
        var rect = _canvasRT.rect;
        var pos = Panel.anchoredPosition;
        PanelLayout.NormPos = new Vector2(
            Mathf.Approximately(rect.xMax, 0f) ? 0f : pos.x / rect.xMax,
            Mathf.Approximately(rect.yMax, 0f) ? 0f : pos.y / rect.yMax);
        PanelLayout.Saved = true;
    }

    void RestoreNormalized() {
        if (_canvasRT == null) { return; }
        var rect = _canvasRT.rect;
        Panel.anchoredPosition = new Vector2(PanelLayout.NormPos.x * rect.xMax, PanelLayout.NormPos.y * rect.yMax);
        Clamp();
    }
}

// Bottom-edge resize: mutates panel sizeDelta with min clamps; content reflows via VLG+CSF.
sealed class ResizeHandle : MonoBehaviour, IPointerDownHandler, IDragHandler {
    internal RectTransform Panel = null!;
    const float MinWidth = 520f;
    const float MinHeight = 220f;

    Canvas? _canvas;
    Vector2 _startScreen;
    Vector2 _startSize;

    void Awake() => _canvas = GetComponentInParent<Canvas>();

    public void OnPointerDown(PointerEventData e) {
        _startScreen = e.position;
        _startSize = Panel.sizeDelta;
    }

    public void OnDrag(PointerEventData e) {
        var scale = _canvas != null ? _canvas.scaleFactor : 1f;
        var delta = (e.position - _startScreen) / scale;
        var w = Mathf.Max(MinWidth, _startSize.x + delta.x);
        var h = Mathf.Max(MinHeight, _startSize.y - delta.y);
        Panel.sizeDelta = new Vector2(w, h);
        PanelLayout.Size = Panel.sizeDelta;
    }
}

// Runtime layout persistence (survives open/close within a session).
static class PanelLayout {
    internal static Vector2 NormPos;
    internal static Vector2 Size = new(920f, 460f);
    internal static bool Saved;
}
