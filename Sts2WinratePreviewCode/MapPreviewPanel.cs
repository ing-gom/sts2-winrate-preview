using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using Sts2WinratePreview.Localization;

namespace Sts2WinratePreview;

/// <summary>
/// Floating panel on the map screen: one row per upcoming encounter category
/// (Monster / Elite / current-act Boss) showing the game's own node icon plus a
/// difficulty band (Easy / Normal / Hard, localized). Added as a child of NMapScreen so visibility
/// follows the screen automatically; modal popups (pause, settings…) are
/// suppressed by polling, mirroring Sts2PotionDropChance's overlay watcher.
/// </summary>
internal sealed partial class MapPreviewPanel : PanelContainer
{
    public const string NodeName = "Sts2WinratePreview_Panel";

    // Native-look palette (mirrors Sts2CardAdvisor AxisLegendPanel).
    private static readonly Color BgColor = new(0.08f, 0.09f, 0.13f, 0.95f);
    private static readonly Color BorderColor = new(0.35f, 0.38f, 0.45f, 0.85f);
    private static readonly Color HeaderColor = new(1.00f, 0.93f, 0.70f);
    private static readonly Color TextColor = new(0.93f, 0.95f, 1.00f);

    private static readonly Color SafeColor = new(0.24f, 0.62f, 0.28f, 0.95f);
    private static readonly Color RiskyColor = new(0.85f, 0.58f, 0.07f, 0.95f);
    private static readonly Color LethalColor = new(0.78f, 0.18f, 0.15f, 0.95f);
    private static readonly Color PendingColor = new(0.45f, 0.48f, 0.55f, 0.95f);

    private const int IconSize = 30;
    private const int FontSize = 18;

    private const string ConfigPath = "user://sts2_winrate_preview.cfg";

    private VBoxContainer _rows = null!;
    private Label _header = null!;
    private readonly HashSet<NSubmenu> _submenus = new();
    private bool _suppressed;
    private bool _dragging;

