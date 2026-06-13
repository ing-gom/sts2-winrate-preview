using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2WinratePreview;

/// <summary>
/// Reads the live run state (deck / relics / potions / character + the next
/// Monster / Elite / Boss encounters) from the map screen, WITHOUT mutating
/// anything — pure read. The output is shaped for the headless win-rate helper
/// (<see cref="WinrateHelperClient"/>):
///   • character / encounter  → C# class name  (engine resolves by GetType().Name)
///   • relics / potions        → C# class name  (ScenarioVerifier resolves by GetType().Name)
///   • deck cards              → Id.Entry        (WinrateQuery resolves by Id.Entry)
/// Getting any of those forms wrong fails silently in the engine, so the mapping
/// is pinned here deliberately.
/// </summary>
public static class RunStateReader
{
    public sealed class EncounterTarget
    {
        public string Kind = "";       // "Monster" | "Elite" | "Boss"
        public string Encounter = "";  // class name e.g. "SlimesNormal", "KaiserCrabBoss"
    }

    public sealed class RunSnapshot
    {
        public string Character = "";
        public List<string> Deck = new();
        public List<string> Relics = new();
        public List<string> Potions = new();
        public List<EncounterTarget> Targets = new();
        /// Boss placeholder portrait png ("<BossNodePath>.png") — same path the
        /// game's own NBossMapPoint uses; may not exist for spine-animated bosses.
        public string? BossIconPng;
        public int ActIndex;
        public int CurrentHp;     // live HP — the sim starts each fight from this, not full
        public int MaxHp;         // live max HP (relics/events can raise it above the default)
        public string Fingerprint = "";  // changes iff a re-query is warranted
    }

    public static bool TryRead(out RunSnapshot snap)
    {
        snap = new RunSnapshot();
        try
        {
            var rm = RunManager.Instance;
            if (rm == null || !rm.IsInProgress) return false;
            IRunState? state = rm.DebugOnlyGetState();
            if (state == null) return false;

            // Local player (multiplayer-safe); falls back to first player solo.
            var player = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(state.Players)
                         ?? state.Players?.FirstOrDefault();
            if (player == null) return false;

            snap.Character = player.Character?.GetType().Name ?? "";
            if (string.IsNullOrEmpty(snap.Character)) return false;

            // Deck — grouped by Id.Entry into "ID*count".
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var card in player.Deck.Cards)
            {
                var id = card?.Id.Entry;
                if (string.IsNullOrEmpty(id)) continue;
                counts[id!] = counts.TryGetValue(id!, out var c) ? c + 1 : 1;
            }
            foreach (var kv in counts)
                snap.Deck.Add(kv.Value > 1 ? $"{kv.Key}*{kv.Value}" : kv.Key);

            foreach (var relic in player.Relics)
            {
                var n = relic?.GetType().Name;
                if (!string.IsNullOrEmpty(n)) snap.Relics.Add(n!);
            }
            foreach (var pot in player.Potions)
            {
                var n = pot?.GetType().Name;
                if (!string.IsNullOrEmpty(n)) snap.Potions.Add(n!);
            }

            snap.ActIndex = state.CurrentActIndex;
            try { snap.CurrentHp = player.Creature?.CurrentHp ?? 0; } catch { snap.CurrentHp = 0; }
            try { snap.MaxHp = (int)(player.Creature?.MaxHp ?? 0); } catch { snap.MaxHp = 0; }

            // Monster/Elite bands aggregate over the ACT'S FULL ENCOUNTER POOL
            // (every monster/elite this act CAN spawn), not this run's pre-rolled
            // node sequence. The pool is fixed per act, so the band stays stable
            // as you move through the act (only deck/HP change it) and isn't
            // skewed by whichever encounters this seed happened to roll.
            var act = state.Act;
            if (act != null)
            {
                foreach (var enc in DistinctByType(
                    SafeEnum(() => act.AllWeakEncounters).Concat(SafeEnum(() => act.AllRegularEncounters))))
                    AddTarget(snap, "Monster", enc);
                foreach (var enc in DistinctByType(SafeEnum(() => act.AllEliteEncounters)))
                    AddTarget(snap, "Elite", enc);

                // Boss is the single, already-revealed act boss — not a pool.
                var boss = SafeNext(() => act._rooms?.NextBossEncounter) ?? SafeNext(() => act.BossEncounter);
                AddTarget(snap, "Boss", boss);
                if (boss != null)
                    try { snap.BossIconPng = boss.BossNodePath + ".png"; } catch { }
            }

            snap.Fingerprint = BuildFingerprint(snap);
            return snap.Targets.Count > 0;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] run-state read failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static EncounterModel? SafeNext(Func<EncounterModel?> get)
    {
        try { return get(); } catch { return null; }
    }

    /// Enumerate a lazily-generated pool defensively — AllWeakEncounters etc. can
    /// trigger GenerateAllEncounters, which we don't want to throw from a UI read.
    private static IEnumerable<EncounterModel> SafeEnum(Func<IEnumerable<EncounterModel>?> get)
    {
        try { return get()?.ToList() ?? Enumerable.Empty<EncounterModel>(); }
        catch { return Enumerable.Empty<EncounterModel>(); }
    }

    /// One entry per distinct encounter class (the pool may list a type once, but
    /// guard against dupes regardless).
    private static IEnumerable<EncounterModel> DistinctByType(IEnumerable<EncounterModel> pool)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in pool)
            if (e != null && seen.Add(e.GetType().Name)) yield return e;
    }

    private static void AddTarget(RunSnapshot snap, string kind, EncounterModel? enc)
    {
        var name = enc?.GetType().Name;
        if (!string.IsNullOrEmpty(name))
            snap.Targets.Add(new EncounterTarget { Kind = kind, Encounter = name! });
    }

    private static string BuildFingerprint(RunSnapshot snap)
    {
        var sb = new StringBuilder();
        sb.Append("c").Append(snap.Character).Append("|a").Append(snap.ActIndex)
          .Append("|hp").Append(snap.CurrentHp).Append("/").Append(snap.MaxHp);
        sb.Append("|d");
        foreach (var d in snap.Deck.OrderBy(x => x, StringComparer.Ordinal)) sb.Append(d).Append(',');
        sb.Append("|r");
        foreach (var r in snap.Relics.OrderBy(x => x, StringComparer.Ordinal)) sb.Append(r).Append(',');
        sb.Append("|p");
        foreach (var p in snap.Potions.OrderBy(x => x, StringComparer.Ordinal)) sb.Append(p).Append(',');
        sb.Append("|t");
        foreach (var t in snap.Targets) sb.Append(t.Kind).Append(':').Append(t.Encounter).Append(',');
        return sb.ToString();
    }
}
