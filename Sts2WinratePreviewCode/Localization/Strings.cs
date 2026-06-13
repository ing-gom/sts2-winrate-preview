using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;

namespace Sts2WinratePreview.Localization;

/// <summary>
/// Tiny localization table for the preview panel. Mirrors the sibling-mod pattern
/// (LocManager.Instance.Language → per-language dictionary, ENG fallback). Bands
/// are framed as difficulty (Easy / Normal / Hard) since the win-rate % is shown
/// alongside — that ladder reads more intuitively than safe/risky/lethal.
/// </summary>
public static class Strings
{
    public static string Get(string key)
    {
        var lang = GetCurrentLanguage();
        if (_tables.TryGetValue(lang, out var t) && t.TryGetValue(key, out var v)) return v;
        if (_tables.TryGetValue("ENG", out var eng) && eng.TryGetValue(key, out var ev)) return ev;
        return key;
    }

    public static string Get(string key, params object[] args)
    {
        var raw = Get(key);
        try { return args.Length == 0 ? raw : string.Format(raw, args); }
        catch { return raw; }
    }

    private static string GetCurrentLanguage()
    {
        try { return (LocManager.Instance?.Language ?? "ENG").ToUpperInvariant(); }
        catch { return "ENG"; }
    }

    // Keys: header, kind_monster/elite/boss, band_easy/normal/hard,
    //       calc, calc_progress ({0}/{1}), tip_line ({0}=kind {1}=win%), tip_hardest ({0}=enc)
    private static readonly Dictionary<string, Dictionary<string, string>> _tables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENG"] = new() {
            ["header"]="Combat risk", ["kind_monster"]="Monster", ["kind_elite"]="Elite", ["kind_boss"]="Boss",
            ["band_easy"]="Safe", ["band_normal"]="Caution", ["band_hard"]="Danger",
            ["calc"]="Calculating…", ["calc_progress"]="Calculating {0}/{1}",
            ["tip_line"]="{0}: {1}% win", ["tip_hardest"]=" (hardest: {0})", ["tip_failed"]=" ({0}/{1} sim failed)", ["boss_progress"]=" {0} · defeat {1}%", ["tip_boss"]="{0}: {1}% defeated", ["tip_quality"]="{0}: {1}% win · quality {2}%", ["qual"]="Q"},
        ["KOR"] = new() {
            ["header"]="예상 전투 위험도", ["kind_monster"]="몬스터", ["kind_elite"]="엘리트", ["kind_boss"]="보스",
            ["band_easy"]="안전", ["band_normal"]="주의", ["band_hard"]="위험",
            ["calc"]="계산 중…", ["calc_progress"]="계산 중 {0}/{1}",
            ["tip_line"]="{0}: 승률 {1}%", ["tip_hardest"]=" (최난적: {0})", ["tip_failed"]=" ({1}개 중 {0}개 시뮬 실패)", ["boss_progress"]=" {0} · 처치 {1}%", ["tip_boss"]="{0}: 처치 {1}%", ["tip_quality"]="{0}: 승률 {1}% · 전투 품질 {2}%", ["qual"]="품질"},
        ["JPN"] = new() {
            ["header"]="戦闘の予想勝率", ["kind_monster"]="モンスター", ["kind_elite"]="エリート", ["kind_boss"]="ボス",
            ["band_easy"]="簡単", ["band_normal"]="普通", ["band_hard"]="難しい",
            ["calc"]="計算中…", ["calc_progress"]="計算中 {0}/{1}",
            ["tip_line"]="{0}: 勝率{1}%", ["tip_hardest"]=" (最難関: {0})", ["tip_failed"]=" ({1}件中{0}件 失敗)", ["boss_progress"]=" {0} · 撃破 {1}%", ["tip_boss"]="{0}: 撃破{1}%"},
        ["ZHS"] = new() {
            ["header"]="战斗预期胜率", ["kind_monster"]="怪物", ["kind_elite"]="精英", ["kind_boss"]="首领",
            ["band_easy"]="简单", ["band_normal"]="普通", ["band_hard"]="困难",
            ["calc"]="计算中…", ["calc_progress"]="计算中 {0}/{1}",
            ["tip_line"]="{0}: 胜率{1}%", ["tip_hardest"]=" (最难: {0})", ["tip_failed"]=" ({1}个中{0}个模拟失败)", ["boss_progress"]=" {0} · 击败 {1}%", ["tip_boss"]="{0}: 击败{1}%"},
        ["ZHT"] = new() {
            ["header"]="戰鬥預期勝率", ["kind_monster"]="怪物", ["kind_elite"]="精英", ["kind_boss"]="首領",
            ["band_easy"]="簡單", ["band_normal"]="普通", ["band_hard"]="困難",
            ["calc"]="計算中…", ["calc_progress"]="計算中 {0}/{1}",
            ["tip_line"]="{0}: 勝率{1}%", ["tip_hardest"]=" (最難: {0})", ["tip_failed"]=" ({1}個中{0}個模擬失敗)", ["boss_progress"]=" {0} · 擊敗 {1}%", ["tip_boss"]="{0}: 擊敗{1}%"},
        ["FRA"] = new() {
            ["header"]="Taux de victoire estimé", ["kind_monster"]="Monstre", ["kind_elite"]="Élite", ["kind_boss"]="Boss",
            ["band_easy"]="Facile", ["band_normal"]="Moyen", ["band_hard"]="Difficile",
            ["calc"]="Calcul…", ["calc_progress"]="Calcul {0}/{1}",
            ["tip_line"]="{0} : {1}% de victoire", ["tip_hardest"]=" (le plus dur : {0})", ["tip_failed"]=" ({0}/{1} sim échoué)", ["boss_progress"]=" {0} · vaincu {1}%", ["tip_boss"]="{0} : {1}% vaincu"},
        ["DEU"] = new() {
            ["header"]="Erwartete Siegchance", ["kind_monster"]="Monster", ["kind_elite"]="Elite", ["kind_boss"]="Boss",
            ["band_easy"]="Leicht", ["band_normal"]="Mittel", ["band_hard"]="Schwer",
            ["calc"]="Berechne…", ["calc_progress"]="Berechne {0}/{1}",
            ["tip_line"]="{0}: {1}% Sieg", ["tip_hardest"]=" (schwerster: {0})", ["tip_failed"]=" ({0}/{1} Sim fehlgeschlagen)", ["boss_progress"]=" {0} · {1}% besiegt", ["tip_boss"]="{0}: {1}% besiegt"},
        ["ESP"] = new() {
            ["header"]="Prob. de victoria estimada", ["kind_monster"]="Monstruo", ["kind_elite"]="Élite", ["kind_boss"]="Jefe",
            ["band_easy"]="Fácil", ["band_normal"]="Normal", ["band_hard"]="Difícil",
            ["calc"]="Calculando…", ["calc_progress"]="Calculando {0}/{1}",
            ["tip_line"]="{0}: {1}% victoria", ["tip_hardest"]=" (más difícil: {0})", ["tip_failed"]=" ({0}/{1} sim fallida)", ["boss_progress"]=" {0} · {1}% derrotado", ["tip_boss"]="{0}: {1}% derrotado"},
        ["SPA"] = new() {
            ["header"]="Prob. de victoria estimada", ["kind_monster"]="Monstruo", ["kind_elite"]="Élite", ["kind_boss"]="Jefe",
            ["band_easy"]="Fácil", ["band_normal"]="Normal", ["band_hard"]="Difícil",
            ["calc"]="Calculando…", ["calc_progress"]="Calculando {0}/{1}",
            ["tip_line"]="{0}: {1}% victoria", ["tip_hardest"]=" (más difícil: {0})", ["tip_failed"]=" ({0}/{1} sim fallida)", ["boss_progress"]=" {0} · {1}% derrotado", ["tip_boss"]="{0}: {1}% derrotado"},
        ["ITA"] = new() {
            ["header"]="Probabilità di vittoria stimata", ["kind_monster"]="Mostro", ["kind_elite"]="Élite", ["kind_boss"]="Boss",
            ["band_easy"]="Facile", ["band_normal"]="Medio", ["band_hard"]="Difficile",
            ["calc"]="Calcolo…", ["calc_progress"]="Calcolo {0}/{1}",
            ["tip_line"]="{0}: {1}% vittoria", ["tip_hardest"]=" (più difficile: {0})", ["tip_failed"]=" ({0}/{1} sim falliti)", ["boss_progress"]=" {0} · {1}% sconfitto", ["tip_boss"]="{0}: {1}% sconfitto"},
        ["RUS"] = new() {
            ["header"]="Ожидаемый шанс победы", ["kind_monster"]="Монстр", ["kind_elite"]="Элита", ["kind_boss"]="Босс",
            ["band_easy"]="Легко", ["band_normal"]="Средне", ["band_hard"]="Сложно",
            ["calc"]="Расчёт…", ["calc_progress"]="Расчёт {0}/{1}",
            ["tip_line"]="{0}: {1}% побед", ["tip_hardest"]=" (сложнее всего: {0})", ["tip_failed"]=" ({0}/{1} симуляций не удалось)", ["boss_progress"]=" {0} · {1}% урона", ["tip_boss"]="{0}: {1}% урона"},
        ["PTB"] = new() {
            ["header"]="Taxa de vitória estimada", ["kind_monster"]="Monstro", ["kind_elite"]="Elite", ["kind_boss"]="Chefe",
            ["band_easy"]="Fácil", ["band_normal"]="Normal", ["band_hard"]="Difícil",
            ["calc"]="Calculando…", ["calc_progress"]="Calculando {0}/{1}",
            ["tip_line"]="{0}: {1}% vitória", ["tip_hardest"]=" (mais difícil: {0})", ["tip_failed"]=" ({0}/{1} sim falhou)", ["boss_progress"]=" {0} · {1}% derrotado", ["tip_boss"]="{0}: {1}% derrotado"},
        ["POR"] = new() {
            ["header"]="Taxa de vitória estimada", ["kind_monster"]="Monstro", ["kind_elite"]="Elite", ["kind_boss"]="Chefe",
            ["band_easy"]="Fácil", ["band_normal"]="Normal", ["band_hard"]="Difícil",
            ["calc"]="Calculando…", ["calc_progress"]="Calculando {0}/{1}",
            ["tip_line"]="{0}: {1}% vitória", ["tip_hardest"]=" (mais difícil: {0})", ["tip_failed"]=" ({0}/{1} sim falhou)", ["boss_progress"]=" {0} · {1}% derrotado", ["tip_boss"]="{0}: {1}% derrotado"},
        ["POL"] = new() {
            ["header"]="Szacowana szansa wygranej", ["kind_monster"]="Potwór", ["kind_elite"]="Elita", ["kind_boss"]="Boss",
            ["band_easy"]="Łatwo", ["band_normal"]="Średnio", ["band_hard"]="Trudno",
            ["calc"]="Obliczanie…", ["calc_progress"]="Obliczanie {0}/{1}",
            ["tip_line"]="{0}: {1}% wygranych", ["tip_hardest"]=" (najtrudniejszy: {0})", ["tip_failed"]=" ({0}/{1} sym. nieudane)", ["boss_progress"]=" {0} · pokonano {1}%", ["tip_boss"]="{0}: pokonano {1}%"},
        ["TUR"] = new() {
            ["header"]="Tahmini kazanma oranı", ["kind_monster"]="Canavar", ["kind_elite"]="Seçkin", ["kind_boss"]="Patron",
            ["band_easy"]="Kolay", ["band_normal"]="Orta", ["band_hard"]="Zor",
            ["calc"]="Hesaplanıyor…", ["calc_progress"]="Hesaplanıyor {0}/{1}",
            ["tip_line"]="{0}: %{1} kazanma", ["tip_hardest"]=" (en zor: {0})", ["tip_failed"]=" ({0}/{1} sim başarısız)", ["boss_progress"]=" {0} · %{1} yenildi", ["tip_boss"]="{0}: %{1} yenildi"},
        ["THA"] = new() {
            ["header"]="อัตราชนะโดยประมาณ", ["kind_monster"]="มอนสเตอร์", ["kind_elite"]="อีลีท", ["kind_boss"]="บอส",
            ["band_easy"]="ง่าย", ["band_normal"]="ปานกลาง", ["band_hard"]="ยาก",
            ["calc"]="กำลังคำนวณ…", ["calc_progress"]="กำลังคำนวณ {0}/{1}",
            ["tip_line"]="{0}: ชนะ {1}%", ["tip_hardest"]=" (ยากสุด: {0})", ["tip_failed"]=" (ล้มเหลว {0}/{1})", ["boss_progress"]=" {0} · กำจัด {1}%", ["tip_boss"]="{0}: กำจัด {1}%"},
    };
}
