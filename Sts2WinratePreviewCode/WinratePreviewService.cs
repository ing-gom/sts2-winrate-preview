using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sts2WinratePreview;

public enum Band { Unknown, Safe, Risky, Lethal }

/// How a category's per-encounter win rates collapse into one number.
///   Mean   — expected win rate across the encounters you might face (default,
///            most interpretable). The raw % is shown next to the band so sim
///            fidelity outliers (e.g. Byrdonis over-won) are visible, not hidden.
///   Median — robust to outliers but hides minority losses in small pools
///            (e.g. 2 winnable + 1 lethal elite → median says "safe").
///   Worst  — band of the single hardest encounter (conservative; dragged down
///            by encounters the sim under-models, e.g. Chompers).
public enum AggMode { Worst, Median, Mean }

/// <summary>
/// Orchestrates the win-rate preview: reads the live run state, fires the helper
/// queries OFF the main thread, caches results by a run fingerprint (so we only
/// re-query when deck / relics / potions / act / next-encounters actually change),
/// and publishes results progressively. The map UI calls <see cref="Refresh"/>
/// while the map is visible and listens to <see cref="Changed"/> to redraw.
/// </summary>
public sealed class WinratePreviewService
{
    public static WinratePreviewService Instance { get; } = new();

    /// Raised (possibly off the main thread) whenever <see cref="Bands"/> changes.
    /// UI handlers MUST marshal to the Godot main thread (CallDeferred).
    public event Action? Changed;

    // Monster/Elite bands AGGREGATE over the whole act pool (~10 monsters, ~8
    // elites), so 1 trial per encounter already yields plenty of samples — no
    // need to pay 3× per encounter. The Boss is a single encounter (no aggregation
    // cushion), so 1 trial would make its band binary (win=100% / loss=0%) and a
    // single lucky/unlucky opening draw could flip it. 2 trials gives a 3-level
    // band (0/50/100%) and hedges the single-draw flip, while being 2× faster than
    // the old 4 — the Boss is one of the slowest queries, so this directly speeds
    // the whole pool refresh. Bump back up via STS2_WINRATE_BOSS_TRIALS if desired.
    // Mean aggregates over the whole pool, so 1 trial per encounter already gives
    // a meaningful average (sample size = number of distinct encounters). Worst /
    // Median modes want more per-encounter resolution — raise STS2_WINRATE_TRIALS
    // (e.g. 3) if switching to those.
    public int PoolTrials { get; set; } = EnvInt("STS2_WINRATE_TRIALS", 1);
    public int BossTrials { get; set; } = EnvInt("STS2_WINRATE_BOSS_TRIALS", 2);
    public AggMode Aggregation { get; set; } = EnvAgg("STS2_WINRATE_AGG", AggMode.Mean);
    public double SafeThreshold { get; set; } = EnvDouble("STS2_WINRATE_SAFE", 0.75);
    public double RiskyThreshold { get; set; } = EnvDouble("STS2_WINRATE_RISKY", 0.45);

    /// One AGGREGATE band per encounter category: Monster/Elite combine every
    /// distinct encounter still ahead in this act (winrate = total wins / total
    /// trials across them); Boss is the single known act boss.
    public sealed class TargetBand
    {
        public string Kind = "";       // Monster | Elite | Boss
        public Band Band = Band.Unknown;
        public double Winrate = -1;    // representative win rate per Aggregation mode; -1 until known
        public int N;                  // total trials aggregated so far
        public int Done;               // encounters finished
        public int Total;              // encounters in this category
        public bool Pending = true;    // true until the FIRST result lands
        public string? Error;          // set when every query in the category failed
        public string? WorstEncounter; // hardest encounter driving the band (Worst mode)
        public int Failed;             // encounters that failed to sim (excluded from the rate)
        // Bands are scored on WIN-QUALITY = win rate × remaining-HP% (loss = 0):
        // "expected HP you walk away with". 100 = always win at full HP; lower =
        // wins cost HP or losses occur. Uniform for Monster/Elite/Boss.
        public bool IsProgress;        // retained for compat; always false now
        public int DisplayPct = -1;    // main number shown in the chip = win rate %
        public int QualPct = -1;       // 전투 품질 % = mean(win rate × remaining-HP%) (병기)
    }

    /// Boss portrait png path captured from the last run snapshot (may be null /
    /// non-existent — the UI falls back to a generic icon).
    public string? BossIconPng { get; private set; }

