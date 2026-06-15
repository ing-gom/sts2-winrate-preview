# Sts2WinratePreview — in-game verification checklist

The mod shows a **combat-risk band** ("예상 전투 위험도": 안전/주의/위험) on the map
before you pick a node, backed by the real STS2 combat engine running in a pooled
headless helper process.

## Prereqs (already satisfied on the dev machine)
- Mod deployed: `<game>/mods/Sts2WinratePreview/Sts2WinratePreview.dll` (built by `dotnet build`).
- Helper exe resolvable. Resolution order (`WinrateHelperClient.EnumerateCandidates`):
  1. env `STS2_WINRATE_HELPER` (explicit path)
  2. `<mod>/helper/Sts2CombatCore.exe` (bundled — for distribution)
  3. dev fallback `C:\Users\kl95\sts2-combat-core\src\Sts2CombatCore\.godot\mono\temp\bin\Release\Sts2CombatCore.exe`
- Clone ONNX + vocab resolve relative to the helper (cwd-relative, then walk-up from the
  exe): `runs/clone_{Char}.onnx` and `python/sts2_combat/_vocab.json`. Only needed when
  `decision=clonehybrid`. Present under `sts2-combat-core/`.

## Steps
1. Launch STS2 with the mod enabled. Start a run.
2. On the map, hover/approach a node selection. The overlay chip should show e.g.
   `주의 62% · 품질 48%` (band word + win-rate% + combat-quality% = winrate × remaining-HP%).
3. Open the mod log (`<game>/mods/.../*.log` or the game console). Expect lines like:
   `[Sts2WinratePreview] helper pool ready (3 process slot(s), lazily started).`
   and per-query helper boot lines. No `helper not found` / `timeout` errors.
4. Sanity: the band should track intuition — easy fights green (안전), bosses red (위험).

## Policy toggle (search default / planner fallback)
- Default (no env): **search** — the rollout-improved planner with the per-seed planner
  FLOOR. Plays closer to optimal than the hand-tuned planner and can never score below it
  (the floor runs the planner too and takes the better per seed). ~2× the planner's wall-
  clock, so a full pool refresh is ~13 s vs ~6 s, but more accurate (esp. marginal fights
  and bosses). Verify the helper replies carry `"decision":"search"` (and a `"floored"`
  count) in the log.
- `STS2_WINRATE_DECISION=plannern`: fall back to the fast hand-tuned **planner**
  (~1–3 s/query) if you prefer speed over accuracy.
- `STS2_WINRATE_DECISION=clonehybrid`: the disagreement-arbitration hybrid (OOD-fragile on
  arbitrary real decks — not recommended for the live overlay; needs `runs/clone_{Char}.onnx`).
- `STS2_WINRATE_QUERY_TIMEOUT_MS` (default 60000): per-query ceiling before the helper is
  recycled (raised from 30 s to give search headroom on long bosses).

## Pool size / timeouts
- `STS2_WINRATE_HELPERS` (default 3): parallel helper processes (≈ K× throughput, K× RAM).
- A query that exceeds 30 s is treated as a hung helper and recycled.

## Troubleshooting
- "helper not found": set `STS2_WINRATE_HELPER` to the absolute exe path.
- clonehybrid errors "missing clone": ensure `runs/clone_{Char}.onnx` exists next to (or
  above) the helper exe; regenerate with `python/export_clones.py`.
- Slow / stutter: lower `trials`, reduce `STS2_WINRATE_HELPERS`, or keep the planner default.

## Distribution bundle (post-verification)
The helper bin is ~208 MB (full Godot mono runtime + cross-platform onnxruntime + sts2.dll).
For release, trim to win-x64 only and drop the game's own sts2.dll (present at runtime).
Not committed to git. Build a `helper/` folder under the mod from
`sts2-combat-core/.../bin/Release` + `runs/clone_*.onnx` + `python/sts2_combat/_vocab.json`.