    public override void _Ready()
    {
        Name = NodeName;
        // Stop (not Ignore): the panel itself must receive mouse input so the
        // user can drag it. Children stay Ignore so the whole surface drags.
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 10;

        // Top-right, below the run top bar. Anchored so viewport resizes track.
        // A user-dragged position (saved offsets) overrides the default.
        AnchorLeft = AnchorRight = 1f;
        AnchorTop = AnchorBottom = 0f;
        OffsetLeft = -30;   // grows leftward from the right edge
        OffsetRight = -30;
        OffsetTop = 170;
        OffsetBottom = 170;
        GrowHorizontal = GrowDirection.Begin;
        GrowVertical = GrowDirection.End;
        LoadPosition();

        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = BgColor,
            BorderColor = BorderColor,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 8, ContentMarginBottom = 10,
        });

        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.AddThemeConstantOverride("separation", 6);
        AddChild(vbox);

        _header = new Label
        {
            Text = Strings.Get("header"),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _header.AddThemeFontSizeOverride("font_size", 15);
        _header.AddThemeColorOverride("font_color", HeaderColor);
        vbox.AddChild(_header);

        _rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _rows.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(_rows);

        // Modal bookkeeping (NSubmenu instances appear after us — track additions).
        var tree = GetTree();
        if (tree != null)
        {
            tree.NodeAdded += OnNodeAdded;
            ScanForSubmenus(tree.Root);
        }

        WinratePreviewService.Instance.Changed += OnServiceChanged;
        RebuildRows();
    }

    public override void _ExitTree()
    {
        WinratePreviewService.Instance.Changed -= OnServiceChanged;
        var tree = GetTree();
        if (tree != null) tree.NodeAdded -= OnNodeAdded;
    }

    // Changed fires off the main thread — marshal via deferred call.
    private void OnServiceChanged() => CallDeferred(nameof(RebuildRows));

    // ---- drag to reposition (left-drag anywhere on the panel; saved to user://) ----

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                if (!mb.Pressed) SavePosition();
                AcceptEvent();
                break;
            case InputEventMouseMotion mm when _dragging:
                OffsetLeft += mm.Relative.X;
                OffsetRight += mm.Relative.X;
                OffsetTop += mm.Relative.Y;
                OffsetBottom += mm.Relative.Y;
                AcceptEvent();
                break;
        }
    }

    private void SavePosition()
    {
        try
        {
            var cf = new ConfigFile();
            cf.SetValue("panel", "offset_x", OffsetLeft);
            cf.SetValue("panel", "offset_y", OffsetTop);
            cf.Save(ConfigPath);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] position save failed: {ex.Message}");
        }
    }

    private void LoadPosition()
    {
        try
        {
            var cf = new ConfigFile();
            if (cf.Load(ConfigPath) != Error.Ok) return;
            float x = (float)cf.GetValue("panel", "offset_x", OffsetLeft).AsDouble();
            float y = (float)cf.GetValue("panel", "offset_y", OffsetTop).AsDouble();
            OffsetLeft = OffsetRight = x;
            OffsetTop = OffsetBottom = y;
        }
        catch
        {
            // corrupt/missing config — keep defaults.
        }
    }

    public void RebuildRows()
    {
        if (!IsInstanceValid(this)) return;
        _header.Text = Strings.Get("header");   // refresh in case the locale changed
        // perf-guard: GetChild(i), not GetChildren() (avoids per-call array alloc).
        int n = _rows.GetChildCount();
        for (int i = 0; i < n; i++) _rows.GetChild(i)?.QueueFree();

        var bands = WinratePreviewService.Instance.Bands;
        Visible = bands.Count > 0 && !_suppressed;

        var tip = new System.Text.StringBuilder();
        foreach (var b in bands)
        {
            _rows.AddChild(BuildRow(b));
            if (!b.Pending && b.Error == null && b.DisplayPct >= 0)
            {
                // Win rate headline + combat-quality (win × remaining-HP) 병기.
                tip.Append(Strings.Get("tip_quality", KindLabel(b.Kind), b.DisplayPct,
                    b.QualPct >= 0 ? b.QualPct : 0));
                if (b.WorstEncounter != null && b.Total > 1)
                    tip.Append(Strings.Get("tip_hardest", b.WorstEncounter));
                // The rate is over simulatable encounters only — if some failed,
                // say so, since that can bias the aggregate (excluded ≠ 0%).
                if (b.Failed > 0)
                    tip.Append(Strings.Get("tip_failed", b.Failed, b.Total));
                tip.Append('\n');
            }
        }
        TooltipText = tip.ToString().TrimEnd();
    }

    private static string KindLabel(string kind) => kind switch
    {
        "Elite" => Strings.Get("kind_elite"),
        "Boss" => Strings.Get("kind_boss"),
        _ => Strings.Get("kind_monster"),
    };

    private Control BuildRow(WinratePreviewService.TargetBand b)
    {
        var hbox = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        hbox.AddThemeConstantOverride("separation", 8);

        var icon = b.Kind switch
        {
            "Elite" => MapIconLoader.Elite(),
            "Boss" => MapIconLoader.Boss(WinratePreviewService.Instance.BossIconPng),
            _ => MapIconLoader.Monster(),
        };
        if (icon != null)
        {
            hbox.AddChild(new TextureRect
            {
                Texture = icon,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(IconSize, IconSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }

        var kindLabel = new Label
        {
            Text = KindLabel(b.Kind),
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(64, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        kindLabel.AddThemeFontSizeOverride("font_size", FontSize);
        kindLabel.AddThemeColorOverride("font_color", TextColor);
        hbox.AddChild(kindLabel);

        var (text, color) = BandVisual(b);
        var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        chip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 2, ContentMarginBottom = 2,
        });
        var chipLabel = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(96, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        chipLabel.AddThemeFontSizeOverride("font_size", FontSize);
        chipLabel.AddThemeColorOverride("font_color", Colors.White);
        chipLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
        chipLabel.AddThemeConstantOverride("outline_size", 3);
        chip.AddChild(chipLabel);
        hbox.AddChild(chip);

        return hbox;
    }

    private static (string, Color) BandVisual(WinratePreviewService.TargetBand b)
    {
        // Nothing back yet → show progress (N/M) so the user sees it's working.
        // No successful result yet → show the count climbing (0/N → …). With the
        // parallel pool, Done jumps in bursts of PoolSize, but it always advances
        // monotonically so the user sees progress instead of a frozen "0/N".
        if (b.Pending)
        {
            string p = b.Total > 1 ? Strings.Get("calc_progress", b.Done, b.Total) : Strings.Get("calc");
            return (p, PendingColor);
        }
        if (b.Error != null) return ("—", PendingColor);
        // Difficulty framing (Easy / Normal / Hard) reads more intuitively than
        // safe/risky/lethal next to the win-rate %; high win rate = easy fight.
        var (word, color) = b.Band switch
        {
            Band.Safe => (Strings.Get("band_easy"), SafeColor),
            Band.Risky => (Strings.Get("band_normal"), RiskyColor),
            Band.Lethal => (Strings.Get("band_hard"), LethalColor),
            _ => ("—", PendingColor),
        };
        // Main number = win rate (risk band). Combat-quality (remaining HP% on a
        // win) is shown as a separate compact "·품질NN%" — NOT multiplied into the
        // win rate, so a safe-but-costly fight stays "안전" instead of being
        // dragged down to look dangerous.
        int pct = b.DisplayPct >= 0 ? b.DisplayPct : (int)Math.Round(b.Winrate * 100);
        string text = $"{word} {pct}%";
        if (b.QualPct >= 0) text += $" · {Strings.Get("qual")}{b.QualPct}%";
        if (b.Done < b.Total) text += $" {b.Done}/{b.Total}";
        return (text, color);
    }

    // ---- modal suppression (mirrors MapBadgeOverlayWatcher) ----

    private void OnNodeAdded(Node n)
    {
        if (n is NSubmenu sm) _submenus.Add(sm);
    }

    private void ScanForSubmenus(Node n)
    {
        if (n is NSubmenu sm) _submenus.Add(sm);
        int count = n.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            var c = n.GetChild(i);
            if (c != null) ScanForSubmenus(c);
        }
    }

    public override void _Process(double delta)
    {
        bool suppress;
        try { suppress = ShouldSuppress(); }
        catch { suppress = false; }
        if (suppress == _suppressed) return;
        _suppressed = suppress;
        Visible = !_suppressed && WinratePreviewService.Instance.HasBands;
    }

    private bool ShouldSuppress()
    {
        var modal = NModalContainer.Instance;
        if (modal != null && modal.OpenModal != null) return true;
        _submenus.RemoveWhere(s => !IsInstanceValid(s));
        foreach (var sm in _submenus)
            if (sm.IsVisibleInTree()) return true;
        return false;
    }
}
