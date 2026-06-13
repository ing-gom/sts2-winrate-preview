using Godot;

namespace Sts2WinratePreview;

/// <summary>
/// Loads the same map sprites the game uses for Monster / Elite nodes, plus a
/// best-effort boss portrait (the placeholder png NBossMapPoint itself falls
/// back to). Boss fallback chain: portrait png → map_boss icon → elite icon.
/// </summary>
internal static class MapIconLoader
{
    private const string MonsterPath = "res://images/atlases/ui_atlas.sprites/map/icons/map_monster.tres";
    private const string ElitePath   = "res://images/atlases/ui_atlas.sprites/map/icons/map_elite.tres";
    // The game's own boss glyph (score screen). Map boss nodes are spine-animated
    // (no static map icon exists), so this is the best native static asset.
    private const string BossPath    = "res://images/ui/game_over_screen/score_boss.png";

    private static Texture2D? _monster;
    private static Texture2D? _elite;
    private static Texture2D? _boss;
    private static string? _bossPortraitPath;
    private static Texture2D? _bossPortrait;

    public static Texture2D? Monster() =>
        _monster ??= ResourceLoader.Load<Texture2D>(MonsterPath, null, ResourceLoader.CacheMode.Reuse);

    public static Texture2D? Elite() =>
        _elite ??= ResourceLoader.Load<Texture2D>(ElitePath, null, ResourceLoader.CacheMode.Reuse);

    public static Texture2D? Boss(string? portraitPng)
    {
        // Boss glyph first — most bosses are spine-animated on the map, so the
        // per-boss placeholder png usually does NOT exist.
        if (_boss == null && ResourceLoader.Exists(BossPath))
            _boss = ResourceLoader.Load<Texture2D>(BossPath, null, ResourceLoader.CacheMode.Reuse);
        if (_boss != null) return _boss;

        if (!string.IsNullOrEmpty(portraitPng))
        {
            if (portraitPng != _bossPortraitPath)
            {
                _bossPortraitPath = portraitPng;
                _bossPortrait = ResourceLoader.Exists(portraitPng)
                    ? ResourceLoader.Load<Texture2D>(portraitPng, null, ResourceLoader.CacheMode.Reuse)
                    : null;
            }
            if (_bossPortrait != null) return _bossPortrait;
        }
        return Elite();
    }
}
