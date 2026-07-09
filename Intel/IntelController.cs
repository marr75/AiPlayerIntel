using System;
using AiPlayerIntel.Core;
using AiPlayerIntel.Ui;
using Cysharp.Threading.Tasks;
using Manager;
using UnityEngine;

namespace AiPlayerIntel.Intel;

// Always-on refresh heartbeat: owns the snapshot cadence, in-flight guard, DIY-gate read,
// view state, and the toggle-key poll. Self-drives in Update so refresh runs on cadence even
// while the panel is hidden; the view only reads Current.
sealed class IntelController : MonoBehaviour {
    internal readonly ViewState State = new();

    float _accumulatedSeconds;
    volatile IntelSnapshot _current = new();
    bool _diyActive = true;
    internal static IntelController? Instance { get; private set; }

    internal IntelSnapshot Current => _current;
    internal bool InFlight { get; private set; }

    void Update() {
        if (Input.GetKeyDown(Services.Config.ToggleKey.Value)) { IntelPanel.Toggle(); }
        _accumulatedSeconds += Time.deltaTime;
        if (_accumulatedSeconds >= Mathf.Clamp(Services.Config.RefreshSeconds.Value, 1f, 30f) && !InFlight) {
            _accumulatedSeconds = 0f;
            Refresh().Forget();
        }
    }

    internal event Action? Changed;

    internal static void Ensure() {
        if (Instance != null) { return; }
        var gameObject = new GameObject(nameof(IntelController)) { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(gameObject);
        Instance = gameObject.AddComponent<IntelController>();
        Plugin.Log.LogInfo("AI Player Intel controller created.");
    }

    internal void ForceRefresh() {
        if (InFlight) { return; }
        _accumulatedSeconds = 0f;
        Refresh().Forget();
    }

    async UniTaskVoid Refresh() {
        InFlight = true;
        try {
            UpdateDiyGate();
            _current = await Collectors.Build(_diyActive);
            Changed?.Invoke();
        } catch (Exception exception) {
            Plugin.Log.LogWarning($"AI Player Intel refresh failed: {exception.Message}");
        } finally {
            InFlight = false;
        }
    }

    void UpdateDiyGate() {
        var gameManager = MonoBehaviourSingleton<GameManager>.Instance;
        var blocked = gameManager != null && gameManager.blockCheckCanPLanMissionForNotPlayer;
        var active = !blocked;
        if (active != _diyActive) {
            Plugin.Log.LogInfo(
                $"AI Player Intel: DIY valuation {(active ? "enabled" : "disabled")} "
                + $"(blocked={blocked})."
            );
        }
        _diyActive = active;
    }
}
