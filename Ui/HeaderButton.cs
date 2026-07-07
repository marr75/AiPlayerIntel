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
// Power/LifeSupport/Fleet tracker mods use). onClick toggles the IMGUI IntelWindow.
sealed class HeaderButton : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler {
    static readonly FieldInfo? ShowBtnField =
        typeof(NotificationManager).GetField("showNotificationHistory", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo? HistoryField =
        typeof(NotificationManager).GetField("notificationHistory", BindingFlags.Instance | BindingFlags.NonPublic);

    Action? _onClick;
    Image _bg = null!;
    Color _normal, _hover, _press;
    RectTransform _rt = null!;
    RectTransform? _showBtnRT;
    Canvas? _canvas;
    RectTransform? _canvasRT;
    Vector2 _pressScreenPos;
    Vector2 _dragStart;

    internal static void Inject(NotificationManager nm) {
        try {
            if (ShowBtnField?.GetValue(nm) is not Button showBtn) {
                Plugin.Log.LogWarning("AI Player Intel: notification button not found; header button skipped (F10 still works).");
                return;
            }
            var canvas = showBtn.GetComponentInParent<Canvas>();
            if (canvas == null) {
                Plugin.Log.LogWarning("AI Player Intel: HUD canvas not found; header button skipped (F10 still works).");
                return;
            }
            var historyGO = HistoryField?.GetValue(nm) as GameObject;
            var font = FindFont(showBtn, historyGO);

            var go = new GameObject("aiPlayerIntelHeaderButton", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            go.transform.SetAsLastSibling();
            go.AddComponent<LayoutElement>().ignoreLayout = true;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(150f, 30f);
            rt.anchoredPosition = new Vector2(-9999f, -9999f);

            var bg = go.AddComponent<Image>();
            var srcImg = showBtn.GetComponent<Image>();
            if (srcImg != null) {
                bg.sprite = srcImg.sprite;
                bg.type = srcImg.type;
                bg.color = srcImg.color;
            } else {
                bg.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);
            }

            MakeLabel(go, font);

            var hb = go.AddComponent<HeaderButton>();
            hb._bg = bg;
            hb._normal = bg.color;
            hb._hover = bg.color * 1.3f;
            hb._press = bg.color * 0.7f;
            hb._showBtnRT = showBtn.GetComponent<RectTransform>();
            hb._onClick = IntelWindow.Toggle;
            Plugin.Log.LogInfo("AI Player Intel header button injected.");
        } catch (Exception e) {
            Plugin.Log.LogWarning($"AI Player Intel: header button injection failed: {e.Message}");
        }
    }

    void Awake() {
        _rt = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
        _canvasRT = _canvas?.GetComponent<RectTransform>();
    }

    IEnumerator Start() {
        yield return null;
        yield return null;
        PositionNextToNotificationButton();
    }

    void PositionNextToNotificationButton() {
        if (_showBtnRT == null || _canvasRT == null) { return; }
        var cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
        var corners = new Vector3[4];
        _showBtnRT.GetWorldCorners(corners);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, corners[1], cam, out var local)) {
            _rt.anchoredPosition = new Vector2(local.x - 10f - _rt.sizeDelta.x, local.y - 5f - _rt.sizeDelta.y - 6f);
            Clamp();
        }
    }

    public void OnPointerEnter(PointerEventData e) { if (_bg != null) { _bg.color = _hover; } }
    public void OnPointerExit(PointerEventData e) { if (_bg != null) { _bg.color = _normal; } }

    public void OnPointerDown(PointerEventData e) {
        _pressScreenPos = e.position;
        if (_bg != null) { _bg.color = _press; }
    }

    public void OnPointerUp(PointerEventData e) {
        if (_bg != null) { _bg.color = _hover; }
        if (Vector2.Distance(e.position, _pressScreenPos) < EventSystem.current.pixelDragThreshold) {
            _onClick?.Invoke();
        }
    }

    public void OnBeginDrag(PointerEventData e) { _dragStart = _rt.anchoredPosition; }

    public void OnDrag(PointerEventData e) {
        var scale = _canvas != null ? _canvas.scaleFactor : 1f;
        _rt.anchoredPosition = _dragStart + (e.position - _pressScreenPos) / scale;
        Clamp();
    }

    public void OnEndDrag(PointerEventData e) {
        Clamp();
        if (_bg != null) { _bg.color = _normal; }
    }

    void Clamp() {
        if (_canvasRT == null) { return; }
        var rect = _canvasRT.rect;
        var size = _rt.sizeDelta;
        var pos = _rt.anchoredPosition;
        pos.x = Mathf.Clamp(pos.x, rect.xMin, rect.xMax - size.x);
        pos.y = Mathf.Clamp(pos.y, rect.yMin + size.y, rect.yMax);
        _rt.anchoredPosition = pos;
    }

    static void MakeLabel(GameObject parent, TMP_FontAsset? font) {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) { tmp.font = font; }
        tmp.text = "AI PLAYER INTEL";
        tmp.fontSize = 11f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
    }

    static TMP_FontAsset? FindFont(Button showBtn, GameObject? historyGO) {
        var src = showBtn.GetComponentInChildren<TextMeshProUGUI>(true);
        if (src?.font != null) { return src.font; }
        if (historyGO != null) {
            src = historyGO.GetComponentInChildren<TextMeshProUGUI>(true);
            if (src?.font != null) { return src.font; }
        }
        return TMP_Settings.defaultFontAsset;
    }
}
