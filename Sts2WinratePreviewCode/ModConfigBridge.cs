using System;
using System.Linq;
using System.Reflection;

namespace Sts2WinratePreview;

/// <summary>
/// Optional ModConfig (Nexus #27) integration via reflection — zero hard dependency.
/// Exposes three Dropdowns (Monster / Elite / Boss simulation trials, 1..10) in the
/// game's in-game mod options, exactly like the sibling mods. ModConfig owns the UI
/// and persistence; we just push changes into <see cref="WinratePreviewService"/>.
/// If ModConfig isn't installed, the env/default trial counts apply and nothing
/// breaks. Dropdown (not Slider) because Slider semantics differ across ModConfig
/// versions while Dropdown is universally supported.
/// </summary>
internal static class ModConfigBridge
{
    private const string KeyMonster = "monsterTrials";
    private const string KeyElite = "eliteTrials";
    private const string KeyBoss = "bossTrials";

    // "1".."10" — the adjustable range exposed in the options menu.
    private static readonly string[] TrialOptions =
        Enumerable.Range(WinratePreviewService.MinTrials,
                         WinratePreviewService.MaxTrials - WinratePreviewService.MinTrials + 1)
                  .Select(i => i.ToString())
                  .ToArray();

    private static bool _attempted;

    public static void TryRegister()
    {
        if (_attempted) return;
        _attempted = true;

        var svc = WinratePreviewService.Instance;

        Type? apiType = null;
        Type? entryType = null;
        Type? configTypeEnum = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            apiType = asm.GetType("ModConfig.ModConfigApi", throwOnError: false);
            if (apiType != null)
            {
                entryType = asm.GetType("ModConfig.ConfigEntry", throwOnError: false);
                configTypeEnum = asm.GetType("ModConfig.ConfigType", throwOnError: false);
                break;
            }
        }
        if (apiType == null || entryType == null || configTypeEnum == null)
        {
            MainFile.Logger.Info($"[{MainFile.ModId}] ModConfig not found; trial counts use defaults "
                + $"(monster {svc.MonsterTrials} / elite {svc.EliteTrials} / boss {svc.BossTrials}). "
                + "Install ModConfig to adjust them in-game.");
            return;
        }

        try
        {
            var dropdownValue = Enum.Parse(configTypeEnum, "Dropdown");

            const string Note = "Simulations run per encounter (1–10). Higher = steadier numbers but slower. "
                + "Monster/Elite average over the whole act pool, so 1 is usually enough; the Boss is a single "
                + "fight (no averaging), so more trials give a more trustworthy boss band.";

            var monsterEntry = BuildEntry(entryType, dropdownValue,
                key: KeyMonster, label: "Monster trials (per encounter)", description: Note,
                defaultValue: svc.MonsterTrials.ToString(), options: TrialOptions,
                onChanged: v => { if (TryParseTrials(v, out var n)) svc.SetTrials("Monster", n); });

            var eliteEntry = BuildEntry(entryType, dropdownValue,
                key: KeyElite, label: "Elite trials (per encounter)", description: Note,
                defaultValue: svc.EliteTrials.ToString(), options: TrialOptions,
                onChanged: v => { if (TryParseTrials(v, out var n)) svc.SetTrials("Elite", n); });

            var bossEntry = BuildEntry(entryType, dropdownValue,
                key: KeyBoss, label: "Boss trials", description: Note,
                defaultValue: svc.BossTrials.ToString(), options: TrialOptions,
                onChanged: v => { if (TryParseTrials(v, out var n)) svc.SetTrials("Boss", n); });

            var entriesArray = Array.CreateInstance(entryType, 3);
            entriesArray.SetValue(monsterEntry, 0);
            entriesArray.SetValue(eliteEntry, 1);
            entriesArray.SetValue(bossEntry, 2);

            var register = apiType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Register"
                                     && m.GetParameters().Length == 3
                                     && m.GetParameters()[1].ParameterType == typeof(string));
            if (register == null)
            {
                MainFile.Logger.Warn($"[{MainFile.ModId}] ModConfigApi.Register(string,string,ConfigEntry[]) not found; skipping.");
                return;
            }
            register.Invoke(null, new object?[] { MainFile.ModId, "Winrate Preview", entriesArray });

            // Apply persisted values immediately (quiet — usually at the menu, no run active).
            var getValue = apiType.GetMethod("GetValue", BindingFlags.Public | BindingFlags.Static);
            if (getValue != null && getValue.IsGenericMethodDefinition)
            {
                var typedString = getValue.MakeGenericMethod(typeof(string));
                ApplySaved(typedString, KeyMonster, "Monster");
                ApplySaved(typedString, KeyElite, "Elite");
                ApplySaved(typedString, KeyBoss, "Boss");
            }

            MainFile.Logger.Info($"[{MainFile.ModId}] ModConfig integration active "
                + $"(monster {svc.MonsterTrials} / elite {svc.EliteTrials} / boss {svc.BossTrials} trials).");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] ModConfig register failed: {ex.Message}");
        }
    }

    private static void ApplySaved(MethodInfo getValueTypedString, string key, string kind)
    {
        try
        {
            var saved = getValueTypedString.Invoke(null, new object?[] { MainFile.ModId, key });
            if (TryParseTrials(saved, out var n))
                WinratePreviewService.Instance.SetTrialsQuiet(kind, n);
        }
        catch
        {
            // GetValue<string> may not be callable yet — OnChanged will sync later.
        }
    }

    private static bool TryParseTrials(object? raw, out int trials)
    {
        if (raw is string s && int.TryParse(s, out var v)) { trials = v; return true; }
        trials = 0;
        return false;
    }

    private static object BuildEntry(
        Type entryType, object configType, string key, string label, string description,
        string defaultValue, string[] options, Action<object?> onChanged)
    {
        var entry = Activator.CreateInstance(entryType)
            ?? throw new InvalidOperationException("ConfigEntry instance creation returned null.");
        SetProp(entry, "Key", key);
        SetProp(entry, "Type", configType);
        SetProp(entry, "Label", label);
        SetProp(entry, "Description", description);
        SetProp(entry, "DefaultValue", defaultValue);
        SetProp(entry, "Options", options);
        SetProp(entry, "OnChanged", onChanged);
        return entry;
    }

    private static void SetProp(object target, string name, object? value)
    {
        var p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        p?.SetValue(target, value);
    }
}
