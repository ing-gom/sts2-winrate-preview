# StS2 Winrate Preview

*[한국어 README](README.ko.md)*

A **map overlay for Slay the Spire 2** that shows a combat-risk band — **Safe / Caution / Danger** — with a **win-rate %** and **combat-quality %** for the upcoming **Monster / Elite / current-act Boss**, *before* you pick a map node.

The estimate isn't a heuristic — it **simulates the real STS2 combat engine** in a separate headless helper process, using your **current deck, relics, potions and HP**. It is **read-only and never touches your game state.**

> ⚠️ **The band is a prediction, not a guarantee.** Even a **100%** does **not** promise a win — it's the AI playing your current deck against *sampled* draws/encounters, so it reflects how a fight *tends* to go, not how *your* run will. Your own draws, targeting, and in-the-moment decisions can still lose a "Safe" fight (or win a "Danger" one). **Use it as guidance only.**

![overlay](docs/screenshot.png)

## Features
- A risk band per upcoming category (Monster / Elite / current-act Boss) on the map screen.
- **Win-rate %** + **combat-quality %** (HP you'd keep on a win) side by side.
- **Real combat-engine simulation** via a pooled headless helper running a rollout-improved policy — not a static formula.
- Aggregates over the whole act's encounter pool, so the band stays stable as you move through the act (only your deck / HP change it).
- **Optional [ModConfig](https://www.nexusmods.com/slaythespire2/mods/27) settings** — adjust the simulation trials per category (Monster / Elite / Boss, 1–10) in-game; more Boss trials = a steadier boss band. Without ModConfig the defaults apply.
- Draggable, dismissible panel; auto-hides whenever another screen (your deck / relic / potion view, pause, settings) is layered over the map.
- 16 languages. Hover the panel for a short legend of the two numbers.

## How it works
1. A headless helper (`Sts2CombatCore`, downloaded separately — see Install step 3) runs the **actual game combat engine** out-of-process.
2. When you open the map, the mod sends your current deck / relics / potions / HP to a **pool of helper processes**, which simulate each upcoming encounter to a win/loss outcome.
3. The win-rate band appears within roughly a second per fight. Bigger machine → more helpers → faster refresh.

The helper plays with a **rollout-improved "search" policy** that is floor-guarded by the hand-tuned planner, so the estimate reflects strong play and can never drop below the planner's own estimate.

## Install
1. Have an STS2 mod loader set up.
2. Extract the mod zip (from the Nexus page) into your game's `mods/` folder.
3. **Download the helper** (~42 MB, required — it runs the real combat engine and is hosted on GitHub due to its size) and extract it into the **same** `mods/` folder. Pick the build matching your game branch:
   - **Stable** (default branch): [Sts2WinratePreview-helper-v0.1.3-stable.zip](https://github.com/ing-gom/sts2-winrate-preview/releases/download/v0.1.3/Sts2WinratePreview-helper-v0.1.3-stable.zip)
   - **Beta** branch: [Sts2WinratePreview-helper-v0.1.3-beta.zip](https://github.com/ing-gom/sts2-winrate-preview/releases/download/v0.1.3/Sts2WinratePreview-helper-v0.1.3-beta.zip)

   <sub>SHA-256 — stable: `3401ad3752d8b850a980e7b5bd4bde6cf9e6e1e5cc7254b92446627e1c1452eb` · beta: `487066cf60d8f05cb64d89f51af65b34497521f8146b219a593e164c86d316c8`</sub>
4. The final layout should be:
   ```
   <game>/mods/Sts2WinratePreview/Sts2WinratePreview.dll
   <game>/mods/Sts2WinratePreview/Sts2WinratePreview.json
   <game>/mods/Sts2WinratePreview/helper/Sts2CombatCore.exe   (+ runtime files)
   ```
5. Launch the game and start a run — the band appears on the map screen.

## Configuration (optional — environment variables)
| Variable | Default | Effect |
|---|---|---|
| `STS2_WINRATE_HELPERS` | auto (≈ physical cores, RAM-capped, 2–16) | Number of parallel helper processes. More = faster refresh. |
| `STS2_WINRATE_DECISION` | `search` | `search` (rollout-improved, more accurate) or `plannern` (faster). |
| `STS2_WINRATE_QUERY_TIMEOUT_MS` | `60000` | Per-encounter timeout before a stuck helper is recycled. |

## Notes
- The helper is self-contained (Windows x64); no separate .NET install is required.
- The helper is a separate process; the first map-open spins it up (a brief one-time warm-up).
- `affects_gameplay: false` — the simulation is a disposable forecast; it does not auto-play or modify your run.
- The band reflects play roughly as strong as the built-in AI, sampled over the act's encounters — treat it as guidance, not a guarantee.

## Compatibility
Built and tested on **Slay the Spire 2 — public (default) branch, Steam build `23478716` (2026-06-02)** (sts2.dll 0.1.0), Windows x64.

Slay the Spire 2 is in Early Access and updates often. The mod and its helper are bound to the game's internal API, so a game update — and the **beta** branch, which runs ahead of stable — can break it until the mod is updated. If the band stops appearing after a game patch, check for a mod update.

## Credits & License
- Author: **inggom**
- Backed by the `Sts2CombatCore` headless combat engine.
