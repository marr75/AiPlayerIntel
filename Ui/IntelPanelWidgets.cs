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

    internal IntelPanelWidgets(TMP_FontAsset? font) {
        _font = font;
    }

    internal static PanelTheme SampleBackground(GameObject panelGameObject) {
        Sprite? sprite = null;
        var color = new Color(0.07f, 0.08f, 0.10f, 0.96f);
        var type = Image.Type.Sliced;
        Material? material = null;
        foreach (var image in panelGameObject.GetComponentsInChildren<Image>(true)) {
            if (image.sprite != null) {
                sprite = image.sprite;
                color = image.color;
                type = image.type;
                material = image.material;
                break;
            }
        }
        var root = panelGameObject.GetComponent<Image>() ?? panelGameObject.AddComponent<Image>();
        root.sprite = sprite;
        root.color = color;
        root.type = type;
        if (material != null) { root.material = material; }
        root.raycastTarget = true;
        return new PanelTheme { Sprite = sprite, Color = color, Type = type };
    }

    internal static TMP_FontAsset? SampleFont(GameObject? historyGameObject) {
        if (historyGameObject != null) {
            var label = historyGameObject.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label?.font != null) { return label.font; }
        }
        return TMP_Settings.defaultFontAsset;
    }

    // ---- scroll view (viewport + content(VLG+CSF) + scrollbar; ScrollRect added last) ----

    internal static RectTransform BuildScrollView(GameObject panelGameObject, PanelTheme theme, float topInset) {
        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(panelGameObject.transform, false);
        var viewportRectTransform = viewport.GetComponent<RectTransform>();
        viewportRectTransform.anchorMin = Vector2.zero;
        viewportRectTransform.anchorMax = Vector2.one;
        viewportRectTransform.offsetMin = new Vector2(8f, 12f);
        viewportRectTransform.offsetMax = new Vector2(-22f, -topInset);
        viewport.AddComponent<RectMask2D>();

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRectTransform = content.GetComponent<RectTransform>();
        contentRectTransform.anchorMin = new Vector2(0f, 1f);
        contentRectTransform.anchorMax = new Vector2(1f, 1f);
        contentRectTransform.pivot = new Vector2(0.5f, 1f);
        contentRectTransform.anchoredPosition = Vector2.zero;
        contentRectTransform.sizeDelta = Vector2.zero;
        var verticalLayoutGroup = content.AddComponent<VerticalLayoutGroup>();
        verticalLayoutGroup.childControlWidth = true;
        verticalLayoutGroup.childControlHeight = true;
        verticalLayoutGroup.childForceExpandWidth = true;
        verticalLayoutGroup.childForceExpandHeight = false;
        verticalLayoutGroup.spacing = 1f;
        verticalLayoutGroup.padding = new RectOffset(4, 4, 4, 4);
        var contentSizeFitter = content.AddComponent<ContentSizeFitter>();
        contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var scrollbarGameObject = new GameObject("Scrollbar", typeof(RectTransform));
        scrollbarGameObject.transform.SetParent(panelGameObject.transform, false);
        var scrollbarRectTransform = scrollbarGameObject.GetComponent<RectTransform>();
        scrollbarRectTransform.anchorMin = new Vector2(1f, 0f);
        scrollbarRectTransform.anchorMax = new Vector2(1f, 1f);
        scrollbarRectTransform.offsetMin = new Vector2(-18f, 12f);
        scrollbarRectTransform.offsetMax = new Vector2(-8f, -topInset);
        var track = scrollbarGameObject.AddComponent<Image>();
        track.color = new Color(1f, 1f, 1f, 0.08f);

        var slide = new GameObject("SlidingArea", typeof(RectTransform));
        slide.transform.SetParent(scrollbarGameObject.transform, false);
        Stretch(slide.GetComponent<RectTransform>());
        var handle = new GameObject("Handle", typeof(RectTransform));
        handle.transform.SetParent(slide.transform, false);
        Stretch(handle.GetComponent<RectTransform>());
        var handleImage = handle.AddComponent<Image>();
        handleImage.color = new Color(0f, 0.663f, 0.604f, 0.8f);

        var scrollbar = scrollbarGameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle.GetComponent<RectTransform>();
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        var scroll = panelGameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewportRectTransform;
        scroll.content = contentRectTransform;
        scroll.verticalScrollbar = scrollbar;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.scrollSensitivity = 30f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return contentRectTransform;
    }

    static void Stretch(RectTransform rectTransform) {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    // ---- row / column DSL ----

    internal GameObject MakeRow(Transform parent, float height) {
        var gameObject = new GameObject("Row", typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        var horizontalLayoutGroup = gameObject.AddComponent<HorizontalLayoutGroup>();
        horizontalLayoutGroup.childControlWidth = true;
        horizontalLayoutGroup.childForceExpandWidth = false;
        horizontalLayoutGroup.childControlHeight = true;
        horizontalLayoutGroup.childForceExpandHeight = true;
        horizontalLayoutGroup.spacing = 4f;
        horizontalLayoutGroup.padding = new RectOffset(6, 6, 0, 0);
        return gameObject;
    }

    internal TextMeshProUGUI AddColumn(GameObject row, (float width, float flexWidth) column, string text, Sprite? icon = null) {
        var gameObject = new GameObject("Col", typeof(RectTransform));
        gameObject.transform.SetParent(row.transform, false);
        var layoutElement = gameObject.AddComponent<LayoutElement>();
        if (column.width > 0f) { layoutElement.preferredWidth = layoutElement.minWidth = column.width; }
        layoutElement.flexibleWidth = column.flexWidth;
        var label = MakeLabel(gameObject.transform, text, RowFontSize, TextColor, FontStyles.Normal);
        if (icon != null) {
            AddIcon(gameObject, icon, IconSize);
            label.margin = new Vector4(IconSize + 3f, 0f, 0f, 0f);
        }
        return label;
    }

    internal void AddHeaderCell(
        GameObject row,
        (float width, float flexWidth) column,
        string label,
        SortCol sortColumn,
        IntelPanel panel
    ) {
        var gameObject = new GameObject("HeaderCell", typeof(RectTransform));
        gameObject.transform.SetParent(row.transform, false);
        var layoutElement = gameObject.AddComponent<LayoutElement>();
        if (column.width > 0f) { layoutElement.preferredWidth = layoutElement.minWidth = column.width; }
        layoutElement.flexibleWidth = column.flexWidth;
        var state = panel.State;
        var arrow = state.Sort == sortColumn ? state.Desc ? " ▼" : " ▲" : "";
        var text = MakeLabel(gameObject.transform, label + arrow, RowFontSize, HeaderColor, FontStyles.Bold);
        text.raycastTarget = true;
        var button = gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = text;
        button.onClick.AddListener(() => panel.CycleSort(sortColumn));
    }

    internal TabButton AddTabButton(GameObject parent, string label, float xPosition, float width, Action onClick) {
        var gameObject = new GameObject(label, typeof(RectTransform));
        gameObject.transform.SetParent(parent.transform, false);
        var rectTransform = gameObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0f, 0.5f);
        rectTransform.pivot = new Vector2(0f, 0.5f);
        rectTransform.sizeDelta = new Vector2(width, IntelPanel.TabH - 4f);
        rectTransform.anchoredPosition = new Vector2(xPosition, 0f);
        var image = gameObject.AddComponent<Image>();
        var button = gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());
        var text = MakeLabel(gameObject.transform, label, RowFontSize, TextColor, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        return new TabButton { Bg = image, Label = text };
    }

    internal static void StyleTab(TabButton tab, bool active) {
        tab.Bg.color = active ? new Color(0.55f, 0.85f, 0.95f, 0.25f) : new Color(1f, 1f, 1f, 0.06f);
        tab.Label.color = active ? HeaderColor : new Color(0.6f, 0.6f, 0.6f, 1f);
    }

    internal void AddSpanRow(Transform content, RowVm rowViewModel, Action? onToggle) {
        var row = MakeRow(content, rowViewModel.Kind == RowKind.Leaf ? 18f : 20f);
        AddSpacer(row, 6f + rowViewModel.Depth * 16f);
        if (onToggle != null && rowViewModel.Key.Length > 0) { AddCaret(row, rowViewModel.Expanded, onToggle); }
        if (rowViewModel.Icon != null) { AddIconCell(row, rowViewModel.Icon, IconSize); }
        var label = MakeLabel(
            row.transform,
            SpanLabel(rowViewModel),
            RowFontSize,
            SpanColor(rowViewModel.Kind),
            rowViewModel.Kind == RowKind.BodyHeader ? FontStyles.Bold : FontStyles.Normal
        );
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;
        var layoutElement = label.gameObject.AddComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1f;
    }

    internal static void MakeClickable(GameObject row, Action onClick) {
        var image = row.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0f);
        var button = row.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());
    }

    static string SpanLabel(RowVm rowViewModel) {
        switch (rowViewModel.Kind) {
            case RowKind.BodyHeader: return $"{rowViewModel.Label}   ({rowViewModel.LeafCount})";
            case RowKind.CompanyHeader: {
                var text = rowViewModel.Label;
                if (rowViewModel.IsHq) { text += IntelFormat.HqTag.text; }
                if (rowViewModel.TimeValuePerDay > 0) {
                    text += $"    time value {IntelFormat.Money(rowViewModel.TimeValuePerDay)}/day ({rowViewModel.CostCalcType})";
                }
                return $"{text}   ({rowViewModel.LeafCount})";
            }
            case RowKind.Objective: {
                var text = "→ " + rowViewModel.Label;
                if (rowViewModel.Objective is { } objective && objective.CurrentStepText.Length > 0) { text += "  —  " + objective.CurrentStepText; }
                return text;
            }
            default: return rowViewModel.Label;
        }
    }

    static Color SpanColor(RowKind kind) =>
        kind switch {
            RowKind.BodyHeader => Color.white,
            RowKind.CompanyHeader => new Color(0.82f, 0.9f, 1f, 1f),
            RowKind.Objective => new Color(0.72f, 0.78f, 0.82f, 1f),
            _ => new Color(0.6f, 0.6f, 0.6f, 1f),
        };

    TextMeshProUGUI MakeLabel(Transform parent, string text, float size, Color color, FontStyles style) {
        var gameObject = new GameObject("Text", typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        var rectTransform = gameObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        var label = gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) { label.font = _font; }
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    static void AddSpacer(GameObject row, float width) {
        var gameObject = new GameObject("Spacer", typeof(RectTransform));
        gameObject.transform.SetParent(row.transform, false);
        var layoutElement = gameObject.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = layoutElement.minWidth = width;
    }

    void AddCaret(GameObject row, bool expanded, Action onToggle) {
        var gameObject = new GameObject("Caret", typeof(RectTransform));
        gameObject.transform.SetParent(row.transform, false);
        var layoutElement = gameObject.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = layoutElement.minWidth = 16f;
        var toggleLabel = gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) { toggleLabel.font = _font; }
        toggleLabel.text = expanded ? "-" : "+";
        toggleLabel.fontSize = RowFontSize;
        toggleLabel.color = TextColor;
        toggleLabel.alignment = TextAlignmentOptions.Center;
        toggleLabel.raycastTarget = true;
        var button = gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = toggleLabel;
        button.onClick.AddListener(() => onToggle());
    }

    static void AddIconCell(GameObject row, Sprite icon, float size) {
        var gameObject = new GameObject("Icon", typeof(RectTransform));
        gameObject.transform.SetParent(row.transform, false);
        var layoutElement = gameObject.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = layoutElement.minWidth = size;
        var image = gameObject.AddComponent<Image>();
        image.sprite = icon;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    static void AddIcon(GameObject cell, Sprite sprite, float size) {
        var gameObject = new GameObject("Icon", typeof(RectTransform));
        gameObject.transform.SetParent(cell.transform, false);
        var rectTransform = gameObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = rectTransform.anchorMax = rectTransform.pivot = new Vector2(0f, 0.5f);
        rectTransform.sizeDelta = new Vector2(size, size);
        rectTransform.anchoredPosition = new Vector2(1f, 0f);
        var image = gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    // ---- filter input field ----

    internal TMP_InputField BuildInputField(GameObject parent, string placeholderText) {
        var gameObject = new GameObject("FilterInput", typeof(RectTransform));
        gameObject.transform.SetParent(parent.transform, false);
        var background = gameObject.AddComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.10f);
        var input = gameObject.AddComponent<TMP_InputField>();

        var area = new GameObject("TextArea", typeof(RectTransform));
        area.transform.SetParent(gameObject.transform, false);
        var areaRectTransform = area.GetComponent<RectTransform>();
        areaRectTransform.anchorMin = Vector2.zero;
        areaRectTransform.anchorMax = Vector2.one;
        areaRectTransform.offsetMin = new Vector2(6f, 1f);
        areaRectTransform.offsetMax = new Vector2(-6f, -1f);
        area.AddComponent<RectMask2D>();

        var placeholder = MakeLabel(
            area.transform,
            placeholderText,
            RowFontSize,
            new Color(0.7f, 0.7f, 0.7f, 0.6f),
            FontStyles.Italic
        );
        var text = MakeLabel(area.transform, "", RowFontSize, TextColor, FontStyles.Normal);

        input.textViewport = areaRectTransform;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.targetGraphic = background;
        input.fontAsset = _font;
        input.pointSize = RowFontSize;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.customCaretColor = true;
        input.caretColor = TextColor;
        return input;
    }

    // ---- theme sampling (sample live donor widgets before gutting) ----

    internal readonly struct PanelTheme {
        public Sprite? Sprite { get; init; }
        public Image.Type Type { get; init; }
        public Color Color { get; init; }
    }

    // ---- tab bar (persistent chrome; two exclusive tabs restyled on switch) ----

    internal readonly struct TabButton {
        public Image Bg { get; init; }
        public TextMeshProUGUI Label { get; init; }
    }
}

