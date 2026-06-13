using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2WinratePreview;

/// <summary>
/// Manages a POOL of long-lived headless helper processes
/// (`Sts2CombatCore.exe --server`) and talks to each over line-delimited JSON.
/// Each helper boots the real STS2 combat engine + ModelDb ONCE and then answers
/// many win-rate queries at pure combat cost. All combat happens in separate
/// processes, so the live game's run state can never be mutated (read-only by
/// construction).
///
/// The pool lets the preview service run several encounter queries concurrently
/// — each helper is an independent process (independent CombatManager singleton),
/// so K helpers ≈ K× throughput at K× memory. Pool size: env
/// STS2_WINRATE_HELPERS (default 3).
/// </summary>
public sealed class WinrateHelperClient : IDisposable
{
    public static WinrateHelperClient Instance { get; } = new();

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public int PoolSize { get; } = EnvInt("STS2_WINRATE_HELPERS", 3, min: 1, max: 8);

    /// Per-query wall-clock ceiling. A boss with many trials can legitimately take
    /// a few seconds; beyond this we treat the helper as hung and recycle it.
    public int QueryTimeoutMs { get; set; } = 30_000;

    private readonly List<Helper> _helpers = new();
    private BlockingCollection<Helper>? _free;   // available (idle) helpers
    private bool _initialized;
    private readonly object _initLock = new();
    private string? _exePath;

    private WinrateHelperClient() { }

    // ---- request / response DTOs (wire format mirrors WinrateQuery.Request) ----

    public sealed class WinrateRequest
    {
        public string op { get; set; } = "winrate";
        public string character { get; set; } = "Ironclad";
        public List<string> deck { get; set; } = new();
        public List<string> relics { get; set; } = new();
        public List<string> potions { get; set; } = new();
        public string encounter { get; set; } = "";
        public int trials { get; set; } = 3;
        public bool forcePotions { get; set; }
        public string? seed { get; set; }
        public int startHp { get; set; }      // 0 = full HP
        public int startMaxHp { get; set; }   // 0 = character default
    }

    public sealed class WinrateResult
    {
        public double winrate { get; set; }
        public int wins { get; set; }
        public int n { get; set; }
        public double ci95 { get; set; }
        public string encounter { get; set; } = "";
        public int deckSize { get; set; }
        public int peakEnemies { get; set; }
        public double seconds { get; set; }
        public string? error { get; set; }
        public List<string>? unknownCards { get; set; }
        public int failedTrials { get; set; }
        public int enemyShavedPct { get; set; }   // avg % of enemy HP removed (win=100%)
        public int winHpPct { get; set; } = -1;    // avg remaining HP% on wins (-1 = no wins)

        public bool Ok => string.IsNullOrEmpty(error) && n > 0;
    }

    /// <summary>
    /// Run one win-rate query on any free helper (blocks until one is available
    /// and has answered, or the timeout trips). Thread-safe — call concurrently
    /// from up to <see cref="PoolSize"/> threads for parallel throughput. Never
    /// throws; returns a result with <c>error</c> set on failure.
    /// </summary>
    public WinrateResult Query(WinrateRequest req)
    {
        BlockingCollection<Helper>? free;
        lock (_initLock)
        {
            if (!EnsurePool(out var err))
                return new WinrateResult { error = err ?? "pool not started", encounter = req.encounter };
            free = _free;
        }
        if (free == null)
            return new WinrateResult { error = "pool unavailable", encounter = req.encounter };

        Helper helper = free.Take();   // wait for an idle helper
        try
        {
            return helper.Query(req, _json, QueryTimeoutMs, ResolveExePath());
        }
        finally
        {
            free.Add(helper);          // return to the pool even on failure
        }
    }

    // ---- pool lifecycle ----

    private bool EnsurePool(out string? error)
    {
        error = null;
        if (_initialized) return true;

        string? exe = ResolveExePath();
        if (exe == null)
        {
            error = "Sts2CombatCore.exe helper not found (set STS2_WINRATE_HELPER or co-locate under <mod>/helper/).";
            MainFile.Logger.Warn($"[{MainFile.ModId}] {error}");
            return false;
        }

        _free = new BlockingCollection<Helper>(new ConcurrentQueue<Helper>());
        for (int i = 0; i < PoolSize; i++)
        {
            var h = new Helper(i);
            _helpers.Add(h);
            _free.Add(h);
        }
        _initialized = true;
        MainFile.Logger.Info($"[{MainFile.ModId}] helper pool ready ({PoolSize} process slot(s), lazily started).");
        return true;
    }