    private readonly object _lock = new();
    private string _displayedFp = "";
    private string? _pendingFp;
    private RunStateReader.RunSnapshot? _pendingSnap;
    private bool _running;
    private List<TargetBand> _bands = new();

    private WinratePreviewService() { }

    public IReadOnlyList<TargetBand> Bands
    {
        get { lock (_lock) return _bands.ToList(); }
    }

    /// Allocation-free emptiness check for per-frame callers (perf-guard).
    public bool HasBands
    {
        get { lock (_lock) return _bands.Count > 0; }
    }

    /// Read the run state and (re)compute bands if anything changed. Cheap and
    /// idempotent — safe to call every time the map screen becomes visible.
    public void Refresh()
    {
        if (!RunStateReader.TryRead(out var snap)) { Clear(); return; }

        bool start = false;
        lock (_lock)
        {
            if (snap.Fingerprint == _displayedFp) return;  // already shown
            if (snap.Fingerprint == _pendingFp) return;    // already queued / in flight

            _pendingFp = snap.Fingerprint;
            _pendingSnap = snap;
            BossIconPng = snap.BossIconPng;
            _bands = BuildAggregates(snap, null);
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
            changed = _bands.Count > 0 || _displayedFp.Length > 0 || _pendingFp != null;
            _bands = new();
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
            RunStateReader.RunSnapshot snap;
            string fp;
            lock (_lock)
            {
                if (_pendingSnap == null) { _running = false; return; }
                snap = _pendingSnap;
                fp = _pendingFp!;
                _pendingSnap = null;   // claim this job
            }

            // results[kind] = per-encounter outcomes accumulated so far. Mutated
            // from multiple pool threads → guarded by _lock on every write.
            var results = new Dictionary<string, List<WinrateHelperClient.WinrateResult>>(StringComparer.Ordinal);

            // Dispatch every encounter query across the helper pool concurrently.
            // Degree = pool size; each thread checks out one helper process.
            var opts = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, WinrateHelperClient.Instance.PoolSize) };
            try
            {
                Parallel.ForEach(snap.Targets, opts, (target, loop) =>
                {
                    // A newer run state arrived — stop spawning more work.
                    lock (_lock) { if (_pendingSnap != null) { loop.Stop(); return; } }
                    if (loop.ShouldExitCurrentIteration) return;

                    var req = new WinrateHelperClient.WinrateRequest
                    {
                        character = snap.Character,
                        deck = snap.Deck,
                        relics = snap.Relics,
                        potions = snap.Potions,
                        encounter = target.Encounter,
                        trials = target.Kind == "Boss" ? BossTrials : PoolTrials,
                        seed = $"preview-a{snap.ActIndex}",
                        startHp = snap.CurrentHp,    // simulate from the player's live HP
                        startMaxHp = snap.MaxHp,
                    };
                    var r = WinrateHelperClient.Instance.Query(req);

                    // Per-encounter diagnostics: log EVERY result so an off-looking
                    // aggregate (e.g. "elite easy" on a starter deck) can be traced
                    // to which encounter drove it and whether others were dropped.
                    if (!r.Ok)
                        MainFile.Logger.Warn($"[{MainFile.ModId}] {target.Kind} {target.Encounter}: FAILED — {r.error ?? "no result"}");
                    else
                        MainFile.Logger.Info($"[{MainFile.ModId}] {target.Kind} {target.Encounter}: {r.winrate * 100:0}% ({r.wins}/{r.n})"
                            + (r.unknownCards is { Count: > 0 } ? $" [dropped: {string.Join(",", r.unknownCards)}]" : ""));

                    // Accumulate + publish a progressive aggregate under the lock.
                    lock (_lock)
                    {
                        if (!results.TryGetValue(target.Kind, out var list))
                            results[target.Kind] = list = new List<WinrateHelperClient.WinrateResult>();
                        list.Add(r);
                        if (_pendingSnap == null && _pendingFp == fp)
                            _bands = BuildAggregates(snap, results);
                    }
                    RaiseChanged();
                });
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[{MainFile.ModId}] parallel dispatch failed: {ex.Message}");
            }

