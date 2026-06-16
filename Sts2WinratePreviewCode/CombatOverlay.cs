using System;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using Sts2WinratePreview.Localization;

namespace Sts2WinratePreview;

/// <summary>
/// Persistent overlay node (added once under the scene root, mirroring the sibling
/// combat-advisor) that shows the in-combat "win from here" chip while a fight is
/// the foreground screen. Polls a cheap board signature each frame and, when it
/// settles, asks <see cref="CombatWinrateService"/> for a fresh estimate — so it
/// updates after you act without firing a heavy sim on every single card.
/// </summary>
internal sealed partial class CombatOverlay : Node
{
    private const string NodeName = "Sts2WinratePreview_CombatOverlay";
    private const double DebounceSec = 0.4;   // wait for the board to settle before re-querying

    private static readonly Color SafeColor = new(0.24f, 0.62f, 0.28f, 0.95f);
    private static readonly Color RiskyColor = new(0.85f, 0.58f, 0.07f, 0.95f);
    private static readonly Color LethalColor = new(0.78f, 0.18f, 0.15f, 0.95f);
    private static readonly Color PendingColor = new(0.45f, 0.48f, 0.55f, 0.95f);

    private CanvasLayer _layer = null!;
    private CombatChip _chip = null!;

    private const double PollSec = 0.15;      // throttle the (reflection) board read

    private volatile bool _dirty;
    private string _lastSig = "";
    private double _stableFor;
    private double _pollTimer;
    private bool _wasVisible;

    /// Add the overlay once, under the scene-tree root. Safe no-op if already there.
    public static void Install()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null) return;
        if (tree.Root.GetNodeOrNull(NodeName) != null) return;
        var n = new CombatOverlay { Name = NodeName };
        tree.Root.CallDeferred("add_child", n);
    }

    public override void _Ready()
    {
        _layer = new CanvasLayer { Layer = 64 };
        AddChild(_layer);

        // Draggable chip (CombatChip owns its position + persistence).
        _chip = new CombatChip();
        _layer.AddChild(_chip);
        _chip.Visible = false;
        _chip.SetVisual(Strings.Get("calc"), PendingColor);

        CombatWinrateService.Instance.Changed += OnServiceChanged;
    }

    public override void _ExitTree()
    {
        CombatWinrateService.Instance.Changed -= OnServiceChanged;
    }

    private void OnServiceChanged() => _dirty = true;

    public override void _Process(double delta)
    {
        bool show = InCombatForeground();
        if (show != _wasVisible)
        {
            _wasVisible = show;
            _chip.Visible = show;
            if (!show) { CombatWinrateService.Instance.Clear(); _lastSig = ""; _stableFor = 0; }
        }
        if (!show) return;

        // Poll the cheap board signature on an interval (not per frame — it reads
        // run state via reflection). When it settles for DebounceSec, ask for a
        // fresh estimate so a rapid multi-card turn fires one query, not one each.
        _pollTimer += delta;
        if (_pollTimer >= PollSec)
        {
            _pollTimer = 0;
            string sig = CombatStateReader.CheapSig();
            if (sig != _lastSig)
            {
                _lastSig = sig;
                _stableFor = 0;
            }
            else
            {
                double prev = _stableFor;
                _stableFor += PollSec;
                if (prev < DebounceSec && _stableFor >= DebounceSec)
                    CombatWinrateService.Instance.Recompute();
            }
        }

        if (_dirty) { _dirty = false; Apply(); }
    }

    private void Apply()
    {
        var r = CombatWinrateService.Instance.Current;
        if (r.Pending)
        {
            _chip.SetVisual(Strings.Get("calc"), PendingColor);
            return;
        }
        if (!r.Ok || r.Winrate < 0)
        {
            _chip.SetVisual("—", PendingColor);
            return;
        }
        int pct = (int)Math.Round(r.Winrate * 100);
        var svc = WinratePreviewService.Instance;
        (string word, Color color) = r.Winrate >= svc.SafeThreshold
            ? (Strings.Get("band_easy"), SafeColor)
            : r.Winrate >= svc.RiskyThreshold
                ? (Strings.Get("band_normal"), RiskyColor)
                : (Strings.Get("band_hard"), LethalColor);
        string text = $"{word} {pct}%";
        if (r.WinHpPct >= 0) text += $" · {Strings.Get("qual")}{r.WinHpPct}%";
        // Second line: the best move to play from here (search's opening pick).
        if (!string.IsNullOrEmpty(r.RecommendText)) text += $"\n{r.RecommendText}";
        _chip.SetVisual(text, color);
    }

    private static bool InCombatForeground()
    {
        try
        {
            if (CombatManager.Instance?.IsInProgress != true) return false;
            return ActiveScreenContext.Instance?.GetCurrentScreen() is NCombatRoom;
        }
        catch { return false; }
    }
}
