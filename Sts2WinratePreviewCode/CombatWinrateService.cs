using System;
using System.Threading.Tasks;

namespace Sts2WinratePreview;

/// <summary>
/// In-combat "win from here" estimate. Reads the live board (CombatStateReader),
/// runs ONE helper query (search policy, EXACT-restored to the current turn —
/// player HP/powers/hand/discard/energy/block + enemy HP/powers/pending intent),
/// and publishes the win %. Re-queries only when the board fingerprint changes (a
/// card played, energy spent, HP moved, an intent rolled), debounced by the panel
/// so a rapid multi-card turn fires one query, not one per card.
/// Off the main thread; the UI marshals via <see cref="Changed"/>.
/// </summary>
public sealed class CombatWinrateService
{
    public static CombatWinrateService Instance { get; } = new();

    public event Action? Changed;

    // Few trials — this runs DURING the live fight, so it must stay cheap. The
    // parallel helper pool spreads them; 4 keeps it ~sub-second while giving a
    // 5-level number. Override via STS2_WINRATE_COMBAT_TRIALS.
    public int Trials { get; } = EnvInt("STS2_WINRATE_COMBAT_TRIALS", 4);

    public sealed class Result
    {
        public bool Pending = true;
        public bool Ok;
        public double Winrate = -1;   // 0..1
        public int Wins, N;
        public int WinHpPct = -1;     // avg remaining HP% on a win
        public string? RecommendText; // "▶ <card> → enemy" — best opening move from here
        public string? Error;
    }

    private readonly object _lock = new();
    private string _displayedFp = "";
    private string? _pendingFp;
    private CombatStateReader.CombatSnap? _pendingSnap;
    private bool _running;
    private Result _result = new();

    private CombatWinrateService() { }

    public Result Current { get { lock (_lock) return _result; } }
    public bool HasResult { get { lock (_lock) return !_result.Pending; } }

    /// Read the live board and (re)compute if it changed. Cheap and idempotent —
    /// the panel calls this (debounced) as the board changes.
    public void Recompute()
    {
        if (WinratePreviewService.IsInCombat() == false) { Clear(); return; }
        if (!CombatStateReader.TryRead(out var snap)) { Clear(); return; }

        bool start = false;
        lock (_lock)
        {
            if (snap.Fingerprint == _displayedFp) return;  // already shown
            if (snap.Fingerprint == _pendingFp) return;    // already queued / in flight
            _pendingFp = snap.Fingerprint;
            _pendingSnap = snap;
            _result = new Result { Pending = true };
            if (!_running) { _running = true; start = true; }
        }
        RaiseChanged();
        if (start) Task.Run(Worker);
    }

    public void Clear()
    {
        bool changed;
        lock (_lock)
        {
            changed = !_result.Pending || _displayedFp.Length > 0 || _pendingFp != null;
            _result = new Result();
            _displayedFp = "";
            _pendingFp = null;
            _pendingSnap = null;
        }
        if (changed) RaiseChanged();
    }

    private void Worker()
    {
        while (true)
        {
            CombatStateReader.CombatSnap snap;
            string fp;
            lock (_lock)
            {
                if (_pendingSnap == null) { _running = false; return; }
                snap = _pendingSnap;
                fp = _pendingFp!;
                _pendingSnap = null;
            }

            var req = new WinrateHelperClient.WinrateRequest
            {
                character = snap.Character,
                deck = snap.Deck,
                relics = snap.Relics,
                potions = snap.Potions,
                encounter = snap.Encounter,
                trials = Math.Max(1, Trials),
                seed = "combat",
                startHp = snap.CurrentHp,
                startMaxHp = snap.MaxHp,
                enemyHps = snap.EnemyHps,
                playerPowers = snap.PlayerPowers,
                enemyPowers = snap.EnemyPowers,
                // EXACT restore ("완전"): resume the real turn so the chip reflects
                // "die this turn" instead of a fresh-draw approximation.
                hand = snap.Hand,
                discard = snap.Discard,
                energy = snap.Energy,
                block = snap.Block,
                enemyMoves = snap.EnemyMoves,
                decision = "search",   // the live-combat path is the rollout search
            };
            var r = WinrateHelperClient.Instance.Query(req);

            if (!r.Ok)
                MainFile.Logger.Warn($"[{MainFile.ModId}] combat {snap.Encounter}: FAILED — {r.error ?? "no result"}");
            else
                MainFile.Logger.Info($"[{MainFile.ModId}] combat {snap.Encounter} "
                    + $"(hp {snap.CurrentHp}/{snap.MaxHp}, enemies [{string.Join(",", snap.EnemyHps)}]): "
                    + $"{r.winrate * 100:0}% ({r.wins}/{r.n})");

            string? recText = BuildRecommendText(r, snap);

            lock (_lock)
            {
                if (_pendingSnap == null && _pendingFp == fp)
                {
                    _result = r.Ok
                        ? new Result { Pending = false, Ok = true, Winrate = r.winrate, Wins = r.wins, N = r.n, WinHpPct = r.winHpPct, RecommendText = recText }
                        : new Result { Pending = false, Ok = false, Error = r.error ?? "error" };
                    _displayedFp = fp;
                    _pendingFp = null;
                    _running = false;
                    RaiseChanged();
                    return;
                }
                // else: a newer board is queued — loop and process it.
            }
            RaiseChanged();
        }
    }

    /// Turn the helper's recommendation into a chip line: "▶ <card name> → enemy N".
    /// The card name comes from the live hand (snap.CardNames); the target arrow is
    /// only shown when there's more than one enemy to disambiguate.
    private static string? BuildRecommendText(WinrateHelperClient.WinrateResult r, CombatStateReader.CombatSnap snap)
    {
        if (!r.Ok) return null;
        if (string.Equals(r.recKind, "end", StringComparison.Ordinal))
            return Localization.Strings.Get("recommend", Localization.Strings.Get("end_turn"));

        bool isPotion = string.Equals(r.recKind, "potion", StringComparison.Ordinal);
        bool isPlay = string.Equals(r.recKind, "play", StringComparison.Ordinal);
        if ((!isPlay && !isPotion) || string.IsNullOrEmpty(r.recCard)) return null;

        var names = isPotion ? snap.PotionNames : snap.CardNames;
        string name = names.TryGetValue(r.recCard!, out var nm) && !string.IsNullOrEmpty(nm) ? nm : r.recCard!;
        // A potion gets a flask glyph so it reads as "drink", not "play a card".
        if (isPotion) name = "⚗ " + name;
        if (snap.EnemyHps.Count > 1 && r.recTarget >= 0)
            name += " " + Localization.Strings.Get("enemy_arrow", r.recTarget + 1);
        return Localization.Strings.Get("recommend", name);
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) { MainFile.Logger.Warn($"[{MainFile.ModId}] combat Changed threw: {ex.Message}"); }
    }

    private static int EnvInt(string key, int dflt)
        => int.TryParse(System.Environment.GetEnvironmentVariable(key), out var v) && v > 0 ? v : dflt;
}
