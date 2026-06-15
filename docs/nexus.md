# Nexus Mods listing

Copy-paste source for the Nexus Mods page.

---

## Short description (Nexus "Summary" field — plain text)

> A map overlay that predicts your win-rate for the next Monster / Elite / current-act Boss **before** you pick a node — by simulating the real STS2 combat engine with your current deck, relics and potions. Read-only; a band (even 100%) is a prediction, not a guarantee.

Alternative shorter one-liner:

> Predicts your combat win-rate for upcoming map nodes by simulating the real STS2 engine with your current deck. Read-only.

---

## Full description (paste into the Nexus BBCode editor)

```bbcode
[center][size=6][b]StS2 Winrate Preview[/b][/size][/center]

A [b]Slay the Spire 2 map overlay[/b] that shows a combat-risk band — [b]Safe / Caution / Danger[/b] — with a [b]win-rate %[/b] and [b]combat-quality %[/b] for the upcoming [b]Monster / Elite / current-act Boss[/b], [i]before[/i] you pick a map node.

The estimate isn't a heuristic — it [b]simulates the real STS2 combat engine[/b] in a separate headless helper process, using your [b]current deck, relics, potions and HP[/b]. It is [b]read-only and never touches your game state.[/b]

[img]https://raw.githubusercontent.com/ing-gom/sts2-winrate-preview/master/docs/screenshot.png[/img]

[quote][color=#e0a020][b]⚠ The band is a prediction, not a guarantee.[/b][/color] Even a [b]100%[/b] does [b]not[/b] promise a win — it's the AI playing your current deck against [i]sampled[/i] draws/encounters, so it shows how a fight [i]tends[/i] to go, not how [i]your[/i] run will. Your own draws, targeting and decisions can still lose a "Safe" fight (or win a "Danger" one). [b]Use it as guidance only.[/b][/quote]

[size=5][b]Features[/b][/size]
[list]
[*]A risk band per upcoming category (Monster / Elite / current-act Boss) on the map screen.
[*][b]Win-rate %[/b] + [b]combat-quality %[/b] (HP you'd keep on a win), side by side.
[*][b]Real combat-engine simulation[/b] via a pooled headless helper — not a static formula.
[*]Aggregates over the whole act's encounter pool, so the band stays stable as you progress (only your deck / HP change it).
[*]Draggable, dismissible panel; auto-hides during pause / settings popups.
[*]16 languages. Hover the panel for a short legend of the two numbers.
[/list]

[size=5][b]How it works[/b][/size]
[list=1]
[*]A bundled headless helper ([i]Sts2CombatCore[/i]) runs the [b]actual game combat engine[/b] out-of-process.
[*]When you open the map, the mod sends your current deck / relics / potions / HP to a [b]pool of helper processes[/b], which simulate each upcoming encounter to a win/loss.
[*]The band appears within ~1s per fight. Bigger machine → more helpers → faster refresh.
[/list]

[size=5][b]Install[/b][/size]
[list=1]
[*]Have an STS2 mod loader set up.
[*]Extract the archive into your game's [b]mods[/b] folder so you have:
[code]<game>/mods/Sts2WinratePreview/Sts2WinratePreview.dll
<game>/mods/Sts2WinratePreview/Sts2WinratePreview.json
<game>/mods/Sts2WinratePreview/helper/Sts2CombatCore.exe  (+ runtime files)[/code]
[*]Launch the game and start a run — the band appears on the map.
[/list]
[i]Windows x64. The bundled helper is self-contained — no separate .NET install needed.[/i]

[size=5][b]Configuration (optional — environment variables)[/b][/size]
[list]
[*][b]STS2_WINRATE_HELPERS[/b] — parallel helper processes (default: auto). More = faster refresh.
[*][b]STS2_WINRATE_DECISION[/b] — [i]search[/i] (default, more accurate) or [i]plannern[/i] (faster).
[*][b]STS2_WINRATE_QUERY_TIMEOUT_MS[/b] — per-encounter timeout in ms (default 60000).
[/list]

[size=5][b]Notes[/b][/size]
[list]
[*]The helper is a separate process; the first map-open spins it up (a brief one-time warm-up).
[*][b]affects_gameplay: false[/b] — the simulation is a disposable forecast; it does not auto-play or modify your run.
[*]Source / issues: [url=https://github.com/ing-gom/sts2-winrate-preview]github.com/ing-gom/sts2-winrate-preview[/url]
[/list]

Author: [b]inggom[/b]

[line]

[center][size=6][b]한국어[/b][/size][/center]

[b]슬레이 더 스파이어 2 맵 오버레이[/b] — 맵에서 노드를 고르기 [i]전에[/i], 다가올 [b]몬스터 / 엘리트 / 현재 막 보스[/b]에 대한 전투 위험도 밴드([b]안전 / 주의 / 위험[/b])와 [b]승률 %[/b], [b]전투 품질 %[/b]를 띄워 줍니다.

이 수치는 휴리스틱이 아니라, [b]현재 덱·유물·포션·HP[/b]로 [b]실제 STS2 전투 엔진을 별도 헤드리스 헬퍼 프로세스에서 시뮬레이션[/b]해 산출합니다. [b]읽기 전용이며 게임 상태에 전혀 영향을 주지 않습니다.[/b]

[quote][color=#e0a020][b]⚠ 밴드는 예측일 뿐 승리를 보장하지 않습니다.[/b][/color] [b]100%[/b]라도 승리를 보장하지 [b]않습니다[/b] — AI가 현재 덱으로 [i]표본[/i] 드로우/인카운터에 대해 플레이한 결과라, 전투가 [i]대체로[/i] 어떻게 흘러가는지를 나타낼 뿐입니다. 실제 드로우·타겟팅·순간 판단에 따라 '안전' 전투도 질 수 있고 '위험' 전투도 이길 수 있습니다. [b]어디까지나 참고용으로만 보세요.[/b][/quote]

[size=5][b]특징[/b][/size]
[list]
[*]맵 화면에 다가올 카테고리별(몬스터 / 엘리트 / 현재 막 보스) 위험도 밴드 표시.
[*][b]승률 %[/b] + [b]전투 품질 %[/b](이겼을 때 남는 HP) 병기.
[*]정적 수식이 아닌 [b]실제 전투 엔진 시뮬레이션[/b]을 헬퍼 풀에서 병렬 수행.
[*]막 전체 인카운터 풀에 대해 집계 — 막을 진행해도 밴드가 안정적(덱·HP 변화만 반영).
[*]드래그 이동·표시 토글 가능, 일시정지/설정 팝업 시 자동 숨김.
[*]16개 언어. 패널에 마우스를 올리면 두 수치 설명이 뜹니다.
[/list]

[size=5][b]작동 방식[/b][/size]
[list=1]
[*]번들된 헤드리스 헬퍼([i]Sts2CombatCore[/i])가 [b]실제 게임 전투 엔진[/b]을 별도 프로세스로 구동합니다.
[*]맵을 열면 모드가 현재 덱·유물·포션·HP를 [b]헬퍼 프로세스 풀[/b]에 보내, 다가올 각 전투를 승/패까지 시뮬레이션합니다.
[*]전투당 약 1초 내로 밴드가 뜹니다. 사양이 좋을수록 헬퍼가 많아져 더 빠릅니다.
[/list]

[size=5][b]설치[/b][/size]
[list=1]
[*]STS2 모드 로더가 설치돼 있어야 합니다.
[*]압축을 게임 [b]mods[/b] 폴더에 풀어 아래 구조가 되게 합니다:
[code]<게임>/mods/Sts2WinratePreview/Sts2WinratePreview.dll
<게임>/mods/Sts2WinratePreview/Sts2WinratePreview.json
<게임>/mods/Sts2WinratePreview/helper/Sts2CombatCore.exe  (+ 런타임 파일)[/code]
[*]게임을 실행하고 런을 시작하면 맵 화면에 밴드가 나타납니다.
[/list]
[i]Windows x64. 번들된 헬퍼는 자체 포함(self-contained)이라 별도 .NET 설치가 필요 없습니다.[/i]

[size=5][b]설정 (선택 — 환경 변수)[/b][/size]
[list]
[*][b]STS2_WINRATE_HELPERS[/b] — 병렬 헬퍼 수(기본: 자동). 많을수록 빠름.
[*][b]STS2_WINRATE_DECISION[/b] — [i]search[/i](기본, 정확) 또는 [i]plannern[/i](빠름).
[*][b]STS2_WINRATE_QUERY_TIMEOUT_MS[/b] — 전투당 타임아웃(ms, 기본 60000).
[/list]

[size=5][b]참고[/b][/size]
[list]
[*]헬퍼는 별도 프로세스라, 첫 맵 진입 시 한 번 워밍업합니다.
[*][b]affects_gameplay: false[/b] — 시뮬은 일회용 예측이며, 런을 자동 플레이하거나 수정하지 않습니다.
[*]소스 / 이슈: [url=https://github.com/ing-gom/sts2-winrate-preview]github.com/ing-gom/sts2-winrate-preview[/url]
[/list]

제작자: [b]inggom[/b]
```

---

### Notes
- The `[img]` line embeds `docs/screenshot.png` from the GitHub repo. If you upload the image to the Nexus gallery instead, delete that line.
- If Nexus renders `[size]` numbers differently, use the editor's header buttons instead.
- For an English-only page, delete everything from `[line]` down (the Korean block); for Korean-only, delete the English block.
