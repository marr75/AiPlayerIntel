using System;
using AiPlayerIntel.Ui;
using Cysharp.Threading.Tasks;
using Manager;
using UnityEngine;

namespace AiPlayerIntel.Intel;

// Always-on refresh heartbeat: owns the snapshot cadence, in-flight guard, DIY-gate read,
// view state, and the toggle-key poll. Self-drives in Update so refresh runs on cadence even
// while the panel is hidden; the view only reads Current.
sealed class IntelController : MonoBehaviour {
    static IntelController? _instance;
    internal static IntelController? Instance => _instance;

    float _accum;
    bool _refreshInFlight;
    bool _diyActive = true;
    volatile IntelSnapshot _current = new();

    internal readonly ViewState State = new();
    internal IntelSnapshot Current => _current;
    internal bool InFlight => _refreshInFlight;
    internal event Action? Changed;

    internal static void Ensure() {
        if (_instance != null) { return; }
        var go = new GameObject(nameof(IntelController)) { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<IntelController>();
        Plugin.Log.LogInfo("AI Player Intel controller created.");
    }

    void Update() {
        if (Input.GetKeyDown(Plugin.ToggleKey.Value)) { IntelPanel.Toggle(); }
        _accum += Time.deltaTime;
        if (_accum >= Mathf.Clamp(Plugin.RefreshSeconds.Value, 1f, 30f) && !_refreshInFlight) {
            _accum = 0f;
            Refresh().Forget();
        }
    }

    internal void ForceRefresh() {
        if (_refreshInFlight) { return; }
        _accum = 0f;
        Refresh().Forget();
    }

    async UniTaskVoid Refresh() {
        _refreshInFlight = true;
        try {
            UpdateDiyGate();
            _current = await Collectors.Build(_diyActive);
            Changed?.Invoke();
        } catch (Exception e) {
            Plugin.Log.LogWarning($"AI Player Intel refresh failed: {e.Message}");
        } finally {
            _refreshInFlight = false;
        }
    }

    void UpdateDiyGate() {
        var gm = MonoBehaviourSingleton<GameManager>.Instance;
        bool blocked = gm != null && gm.blockCheckCanPLanMissionForNotPlayer;
        bool active = !blocked;
        if (active != _diyActive) {
            Plugin.Log.LogInfo($"AI Player Intel: DIY valuation {(active ? "enabled" : "disabled")} "
                + $"(blocked={blocked}).");
        }
        _diyActive = active;
    }
}
