using System;
using Godot;

namespace Sts2WinratePreview;

/// <summary>
/// The draggable in-combat win-rate chip. Mirrors MapPreviewPanel's drag logic:
/// left-drag anywhere on the chip to reposition; the offset persists to user://
/// (separate file from the map panel so the two positions don't clash).
/// </summary>
internal sealed partial class CombatChip : PanelContainer
{
    private const string ConfigPath = "user://sts2_winrate_preview_combat.cfg";

    private StyleBoxFlat _style = null!;
    private Label _label = null!;
    private bool _dragging;

    public override void _Ready()
    {
        // Stop so the chip itself receives the drag; the label stays Ignore so the
        // whole surface drags. The chip is the only Control on its CanvasLayer, so
        // clicks elsewhere on screen still reach the game.
        MouseFilter = MouseFilterEnum.Stop;

        // Top-center by default, just below the run top bar; a zero-size rect at the
        // anchor that Grow expands to content size (same trick as the map panel), so
        // dragging all four offsets together moves it cleanly.
        AnchorLeft = AnchorRight = 0.5f;
        AnchorTop = AnchorBottom = 0f;
        OffsetLeft = OffsetRight = 0;
        OffsetTop = OffsetBottom = 120;
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.End;

        _style = new StyleBoxFlat
        {
            BgColor = new Color(0.45f, 0.48f, 0.55f, 0.95f),
            BorderColor = new Color(0.35f, 0.38f, 0.45f, 0.85f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 5, ContentMarginBottom = 6,
        };
        AddThemeStyleboxOverride("panel", _style);

        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", 18);
        _label.AddThemeColorOverride("font_color", Colors.White);
        _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
        _label.AddThemeConstantOverride("outline_size", 3);
        AddChild(_label);

        LoadPosition();
    }

    public void SetVisual(string text, Color bg)
    {
        if (_label.Text != text) _label.Text = text;
        if (_style.BgColor != bg) _style.BgColor = bg;
    }

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
            cf.Load(ConfigPath);   // preserve any other keys
            cf.SetValue("chip", "offset_x", OffsetLeft);
            cf.SetValue("chip", "offset_y", OffsetTop);
            cf.Save(ConfigPath);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] combat chip pos save failed: {ex.Message}");
        }
    }

    private void LoadPosition()
    {
        try
        {
            var cf = new ConfigFile();
            if (cf.Load(ConfigPath) != Error.Ok) return;
            float x = (float)cf.GetValue("chip", "offset_x", OffsetLeft).AsDouble();
            float y = (float)cf.GetValue("chip", "offset_y", OffsetTop).AsDouble();
            OffsetLeft = OffsetRight = x;
            OffsetTop = OffsetBottom = y;
        }
        catch
        {
            // corrupt / missing — keep the default top-center position.
        }
    }
}