            lock (_lock)
            {
                if (_pendingSnap == null)
                {
                    _displayedFp = fp;
                    _pendingFp = null;
                    _running = false;
                    return;
                }
                // else: a newer job is queued — loop and process it.
            }
        }
    }

    /// One aggregate TargetBand per category present in the snapshot, in the
    /// snapshot's category order (Monster → Elite → Boss). `results` may be null
    /// (initial publish) or partial (progressive refinement).
    private List<TargetBand> BuildAggregates(
        RunStateReader.RunSnapshot snap,
        Dictionary<string, List<WinrateHelperClient.WinrateResult>>? results)
    {
        var bands = new List<TargetBand>();
        foreach (var kindGroup in snap.Targets.GroupBy(t => t.Kind))
        {
            var tb = new TargetBand { Kind = kindGroup.Key, Total = kindGroup.Count() };
            if (results != null && results.TryGetValue(kindGroup.Key, out var list) && list.Count > 0)
            {
                tb.Done = list.Count;
                // Per-encounter WIN RATE (only completed ones) — the band/headline.
                // Remaining-HP% is tracked SEPARATELY as a secondary "cost" number,
                // NOT multiplied in (win × HP undervalues safe-but-costly fights:
                // 95% win @ 40% HP is still a 95% win, not a 38% danger).
                var rates = new List<(double wr, string enc)>();
                int n = 0, errors = 0;
                double qualSum = 0;
                string? firstError = null;
                foreach (var r in list)
                {
                    if (r.Ok)
                    {
                        rates.Add((r.winrate, r.encounter)); n += r.n;
                        // Combat quality per encounter = win rate × remaining-HP fraction
                        // on a win (loss / no-HP-data → 0). Captures "win AND stay healthy".
                        qualSum += r.winHpPct >= 0 ? r.winrate * (r.winHpPct / 100.0) : 0.0;
                    }
                    else { errors++; firstError ??= r.error; }
                }
                tb.Failed = errors;
                if (rates.Count > 0)
                {
                    tb.Pending = false;
                    tb.N = n;
                    var (wr, worstEnc) = Collapse(rates);
                    tb.Winrate = wr;
                    tb.WorstEncounter = worstEnc;
                    tb.DisplayPct = (int)Math.Round(wr * 100);   // main = win rate (band)
                    tb.Band = ToBand(wr);                         // risk band by win rate
                    // 병기 — combat quality = mean(win rate × remaining-HP%).
                    tb.QualPct = (int)Math.Round(100.0 * qualSum / rates.Count);
                }
                else if (errors == tb.Total)
                {
                    // Every encounter in this category failed in the engine —
                    // keep the real reason for the log / tooltip, show "-" in UI.
                    tb.Pending = false;
                    tb.Error = firstError ?? "error";
                }
            }
            bands.Add(tb);
        }
        return bands;
    }

    /// Collapse per-encounter win rates into one representative rate per the
    /// configured Aggregation mode. Returns (rate, worstEncounterName).
    private (double rate, string? worstEnc) Collapse(List<(double wr, string enc)> rates)
    {
        // Always identify the hardest encounter (useful as a label regardless of mode).
        string? worstEnc = null;
        double worst = double.MaxValue;
        foreach (var (wr, enc) in rates)
            if (wr < worst) { worst = wr; worstEnc = enc; }

        switch (Aggregation)
        {
            case AggMode.Worst:
                return (worst, worstEnc);
            case AggMode.Median:
            {
                var sorted = rates.Select(x => x.wr).OrderBy(x => x).ToList();
                int m = sorted.Count;
                double median = (m % 2 == 1) ? sorted[m / 2] : (sorted[m / 2 - 1] + sorted[m / 2]) / 2.0;
                return (median, worstEnc);
            }
            default: // Mean
                return (rates.Average(x => x.wr), worstEnc);
        }
    }

    private Band ToBand(double wr)
        => wr >= SafeThreshold ? Band.Safe
         : wr >= RiskyThreshold ? Band.Risky
         : Band.Lethal;

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) { MainFile.Logger.Warn($"[{MainFile.ModId}] Changed handler threw: {ex.Message}"); }
    }

    private static int EnvInt(string key, int dflt)
        => int.TryParse(System.Environment.GetEnvironmentVariable(key), out var v) && v > 0 ? v : dflt;

    private static double EnvDouble(string key, double dflt)
        => double.TryParse(System.Environment.GetEnvironmentVariable(key), out var v) ? v : dflt;

    private static AggMode EnvAgg(string key, AggMode dflt)
        => System.Environment.GetEnvironmentVariable(key)?.ToLowerInvariant() switch
        {
            "worst" => AggMode.Worst,
            "median" => AggMode.Median,
            "mean" => AggMode.Mean,
            _ => dflt,
        };
}