// Title-bar drag: moves the panel RectTransform; normalized-position persistence across
// canvas resize; two-frame yield before reading the show button's world corners.
sealed class DraggableMover : MonoBehaviour,
    IPointerDownHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler {
    Canvas? _canvas;
    RectTransform? _canvasRectTransform;
    Vector2 _dragStart;
    Vector2 _lastCanvasSize;
    Vector2 _pressScreen;
    internal RectTransform Panel = null!;
    internal RectTransform? ShowBtn;

    void Awake() {
        _canvas = GetComponentInParent<Canvas>();
        _canvasRectTransform = _canvas?.GetComponent<RectTransform>();
    }

    IEnumerator Start() {
        yield return null;
        yield return null;
        if (PanelLayout.Saved) { RestoreNormalized(); } else { PositionUnderButton(); }
        if (_canvasRectTransform != null) { _lastCanvasSize = _canvasRectTransform.rect.size; }
    }

    void Update() {
        if (_canvasRectTransform == null) { return; }
        var size = _canvasRectTransform.rect.size;
        if (size != _lastCanvasSize) {
            _lastCanvasSize = size;
            if (PanelLayout.Saved) { RestoreNormalized(); }
        }
    }

    public void OnBeginDrag(PointerEventData eventData) => _dragStart = Panel.anchoredPosition;

    public void OnDrag(PointerEventData eventData) {
        var scale = _canvas != null ? _canvas.scaleFactor : 1f;
        Panel.anchoredPosition = _dragStart + (eventData.position - _pressScreen) / scale;
        Clamp();
    }

    public void OnEndDrag(PointerEventData eventData) {
        Clamp();
        StoreNormalized();
    }

    public void OnPointerDown(PointerEventData eventData) => _pressScreen = eventData.position;

    void PositionUnderButton() {
        if (ShowBtn == null || _canvasRectTransform == null) { return; }
        var worldCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
        var corners = new Vector3[4];
        ShowBtn.GetWorldCorners(corners);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRectTransform, corners[0], worldCamera, out var local)) {
            Panel.anchoredPosition = new Vector2(local.x, local.y - 40f);
            Clamp();
            StoreNormalized();
        }
    }

    void Clamp() {
        if (_canvasRectTransform == null) { return; }
        var rect = _canvasRectTransform.rect;
        var size = Panel.sizeDelta;
        var position = Panel.anchoredPosition;
        position.x = Mathf.Clamp(position.x, rect.xMin, Mathf.Max(rect.xMin, rect.xMax - size.x));
        position.y = Mathf.Clamp(position.y, rect.yMin + size.y, rect.yMax);
        Panel.anchoredPosition = position;
    }

    void StoreNormalized() {
        if (_canvasRectTransform == null) { return; }
        var rect = _canvasRectTransform.rect;
        var position = Panel.anchoredPosition;
        PanelLayout.NormPos = new Vector2(
            Mathf.Approximately(rect.xMax, 0f) ? 0f : position.x / rect.xMax,
            Mathf.Approximately(rect.yMax, 0f) ? 0f : position.y / rect.yMax
        );
        PanelLayout.Saved = true;
    }

    void RestoreNormalized() {
        if (_canvasRectTransform == null) { return; }
        var rect = _canvasRectTransform.rect;
        Panel.anchoredPosition = new Vector2(PanelLayout.NormPos.x * rect.xMax, PanelLayout.NormPos.y * rect.yMax);
        Clamp();
    }
}

