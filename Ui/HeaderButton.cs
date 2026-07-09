using System;
using System.Collections;
using System.Reflection;
using Manager;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AiPlayerIntel.Ui;

// Floating HUD toggle, parked next to the vanilla notification button (same pattern the
// Power/LifeSupport/Fleet tracker mods use). onClick toggles the UGUI IntelPanel.
sealed class HeaderButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler {
    const float LeftEdgeInset = 260f;

    static readonly FieldInfo? ShowButtonField =
        typeof(NotificationManager).GetField("showNotificationHistory", BindingFlags.Instance | BindingFlags.NonPublic);

    static readonly FieldInfo? HistoryField =
        typeof(NotificationManager).GetField("notificationHistory", BindingFlags.Instance | BindingFlags.NonPublic);

    Image _background = null!;
    Canvas? _canvas;
    RectTransform? _canvasRectTransform;
    Vector2 _dragStart;
    Color _normal, _hover, _press;

    Action? _onClick;
    Vector2 _pressScreenPos;
    RectTransform _rectTransform = null!;
    RectTransform? _showButtonRectTransform;

    void Awake() {
        _rectTransform = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
        _canvasRectTransform = _canvas?.GetComponent<RectTransform>();
    }

    IEnumerator Start() {
        yield return null;
        yield return null;
        PositionNextToNotificationButton();
    }

    public void OnBeginDrag(PointerEventData eventData) => _dragStart = _rectTransform.anchoredPosition;

    public void OnDrag(PointerEventData eventData) {
        var scale = _canvas != null ? _canvas.scaleFactor : 1f;
        _rectTransform.anchoredPosition = _dragStart + (eventData.position - _pressScreenPos) / scale;
        Clamp();
    }

    public void OnEndDrag(PointerEventData eventData) {
        Clamp();
        if (_background != null) { _background.color = _normal; }
    }

    public void OnPointerDown(PointerEventData eventData) {
        _pressScreenPos = eventData.position;
        if (_background != null) { _background.color = _press; }
    }

    public void OnPointerEnter(PointerEventData eventData) {
        if (_background != null) { _background.color = _hover; }
    }

    public void OnPointerExit(PointerEventData eventData) {
        if (_background != null) { _background.color = _normal; }
    }

    public void OnPointerUp(PointerEventData eventData) {
        if (_background != null) { _background.color = _hover; }
        if (Vector2.Distance(eventData.position, _pressScreenPos) < EventSystem.current.pixelDragThreshold) {
            _onClick?.Invoke();
        }
    }

    internal static void Inject(NotificationManager notificationManager) {
        try {
            if (ShowButtonField?.GetValue(notificationManager) is not Button showButton) {
                Plugin.Log.LogWarning(
                    "AI Player Intel: notification button not found; header button skipped (F10 still works)."
                );
                return;
            }
            var canvas = showButton.GetComponentInParent<Canvas>();
            if (canvas == null) {
                Plugin.Log.LogWarning(
                    "AI Player Intel: HUD canvas not found; header button skipped (F10 still works)."
                );
                return;
            }
            var historyGameObject = HistoryField?.GetValue(notificationManager) as GameObject;
            var font = FindFont(showButton, historyGameObject);

            var gameObject = new GameObject("aiPlayerIntelHeaderButton", typeof(RectTransform));
            gameObject.transform.SetParent(canvas.transform, false);
            gameObject.transform.SetAsLastSibling();
            gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var rectTransform = gameObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0f, 1f);
            rectTransform.sizeDelta = new Vector2(150f, 30f);
            rectTransform.anchoredPosition = new Vector2(-9999f, -9999f);

            var background = gameObject.AddComponent<Image>();
            var sourceImage = showButton.GetComponent<Image>();
            if (sourceImage != null) {
                background.sprite = sourceImage.sprite;
                background.type = sourceImage.type;
                background.color = sourceImage.color;
            } else {
                background.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);
            }

            MakeLabel(gameObject, font);

            var headerButton = gameObject.AddComponent<HeaderButton>();
            headerButton._background = background;
            headerButton._normal = background.color;
            headerButton._hover = background.color * 1.3f;
            headerButton._press = background.color * 0.7f;
            headerButton._showButtonRectTransform = showButton.GetComponent<RectTransform>();
            headerButton._onClick = IntelPanel.Toggle;
            Plugin.Log.LogInfo("AI Player Intel header button injected.");
        } catch (Exception exception) {
            Plugin.Log.LogWarning($"AI Player Intel: header button injection failed: {exception.Message}");
        }
    }

    void PositionNextToNotificationButton() {
        if (_showButtonRectTransform == null || _canvasRectTransform == null) { return; }
        var worldCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
        var corners = new Vector3[4];
        _showButtonRectTransform.GetWorldCorners(corners);
        // Default to the left side of the top bar (same height as the notification button) instead
        // of stacking under it on the far right; LeftEdgeInset keeps clear of the company logo.
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRectTransform, corners[0], worldCamera, out var local)) {
            var rect = _canvasRectTransform.rect;
            _rectTransform.anchoredPosition = new Vector2(rect.xMin + LeftEdgeInset, local.y - 12f);
            Clamp();
        }
    }

    void Clamp() {
        if (_canvasRectTransform == null) { return; }
        var rect = _canvasRectTransform.rect;
        var size = _rectTransform.sizeDelta;
        var position = _rectTransform.anchoredPosition;
        position.x = Mathf.Clamp(position.x, rect.xMin, rect.xMax - size.x);
        position.y = Mathf.Clamp(position.y, rect.yMin + size.y, rect.yMax);
        _rectTransform.anchoredPosition = position;
    }

    static void MakeLabel(GameObject parent, TMP_FontAsset? font) {
        var gameObject = new GameObject("Label", typeof(RectTransform));
        gameObject.transform.SetParent(parent.transform, false);
        var rectTransform = gameObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.sizeDelta = Vector2.zero;
        var label = gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) { label.font = font; }
        label.text = "AI PLAYER INTEL";
        label.fontSize = 11f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    static TMP_FontAsset? FindFont(Button showButton, GameObject? historyGameObject) {
        var source = showButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (source?.font != null) { return source.font; }
        if (historyGameObject != null) {
            source = historyGameObject.GetComponentInChildren<TextMeshProUGUI>(true);
            if (source?.font != null) { return source.font; }
        }
        return TMP_Settings.defaultFontAsset;
    }
}