    private string? ResolveExePath()
    {
        if (_exePath != null && File.Exists(_exePath)) return _exePath;
        foreach (var cand in EnumerateCandidates())
            if (!string.IsNullOrEmpty(cand) && File.Exists(cand)) { _exePath = cand; return cand; }
        return null;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        var env = Environment.GetEnvironmentVariable("STS2_WINRATE_HELPER");
        if (!string.IsNullOrEmpty(env)) yield return env;

        string? modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(modDir))
            yield return Path.Combine(modDir, "helper", "Sts2CombatCore.exe");

        yield return @"C:\Users\kl95\sts2-combat-core\src\Sts2CombatCore\.godot\mono\temp\bin\Release\Sts2CombatCore.exe";
    }

    public void Dispose()
    {
        lock (_initLock)
        {
            foreach (var h in _helpers) h.Kill();
            _helpers.Clear();
            _free?.Dispose();
            _free = null;
            _initialized = false;
        }
    }

    private static int EnvInt(string key, int dflt, int min, int max)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable(key), out var v))
            return Math.Clamp(v, min, max);
        return dflt;
    }

    /// <summary>One pooled helper process + its own I/O. Used by exactly one
    /// thread at a time (checkout/return via the free collection).</summary>
    private sealed class Helper
    {
        private readonly int _id;
        private Process? _proc;
        private StreamWriter? _stdin;
        private StreamReader? _stdout;

        public Helper(int id) => _id = id;

        public WinrateResult Query(WinrateRequest req, JsonSerializerOptions json, int timeoutMs, string? exe)
        {
            try
            {
                if (!EnsureStarted(exe, out var startErr))
                    return new WinrateResult { error = startErr ?? "helper not started", encounter = req.encounter };

                string line = JsonSerializer.Serialize(req, json);
                var task = Task.Run(() => Exchange(line));
                if (!task.Wait(timeoutMs))
                {
                    MainFile.Logger.Warn($"[{MainFile.ModId}] helper#{_id} query timed out ({timeoutMs}ms) — recycling.");
                    Kill();
                    return new WinrateResult { error = "timeout", encounter = req.encounter };
                }

                string? resp = task.Result;
                if (resp == null) { Kill(); return new WinrateResult { error = "helper closed stream", encounter = req.encounter }; }

                var result = JsonSerializer.Deserialize<WinrateResult>(resp, json)
                             ?? new WinrateResult { error = "null response" };
                if (string.IsNullOrEmpty(result.encounter)) result.encounter = req.encounter;
                return result;
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[{MainFile.ModId}] helper#{_id} query failed: {ex.GetType().Name}: {ex.Message}");
                Kill();
                return new WinrateResult { error = ex.Message, encounter = req.encounter };
            }
        }

        private string? Exchange(string requestLine)
        {
            _stdin!.WriteLine(requestLine);
            _stdin.Flush();
            return _stdout!.ReadLine();
        }

        private bool EnsureStarted(string? exe, out string? error)
        {
            error = null;
            if (_proc is { HasExited: false } && _stdin != null && _stdout != null) return true;
            Kill();

            if (exe == null) { error = "helper exe not found"; return false; }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--server",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
            };
            try
            {
                _proc = Process.Start(psi);
                if (_proc == null) { error = "Process.Start returned null"; return false; }
                _stdin = _proc.StandardInput;
                _stdout = _proc.StandardOutput;
                _proc.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        MainFile.Logger.Info($"[{MainFile.ModId}][helper#{_id}] {e.Data}");
                };
                _proc.BeginErrorReadLine();
                MainFile.Logger.Info($"[{MainFile.ModId}] helper#{_id} started (pid {_proc.Id}).");
                return true;
            }
            catch (Exception ex)
            {
                error = $"failed to start helper#{_id}: {ex.Message}";
                MainFile.Logger.Warn($"[{MainFile.ModId}] {error}");
                Kill();
                return false;
            }
        }

        public void Kill()
        {
            try { _stdin?.Dispose(); } catch { }
            try { if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true); } catch { }
            try { _proc?.Dispose(); } catch { }
            _proc = null; _stdin = null; _stdout = null;
        }
    }
}
