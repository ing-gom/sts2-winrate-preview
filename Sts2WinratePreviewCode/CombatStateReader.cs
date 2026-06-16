using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2WinratePreview;

/// <summary>
/// Reads the LIVE combat state for the in-combat "win from here" overlay.
/// Read-only. EXACT-restore ("완전") fidelity: it captures the board's HP +
/// powers AND the precise turn state — the current hand (in order), discard pile,
/// energy, block, and each enemy's pending intent — so the helper resumes the
/// actual turn rather than a fresh draw. This lets the estimate see a lethal hit
/// landing THIS turn (a "확정 죽음" board reads as ~0%), not just the average
/// continuation. The draw-pile ORDER is the one piece left to chance (it's hidden
/// info in-game), so deeper turns remain a sampled estimate.
/// </summary>
internal static class CombatStateReader
{
    public sealed class CombatSnap
    {
        public string Character = "";
        public string Encounter = "";      // EncounterModel class name (helper resolves it)
        public List<string> Deck = new();   // remaining cards (draw+hand+discard), "ID*n"
        public List<string> Relics = new();
        public List<string> Potions = new();
        public int CurrentHp;
        public int MaxHp;
        public List<int> EnemyHps = new();  // per-enemy current HP, in spawn order
        public List<WinrateHelperClient.PowerSpec> PlayerPowers = new();
        public List<List<WinrateHelperClient.PowerSpec>> EnemyPowers = new();  // index-matched to EnemyHps
        // EXACT restore ("완전" mode): resume the real turn so the sim sees "die now".
        public List<string> Hand = new();      // current hand, in order, by card id
        public List<string> Discard = new();   // discard pile by card id
        public int Energy = -1;                // current energy (-1 = unknown)
        public int Block;                      // current player block
        public List<WinrateHelperClient.MoveSpec> EnemyMoves = new();  // index-matched to EnemyHps
        public Dictionary<string, string> CardNames = new(StringComparer.Ordinal);    // hand card id -> localized name
        public Dictionary<string, string> PotionNames = new(StringComparer.Ordinal);  // potion id -> localized name
        public string Fingerprint = "";     // changes when the board changes (re-query gate)
    }

    public static bool TryRead(out CombatSnap snap)
    {
        snap = new CombatSnap();
        try
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsInProgress) return false;

            var rm = RunManager.Instance;
            IRunState? state = rm?.DebugOnlyGetState();
            if (state == null) return false;
            var player = LocalContext.GetMe(state.Players) ?? state.Players?.FirstOrDefault();
            if (player == null) return false;

            var cs = player.Creature?.CombatState;
            var enc = cs?.Encounter;
            if (cs == null || enc == null) return false;

            snap.Character = player.Character?.GetType().Name ?? "";
            snap.Encounter = enc.GetType().Name;
            if (string.IsNullOrEmpty(snap.Character) || string.IsNullOrEmpty(snap.Encounter)) return false;

            try { snap.CurrentHp = player.Creature?.CurrentHp ?? 0; } catch { snap.CurrentHp = 0; }
            try { snap.MaxHp = (int)(player.Creature?.MaxHp ?? 0); } catch { snap.MaxHp = 0; }

