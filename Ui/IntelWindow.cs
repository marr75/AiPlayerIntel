using System;
using System.Collections.Generic;
using System.Linq;
using AiPlayerIntel.Intel;
using Cysharp.Threading.Tasks;
using Game.Info;
using Game.UI;
using Game.UI.Windows.Windows;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;

namespace AiPlayerIntel.Ui;

sealed class IntelWindow : MonoBehaviour {
    const int WindowId = 1179552853;
    const int ObjectiveCharCap = 72;
    const string PriceLegend = "Self-source = AI's own per-unit make/mine cost ($materials + delay days, each day priced at its time value). "
        + "Max buy = most it'll pay per unit for a batch that size (10% under self-source). "
        + "Priced at the row's need qty (bigger batches lower the per-unit ceiling as fixed costs amortize); "
        + "a sell offer priced above max buy is ignored.";
    const string BehaviorLegend = "AIs also buy any resource offered below their Max buy price even with no listed need, "
        + "and rarely post buy-bids (mostly buy reactively). Bodies tagged (HQ) are self-sourced — the AI never buys there.";
    const float MinWidth = 660f;
    const float MaxWidth = 1200f;
    const float WidthPad = 36f;
    static IntelWindow? _instance;
    static Rect _winRect = new(60f, 60f, 680f, 560f);
    static Vector2 _scroll;
    static float _measureMaxX;
    static readonly HashSet<string> _expanded = new();

    bool _show;
    bool _styled;
    float _accum;
    bool _refreshInFlight;
    bool _diyActive = true;
    volatile IntelSnapshot _snapshot = new();

    internal static void Ensure() {
        if (_instance != null) { return; }
        var go = new GameObject(nameof(IntelWindow)) { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<IntelWindow>();
        Plugin.Log.LogInfo("AI Player Intel GUI host created.");
    }

    internal static void Toggle() {
        if (_instance != null) { _instance._show = !_instance._show; }
    }

    internal static bool HandleEscape() {
        if (_instance == null || !_instance._show) { return false; }
        _instance._show = false;
        return true;
    }

    void Update() {
        if (Input.GetKeyDown(Plugin.ToggleKey.Value)) { _show = !_show; }
        if (!_show) { return; }
        _accum += Time.deltaTime;
        if (_accum >= Mathf.Clamp(Plugin.RefreshSeconds.Value, 1f, 30f) && !_refreshInFlight) {
            _accum = 0f;
            Refresh().Forget();
        }
    }

    async UniTaskVoid Refresh() {
        _refreshInFlight = true;
        try {
            UpdateDiyGate();
            _snapshot = await Collectors.Build(_diyActive);
        } catch (Exception e) {
            Plugin.Log.LogWarning($"AI Player Intel refresh failed: {e.Message}");
        } finally {
            _refreshInFlight = false;
        }
    }

    void UpdateDiyGate() {
        var gm = MonoBehaviourSingleton<GameManager>.Instance;
        bool blocked = gm != null && gm.blockCheckCanPLanMissionForNotPlayer;
        bool active = Plugin.EnableDiyValuation.Value && !blocked;
        if (active != _diyActive) {
            Plugin.Log.LogInfo($"AI Player Intel: DIY valuation {(active ? "enabled" : "disabled")} "
                + $"(config={Plugin.EnableDiyValuation.Value}, blocked={blocked}).");
        }
        _diyActive = active;
    }

    void OnGUI() {
        if (!_show) { return; }
        _styled = Plugin.StyledTheme.Value;
        if (_styled) { Skin.Build(); }
        var title = $"AI Player Intel  [{Plugin.ToggleKey.Value} to close]";
        _winRect = _styled
            ? GUILayout.Window(WindowId, _winRect, DrawWindow, title, Skin.Win)
            : GUILayout.Window(WindowId, _winRect, DrawWindow, title);

        if (!string.IsNullOrEmpty(GUI.tooltip)) {
            var style = _styled ? Skin.Tooltip : GUI.skin.box;
            var content = new GUIContent(GUI.tooltip);
            const float w = 360f;
            float h = style.CalcHeight(content, w);
            var pos = Event.current.mousePosition;
            GUI.Label(new Rect(pos.x + 12f, pos.y + 12f, w, h), content, style);
        }
    }

    GUIStyle SBox => _styled ? Skin.Box : GUI.skin.box;
    GUIStyle SBtn => _styled ? Skin.Btn : GUI.skin.button;
    GUIStyle SLbl => _styled ? Skin.Label : GUI.skin.label;

    void DrawWindow(int id) {
        var snap = _snapshot;
        var age = Mathf.Max(0f, Time.realtimeSinceStartup - snap.BuiltAt);
        bool measuring = Event.current.type == EventType.Repaint;
        if (measuring) { _measureMaxX = 0f; }
        bool bodyFirst = Plugin.BodyFirstGrouping.Value;

        DrawTitleButtons();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"AI companies: {snap.Companies.Count}   updated {age:0}s ago"
            + (snap.DiyActive ? "" : "   (DIY valuation off)"), SLbl, GUILayout.ExpandWidth(false));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(bodyFirst ? "Body → Company" : "Company → Body", SBtn, GUILayout.ExpandWidth(false))) {
            Plugin.BodyFirstGrouping.Value = !bodyFirst;
        }
        GUILayout.EndHorizontal();
        Track(measuring);