// Bottom-edge resize: mutates panel sizeDelta with min clamps; content reflows via VLG+CSF.
sealed class ResizeHandle : MonoBehaviour, IPointerDownHandler, IDragHandler {
    const float MinWidth = 520f;
    const float MinHeight = 220f;

    Canvas? _canvas;
    Vector2 _startScreen;
    Vector2 _startSize;
    internal RectTransform Panel = null!;

    void Awake() => _canvas = GetComponentInParent<Canvas>();

    public void OnDrag(PointerEventData eventData) {
        var scale = _canvas != null ? _canvas.scaleFactor : 1f;
        var delta = (eventData.position - _startScreen) / scale;
        var newWidth = Mathf.Max(MinWidth, _startSize.x + delta.x);
        var newHeight = Mathf.Max(MinHeight, _startSize.y - delta.y);
        Panel.sizeDelta = new Vector2(newWidth, newHeight);
        PanelLayout.Size = Panel.sizeDelta;
    }

    public void OnPointerDown(PointerEventData eventData) {
        _startScreen = eventData.position;
        _startSize = Panel.sizeDelta;
    }
}

// Runtime layout persistence (survives open/close within a session).
static class PanelLayout {
    internal static Vector2 NormPos;
    internal static Vector2 Size = new(560f, 460f);
    internal static bool Saved;
}