            // Remaining deck = draw + hand + discard (exhausted cards are gone for this fight).
            // Also capture the EXACT hand (ordered) + discard so the helper can resume
            // the real turn instead of drawing a fresh hand.
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pt in new[] { PileType.Draw, PileType.Hand, PileType.Discard })
            {
                var pile = pt.GetPile(player);
                if (pile?.Cards == null) continue;
                foreach (var card in pile.Cards)
                {
                    var id = card?.Id.Entry;
                    if (string.IsNullOrEmpty(id)) continue;
                    counts[id!] = counts.TryGetValue(id!, out var c) ? c + 1 : 1;
                    if (pt == PileType.Hand)
                    {
                        snap.Hand.Add(id!);
                        // Localized name for the recommendation chip (cheap; hand only).
                        if (!snap.CardNames.ContainsKey(id!))
                            try { var nm = card!.Title; if (!string.IsNullOrEmpty(nm)) snap.CardNames[id!] = nm; } catch { }
                    }
                    else if (pt == PileType.Discard) snap.Discard.Add(id!);
                }
            }
            if (counts.Count == 0) return false;
            foreach (var kv in counts)
                snap.Deck.Add(kv.Value > 1 ? $"{kv.Key}*{kv.Value}" : kv.Key);

            // Current energy + block (publicized). Energy < 0 stays "leave full".
            try { snap.Energy = (int)(player.PlayerCombatState?.Energy ?? -1); } catch { snap.Energy = -1; }
            try { snap.Block = (int)(player.Creature?.Block ?? 0); } catch { snap.Block = 0; }

            foreach (var relic in player.Relics)
            {
                var n = relic?.GetType().Name;
                if (!string.IsNullOrEmpty(n)) snap.Relics.Add(n!);
            }
            foreach (var pot in player.Potions)
            {
                var n = pot?.GetType().Name;
                if (string.IsNullOrEmpty(n)) continue;
                snap.Potions.Add(n!);
                // Localized name for a potion recommendation (PotionModel.Title is a LocString).
                if (!snap.PotionNames.ContainsKey(n!))
                    try { var nm = pot!.Title.GetFormattedText(); if (!string.IsNullOrEmpty(nm)) snap.PotionNames[n!] = nm; } catch { }
            }

            snap.PlayerPowers = ReadPowers(player.Creature);

            // Per-enemy current HP + powers, in spawn order (index-matched by the helper).
            foreach (var e in cs.Enemies)
            {
                int hp = 0;
                try { hp = e?.CurrentHp ?? 0; } catch { }
                snap.EnemyHps.Add(Math.Max(0, hp));
                snap.EnemyPowers.Add(ReadPowers(e));
                snap.EnemyMoves.Add(ReadMove(e));
            }
            if (snap.EnemyHps.Count == 0 || snap.EnemyHps.All(h => h <= 0)) return false;

            snap.Fingerprint = BuildFingerprint(snap);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// Cheap per-frame board signature (player HP + enemy HP total + hand size).
    /// Used by the overlay to detect "something changed" without the full pile
    /// read — when it moves, a debounced Recompute does the precise read + query.
    public static string CheapSig()
    {
        try
        {
            var rm = RunManager.Instance;
            var state = rm?.DebugOnlyGetState();
            var player = state != null ? (LocalContext.GetMe(state.Players) ?? state.Players?.FirstOrDefault()) : null;
            var cs = player?.Creature?.CombatState;
            if (cs == null) return "";
            int hp = 0; try { hp = player!.Creature?.CurrentHp ?? 0; } catch { }
            int esum = 0;
            foreach (var e in cs.Enemies) { try { esum += Math.Max(0, e?.CurrentHp ?? 0); } catch { } }
            int hand = 0; try { hand = PileType.Hand.GetPile(player)?.Cards?.Count ?? 0; } catch { }
            return hp + ":" + esum + ":" + hand;
        }
        catch { return ""; }
    }

    /// Read one enemy's pending intent (move state) so the helper can restore it
    /// and detect a lethal hit landing this turn. NextMove is the player-visible
    /// intent; current state + performedFirstMove keep the move machine consistent.
    private static WinrateHelperClient.MoveSpec ReadMove(Creature? c)
    {
        var spec = new WinrateHelperClient.MoveSpec();
        try
        {
            var monster = c?.Monster;
            if (monster == null) return spec;
            try { spec.nextMoveId = monster.NextMove?.Id; } catch { }
            var sm = monster.MoveStateMachine;
            if (sm != null)
            {
                try { spec.currentMoveId = sm._currentState?.Id; } catch { }
                try { spec.performedFirstMove = sm._performedFirstMove; } catch { }
            }
        }
        catch { }
        return spec;
    }

    private static List<WinrateHelperClient.PowerSpec> ReadPowers(Creature? c)
    {
        var list = new List<WinrateHelperClient.PowerSpec>();
        try
        {
            if (c?.Powers == null) return list;
            foreach (var p in c.Powers)
            {
                var id = p?.GetType().Name;
                if (string.IsNullOrEmpty(id)) continue;
                int amt = 0;
                try { amt = (int)p!.Amount; } catch { }
                list.Add(new WinrateHelperClient.PowerSpec { id = id!, amount = amt });
            }
        }
        catch { }
        return list;
    }

    private static string BuildFingerprint(CombatSnap s)
    {
        var sb = new StringBuilder();
        sb.Append(s.Encounter).Append("|hp").Append(s.CurrentHp).Append('/').Append(s.MaxHp);
        sb.Append("|e");
        foreach (var h in s.EnemyHps) sb.Append(h).Append(',');
        sb.Append("|d");
        foreach (var d in s.Deck.OrderBy(x => x, StringComparer.Ordinal)) sb.Append(d).Append(',');
        sb.Append("|pp");
        foreach (var p in s.PlayerPowers.OrderBy(x => x.id, StringComparer.Ordinal)) sb.Append(p.id).Append(p.amount).Append(',');
        sb.Append("|ep");
        foreach (var ep in s.EnemyPowers)
        {
            foreach (var p in ep.OrderBy(x => x.id, StringComparer.Ordinal)) sb.Append(p.id).Append(p.amount).Append(',');
            sb.Append(';');
        }
        // EXACT-restore inputs: hand (ordered), energy, block, and each enemy's
        // pending intent — re-query when any change (a card played, intent rolled).
        sb.Append("|h");
        foreach (var h in s.Hand) sb.Append(h).Append(',');
        sb.Append("|en").Append(s.Energy).Append("|bl").Append(s.Block);
        sb.Append("|m");
        foreach (var m in s.EnemyMoves) sb.Append(m.nextMoveId).Append('/').Append(m.currentMoveId).Append(m.performedFirstMove ? '1' : '0').Append(';');
        return sb.ToString();
    }
}