        DrawLegend();

        _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(460f));
        if (snap.Companies.Count == 0) {
            GUILayout.Label("No AI companies found (not in a running game yet?).", SLbl);
        } else if (bodyFirst) {
            DrawByBody(snap, measuring);
        } else {
            DrawByCompany(snap, measuring);
        }
        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(0f, _winRect.width - 108f), 20f));

        if (measuring && _measureMaxX > 0f) {
            float target = Mathf.Clamp(_measureMaxX + WidthPad, MinWidth, MaxWidth);
            if (Mathf.Abs(target - _winRect.width) > 4f) { _winRect.width = target; }
        }
    }

    void DrawTitleButtons() {
        // Right-anchored, clear of the window border (~12px) so they stay fully visible.
        float w = _winRect.width;
        var closeRect = new Rect(w - 34f, 4f, 20f, 18f);
        var refreshRect = new Rect(w - 98f, 4f, 60f, 18f);
        if (GUI.Button(refreshRect, "Refresh", SBtn) && !_refreshInFlight) {
            _accum = 0f;
            Refresh().Forget();
        }
        if (GUI.Button(closeRect, "X", SBtn)) { _show = false; }
    }

    static void Track(bool measuring) {
        if (!measuring) { return; }
        _measureMaxX = Mathf.Max(_measureMaxX, GUILayoutUtility.GetLastRect().xMax);
    }

    void DrawLegend() {
        float w = _winRect.width - 28f;
        GUILayout.Label(PriceLegend, LegendLbl, GUILayout.Width(w));
        GUILayout.Label(BehaviorLegend, LegendLbl, GUILayout.Width(w));
    }

    static GUIStyle? _legendLbl;
    static bool _legendStyled;
    GUIStyle LegendLbl {
        get {
            if (_legendLbl == null || _legendStyled != _styled) {
                _legendLbl = new GUIStyle(SLbl) { wordWrap = true, fontStyle = FontStyle.Italic };
                _legendStyled = _styled;
            }
            return _legendLbl;
        }
    }

    void DrawByCompany(IntelSnapshot snap, bool measuring) {
        var byCompany = snap.Rows.ToLookup(r => r.CompanyKey);
        foreach (var c in snap.Companies) {
            var key = "co:" + c.CompanyKey;
            bool open = _expanded.Contains(key);
            GUILayout.BeginVertical(SBox);
            DrawCompanyHeader(c, key, open, measuring);
            if (open) {
                DrawSecondaryObjectives(c, measuring);
                var groups = byCompany[c.CompanyKey].GroupBy(r => r.BodyId).ToList();
                if (groups.Count == 0) {
                    GUILayout.Label("    (no standing demand)", SLbl);
                }
                foreach (var g in groups) {
                    DrawBodySubHeader(g.First(), measuring);
                    foreach (var r in g) { DrawRow(r, measuring); }
                }
            }
            GUILayout.EndVertical();
            GUILayout.Space(4f);
        }
    }

    void DrawByBody(IntelSnapshot snap, bool measuring) {
        var byKey = snap.Companies.ToDictionary(c => c.CompanyKey);
        foreach (var body in snap.Rows.GroupBy(r => r.BodyId)) {
            var first = body.First();
            var key = "body:" + first.BodyId;
            bool open = _expanded.Contains(key);
            GUILayout.BeginVertical(SBox);
            GUILayout.BeginHorizontal();
            Caret(key, open);
            if (MarketIcon(first.BodyIcon, first.BodyName, first.Body)) { OpenMarket(first.Body); }
            GUILayout.Label(new GUIContent(first.BodyName, first.BodyName), SLbl, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Track(measuring);
            if (open) {
                foreach (var co in body.GroupBy(r => r.CompanyKey)) {
                    byKey.TryGetValue(co.Key, out var c);
                    DrawCompanySubHeader(c, co.Key, co.Any(r => r.IsHq), measuring);
                    foreach (var r in co) { DrawRow(r, measuring); }
                }
            }
            GUILayout.EndVertical();
            GUILayout.Space(4f);
        }
    }

    void DrawCompanyHeader(CompanyIntel c, string key, bool open, bool measuring) {
        GUILayout.BeginHorizontal();
        Caret(key, open);
        DrawIcon(c.CompanyIcon, 20f);
        GUILayout.Label(new GUIContent(c.CompanyName, c.CompanyName), SLbl, GUILayout.ExpandWidth(false));
        DrawTimeValue(c);
        DrawObjective(c.Current);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        Track(measuring);
    }

    void DrawCompanySubHeader(CompanyIntel? c, string fallbackKey, bool isHq, bool measuring) {
        GUILayout.BeginHorizontal();
        GUILayout.Space(24f);
        DrawIcon(c?.CompanyIcon, 20f);
        var name = c?.CompanyName ?? fallbackKey;
        GUILayout.Label(new GUIContent(name, name), SLbl, GUILayout.ExpandWidth(false));
        if (isHq) { GUILayout.Label(HqTag, SLbl, GUILayout.ExpandWidth(false)); }
        if (c != null) { DrawTimeValue(c); }
        DrawObjective(c?.Current);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        Track(measuring);
    }

    void DrawBodySubHeader(IntelRow row, bool measuring) {
        GUILayout.BeginHorizontal();
        GUILayout.Space(24f);
        if (MarketIcon(row.BodyIcon, row.BodyName, row.Body)) { OpenMarket(row.Body); }
        GUILayout.Label(new GUIContent(row.BodyName, row.BodyName), SLbl, GUILayout.ExpandWidth(false));
        if (row.IsHq) { GUILayout.Label(HqTag, SLbl, GUILayout.ExpandWidth(false)); }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        Track(measuring);
    }

    void DrawSecondaryObjectives(CompanyIntel c, bool measuring) {
        foreach (var o in c.Others) {
            GUILayout.BeginHorizontal();
            GUILayout.Space(24f);
            GUILayout.Label("also:", SLbl, GUILayout.ExpandWidth(false));
            DrawObjective(o);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Track(measuring);
        }
    }

    static readonly GUIContent HqTag = new("  (HQ — won't buy here)", "The AI self-sources at its main object and never buys market offers here.");

    void DrawObjective(ObjectiveLine? current) {
        if (current is { } o) {
            var title = o.ContractTitle.Length > 0 ? o.ContractTitle : o.Type;
            GUILayout.Label($"  {title}", SLbl, GUILayout.ExpandWidth(false));
            if (o.Segments.Count > 0) {
                GUILayout.Label("  —  ", SLbl, GUILayout.ExpandWidth(false));
                DrawObjectiveSegments(o);
            }
        } else {
            GUILayout.Label("  (no active contract)", SLbl, GUILayout.ExpandWidth(false));
        }
    }

    static readonly string TimeValueTip = "How many dollars this AI values one day of self-source delay. "
        + "A higher time value inflates its max buy price, so it out-bids rivals for the same offer.";

    void DrawTimeValue(CompanyIntel c) {
        if (c.TimeValuePerDay <= 0) { return; }
        GUILayout.Label(new GUIContent($"  time value {Money(c.TimeValuePerDay)}/day ({c.CostCalcType})", TimeValueTip),
            SLbl, GUILayout.ExpandWidth(false));
    }

    void Caret(string key, bool open) {
        if (GUILayout.Button(open ? "▾" : "▸", SBtn, GUILayout.Width(24f))) {
            if (open) { _expanded.Remove(key); } else { _expanded.Add(key); }
        }
    }

    static readonly string OfferHint = "click: create sell offer at this AI's max buy "
        + "(open market — a higher-ceiling buyer may take it first)";

    void DrawRow(IntelRow row, bool measuring) {
        var r = row.Line;
        GUILayout.BeginHorizontal();
        GUILayout.Space(48f);
        bool canOffer = r.MaxBid.HasValue && r.Rd != null && row.Body != null;
        if (MarketIcon(r.ResourceIcon, r.Resource, row.Body, canOffer ? OfferHint : "(open market)")) {
            if (canOffer && r.MaxBid is { } ceiling && r.Rd is { } rd && row.Body is { } body) {
                OpenOffer(body, rd, ceiling, r.PriceQty);
            } else {
                OpenMarket(row.Body);
            }
        }
        bool stocked = r.State == ResourceState.Stocked;
        GUILayout.Label(new GUIContent(RowSentence(r, stocked), r.Resource), SLbl, GUILayout.ExpandWidth(false));
        DrawPrice(r);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        Track(measuring);
    }

    static string RowSentence(ResourceLine r, bool stocked) {
        var parts = new List<string>();
        if (r.Provenance.Length > 0) { parts.Add(r.Provenance); }
        parts.Add(StockText(r, stocked));
        var state = DeficitState(r, stocked);
        if (state.Length > 0) { parts.Add(state); }
        return $"{r.Resource} — {string.Join(" · ", parts)}";
    }

    void DrawPrice(ResourceLine r) {
        if (r.MaxBid is { } mb) {
            var text = $"max buy {Mag(r.PriceQty)}u @ {Money(mb)}/u "
                + $"(self-source {Money(r.DiyMoneyPerUnit ?? 0)}/u + ~{Mag(r.DiyTotalDays ?? 0)} days)";
            GUILayout.Label(new GUIContent($" · {text}", PriceLegend), SLbl, GUILayout.ExpandWidth(false));
        }
        if (r.PostedPrice is { } p) {
            GUILayout.Label($" · offer {(r.PostedIsBuy == true ? "buy" : "sell")} {Money(p)}×{r.PostedCountLeft:0}",
                SLbl, GUILayout.ExpandWidth(false));
        }
    }

    static string StockText(ResourceLine r, bool stocked) {
        if (r.IsBom) {
            return stocked ? $"have {Mag(r.Have)} stocked" : $"have {Mag(r.Have)}/need {Mag(r.PrimaryQty)}";
        }
        return $"have {Mag(r.Have)}";
    }

    static string DeficitState(ResourceLine r, bool stocked) {
        if (stocked) { return ""; }
        var parts = new List<string>();
        if (r.IsBom && r.Deficit > 0) {
            var word = StateWord(r.State);
            parts.Add(word.Length > 0 ? $"deficit {Mag(r.Deficit)} ({word})" : $"deficit {Mag(r.Deficit)}");
        }
        if (r.Rate is { } rate && Math.Abs(rate) >= 0.05) {
            parts.Add($"{(rate >= 0 ? "+" : "")}{Mag(rate)}/day");
        }
        if (r.EtaDays is { } eta) { parts.Add($"ETA ~{eta:0} d"); }
        return string.Join(" · ", parts);
    }

    static void OpenMarket(ObjectInfo? body) {
        if (body == null) { return; }
        var ui = SerializedMonoBehaviourSingleton<UIManager>.Instance;
        if (ui == null) { return; }
        ui.Open(EWindowType.ObjectInfo, body);
        ui.Open(EWindowType.MarketOffer, body);
    }

    // Opens the make-offer dialog pre-filled with a sell offer at the AI's max-buy ceiling.
    // No auto-submit: the user reviews and clicks the dialog's confirm button.
    static void OpenOffer(ObjectInfo body, ResourceDefinition rd, double maxBuyPerUnit, double needQty) {
        var ui = SerializedMonoBehaviourSingleton<UIManager>.Instance;
        if (ui == null) { return; }
        ui.Open(EWindowType.MarketOfferMakeOffer, body);
        var dialog = ui.GetWindow<MarketOfferMakeOfferWindow>();
        if (dialog == null) { OpenMarket(body); return; }
        dialog.sell.isOn = true;
        dialog.rdDropDown.SetSelectRD(rd);
        var price = Math.Floor(maxBuyPerUnit * 100.0) / 100.0;
        dialog.pricePerUnitInputField.text = price.ToString();
        dialog.countToBuySellSlider.value = (float)needQty;
    }

    void DrawObjectiveSegments(ObjectiveLine o) {
        int cost = 0;
        foreach (var s in o.Segments) { cost += s.Kind == ObjSegmentKind.Icon ? 2 : s.Text.Length; }
        bool truncated = cost > ObjectiveCharCap;
        var tip = truncated ? o.CurrentStepText : "";
        int budget = ObjectiveCharCap;
        foreach (var s in o.Segments) {
            if (budget <= 0) { break; }
            if (s.Kind == ObjSegmentKind.Icon) {
                DrawIcon(s.Icon, 16f);
                budget -= 2;
            } else {
                var text = s.Text.Length > budget ? s.Text.Substring(0, budget) : s.Text;
                budget -= text.Length;
                GUILayout.Label(new GUIContent(text, tip), SLbl, GUILayout.ExpandWidth(false));
            }
        }
        if (truncated) {
            GUILayout.Label(new GUIContent("…", tip), SLbl, GUILayout.ExpandWidth(false));
        }
    }

    static GUIStyle? _iconStyle;
    static GUIStyle IconStyle => _iconStyle ??= new GUIStyle { margin = new RectOffset(2, 2, 2, 2) };

    static void DrawIcon(Sprite? s, float size) {
        var rect = GUILayoutUtility.GetRect(size, size, IconStyle, GUILayout.Width(size), GUILayout.Height(size));
        if (Event.current.type == EventType.Repaint) { PaintSprite(rect, s); }
    }

    bool MarketIcon(Sprite? s, string tooltip, ObjectInfo? body, string hint = "(open market)") {
        if (s == null) { return false; }
        bool clicked = GUILayout.Button(new GUIContent("", $"{tooltip}  {hint}"), IconBtn,
            GUILayout.Width(20f), GUILayout.Height(20f));
        if (Event.current.type == EventType.Repaint) { PaintSprite(GUILayoutUtility.GetLastRect(), s); }
        return clicked && body != null;
    }

    static void PaintSprite(Rect rect, Sprite? s) {
        if (s == null || s.texture == null) { return; }
        var tr = s.textureRect;
        var tex = s.texture;
        GUI.DrawTextureWithTexCoords(rect, tex,
            new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
    }

    static GUIStyle? _iconBtn;
    static GUIStyle IconBtn => _iconBtn ??= new GUIStyle(GUI.skin.button) {
        padding = new RectOffset(1, 1, 1, 1),
        margin = new RectOffset(2, 2, 2, 2),
    };

    static string StateWord(ResourceState s) => s switch {
        ResourceState.Stocked => "stocked",
        ResourceState.Acquiring => "acquiring",
        ResourceState.Needed => "needed",
        _ => "",
    };

    static string Mag(double v) {
        double a = Math.Abs(v);
        if (a < 10) { return $"{v:0.#}"; }
        if (a < 1000) { return $"{v:0}"; }
        if (a < 1_000_000) { return $"{v / 1e3:0.#}K"; }
        if (a < 1_000_000_000) { return $"{v / 1e6:0.#}M"; }
        return $"{v / 1e9:0.#}B";
    }

    static string Money(double v) => $"${Mag(v)}";

    static class Skin {
        static bool _built;
        internal static GUIStyle Win = null!;
        internal static GUIStyle Box = null!;
        internal static GUIStyle Btn = null!;
        internal static GUIStyle Label = null!;
        internal static GUIStyle Tooltip = null!;
        static readonly List<Texture2D> _tex = new();

        static Texture2D T(float r, float g, float b, float a) {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, new Color(r, g, b, a));
            t.Apply();
            _tex.Add(t);
            return t;
        }

        internal static void Build() {
            if (_built) { return; }
            _built = true;
            var text = new Color(0.835f, 0.835f, 0.835f, 1f);
            var win = T(0.055f, 0.071f, 0.075f, 0.98f);
            var box = T(0.094f, 0.11f, 0.118f, 0.98f);
            var btn = T(0.2f, 0.2f, 0.2f, 1f);
            var btnHover = T(0.4f, 0.4f, 0.4f, 1f);
            var btnActive = T(0f, 0.663f, 0.604f, 1f);

            Win = new GUIStyle(GUI.skin.window) {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter,
                border = new RectOffset(12, 12, 24, 12),
                padding = new RectOffset(10, 10, 26, 10),
            };
            Win.normal.background = Win.onNormal.background = win;
            Win.normal.textColor = Win.onNormal.textColor = text;

            Box = new GUIStyle(GUI.skin.box) {
                border = new RectOffset(6, 6, 6, 6),
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 2, 2),
            };
            Box.normal.background = box;
            Box.normal.textColor = text;

            Btn = new GUIStyle(GUI.skin.button) {
                border = new RectOffset(6, 6, 6, 6),
                padding = new RectOffset(8, 8, 4, 4),
                alignment = TextAnchor.MiddleCenter,
            };
            Btn.normal.background = btn;
            Btn.hover.background = btnHover;
            Btn.active.background = btnActive;
            Btn.normal.textColor = text;
            Btn.hover.textColor = Color.white;

            Label = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                padding = new RectOffset(2, 2, 2, 2),
            };
            Label.normal.textColor = text;

            Tooltip = new GUIStyle(GUI.skin.box) {
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                padding = new RectOffset(8, 8, 6, 6),
            };
            Tooltip.normal.background = box;
            Tooltip.normal.textColor = text;
        }
    }
}
