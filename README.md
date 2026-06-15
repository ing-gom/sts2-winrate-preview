# StS2 Winrate Preview

*[한국어](#한국어)*

A **map overlay for Slay the Spire 2** that shows a combat-risk band — **Safe / Caution / Danger** — with a **win-rate %** and **combat-quality %** for the upcoming **Monster / Elite / current-act Boss**, *before* you pick a map node.

The estimate isn't a heuristic — it **simulates the real STS2 combat engine** in a separate headless helper process, using your **current deck, relics, potions and HP**. It is **read-only and never touches your game state.**

![overlay](docs/screenshot.png)

## Features
- A risk band per upcoming category (Monster / Elite / current-act Boss) on the map screen.
- **Win-rate %** + **combat-quality %** (HP you'd keep on a win) side by side.
- **Real combat-engine simulation** via a pooled headless helper running a rollout-improved policy — not a static formula.
- Aggregates over the whole act's encounter pool, so the band stays stable as you move through the act (only your deck / HP change it).
- Draggable, dismissible panel; auto-hides during pause / settings popups.
- 16 languages. Hover the panel for a short legend of the two numbers.

## How it works
1. A bundled headless helper (`Sts2CombatCore`) runs the **actual game combat engine** out-of-process.
2. When you open the map, the mod sends your current deck / relics / potions / HP to a **pool of helper processes**, which simulate each upcoming encounter to a win/loss outcome.
3. The win-rate band appears within roughly a second per fight. Bigger machine → more helpers → faster refresh.

The helper plays with a **rollout-improved "search" policy** that is floor-guarded by the hand-tuned planner, so the estimate reflects strong play and can never drop below the planner's own estimate.

## Install
1. Have an STS2 mod loader set up.
2. Extract the release zip into your game's `mods/` folder so the layout is:
   ```
   <game>/mods/Sts2WinratePreview/Sts2WinratePreview.dll
   <game>/mods/Sts2WinratePreview/Sts2WinratePreview.json
   <game>/mods/Sts2WinratePreview/helper/Sts2CombatCore.exe   (+ runtime files)
   ```
3. Launch the game and start a run — the band appears on the map screen.

## Configuration (optional — environment variables)
| Variable | Default | Effect |
|---|---|---|
| `STS2_WINRATE_HELPERS` | auto (≈ physical cores, RAM-capped, 2–16) | Number of parallel helper processes. More = faster refresh. |
| `STS2_WINRATE_DECISION` | `search` | `search` (rollout-improved, more accurate) or `plannern` (faster). |
| `STS2_WINRATE_QUERY_TIMEOUT_MS` | `60000` | Per-encounter timeout before a stuck helper is recycled. |

## Notes
- The helper is a separate process; the first map-open spins it up (a brief one-time warm-up).
- `affects_gameplay: false` — the simulation is a disposable forecast; it does not auto-play or modify your run.
- The band reflects play roughly as strong as the built-in AI, sampled over the act's encounters — treat it as guidance, not a guarantee.

## Credits & License
- Author: **inggom**
- Backed by the `Sts2CombatCore` headless combat engine.

---

## 한국어

**슬레이 더 스파이어 2 맵 오버레이** — 맵에서 노드를 고르기 *전에*, 다가올 **몬스터 / 엘리트 / 현재 막 보스**에 대한 전투 위험도 밴드(**안전 / 주의 / 위험**)와 **승률 %**, **전투 품질 %**를 띄워 줍니다.

이 수치는 휴리스틱이 아니라, **현재 덱·유물·포션·HP**로 **실제 STS2 전투 엔진을 별도 헤드리스 헬퍼 프로세스에서 시뮬레이션**해 산출합니다. **읽기 전용이며 게임 상태에 전혀 영향을 주지 않습니다.**

### 특징
- 맵 화면에 다가올 카테고리별(몬스터 / 엘리트 / 현재 막 보스) 위험도 밴드 표시.
- **승률 %** + **전투 품질 %**(이겼을 때 남는 HP) 병기.
- 정적 수식이 아닌 **실제 전투 엔진 시뮬레이션**(롤아웃-개선 정책)을 헬퍼 풀에서 병렬 수행.
- 막 전체 인카운터 풀에 대해 집계하므로, 막을 진행해도 밴드가 안정적(덱·HP 변화만 반영).
- 드래그 이동·표시 토글 가능, 일시정지/설정 팝업 시 자동 숨김.
- 16개 언어. 패널에 마우스를 올리면 두 수치에 대한 간단한 설명이 뜹니다.

### 작동 방식
1. 번들된 헤드리스 헬퍼(`Sts2CombatCore`)가 **실제 게임 전투 엔진**을 별도 프로세스로 구동합니다.
2. 맵을 열면 모드가 현재 덱·유물·포션·HP를 **헬퍼 프로세스 풀**에 보내, 다가올 각 전투를 승/패까지 시뮬레이션합니다.
3. 전투당 약 1초 내로 밴드가 뜹니다. 사양이 좋을수록 헬퍼가 많아져 더 빠릅니다.

헬퍼는 **롤아웃-개선 "search" 정책**으로 플레이하며, 손튜닝 플래너로 floor를 보장해 추정치가 절대 플래너보다 낮아지지 않습니다.

### 설치
1. STS2 모드 로더가 설치돼 있어야 합니다.
2. 릴리즈 zip을 게임 `mods/` 폴더에 풀어 아래 구조가 되게 합니다:
   ```
   <게임>/mods/Sts2WinratePreview/Sts2WinratePreview.dll
   <게임>/mods/Sts2WinratePreview/Sts2WinratePreview.json
   <게임>/mods/Sts2WinratePreview/helper/Sts2CombatCore.exe   (+ 런타임 파일)
   ```
3. 게임을 실행하고 런을 시작하면 맵 화면에 밴드가 나타납니다.

### 설정 (선택 — 환경 변수)
| 변수 | 기본값 | 효과 |
|---|---|---|
| `STS2_WINRATE_HELPERS` | 자동 (≈ 물리 코어, RAM 상한, 2–16) | 병렬 헬퍼 프로세스 수. 많을수록 빠름. |
| `STS2_WINRATE_DECISION` | `search` | `search`(롤아웃-개선, 정확) 또는 `plannern`(빠름). |
| `STS2_WINRATE_QUERY_TIMEOUT_MS` | `60000` | 전투당 타임아웃(초과 시 헬퍼 재활용). |

### 참고
- 헬퍼는 별도 프로세스라, 첫 맵 진입 시 한 번 워밍업합니다.
- `affects_gameplay: false` — 시뮬은 일회용 예측이며, 런을 자동 플레이하거나 수정하지 않습니다.
- 밴드는 내장 AI 수준의 플레이를 막 인카운터에 표본추출한 것 — 보장이 아닌 **참고용**으로 보세요.

### 제작 / 라이선스
- 제작자: **inggom**
- `Sts2CombatCore` 헤드리스 전투 엔진 기반.
