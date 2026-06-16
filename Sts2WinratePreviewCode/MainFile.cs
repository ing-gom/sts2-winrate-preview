using System;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2WinratePreview;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "Sts2WinratePreview";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; }
        = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        try
        {
            var harmony = new Harmony(ModId);
            harmony.PatchAll(typeof(MainFile).Assembly);
            Logger.Info($"[{ModId}] Harmony patches applied.");

            // Make sure the headless helper child is torn down with the game.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => WinrateHelperClient.Instance.Dispose();

            // Register the in-game mod options (Monster/Elite/Boss trials, 1–10) via
            // ModConfig. Deferred a frame so ModConfig's own Initialize runs first;
            // a no-op (defaults apply) if ModConfig isn't installed.
            if (Engine.GetMainLoop() is SceneTree tree)
                tree.CreateTimer(0.0).Timeout += ModConfigBridge.TryRegister;

            // Opt-in in-game smoke test: proves the live mod <-> helper-process
            // roundtrip end-to-end. Run off the main thread so the game never stalls.
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("STS2_WINRATE_SELFTEST")))
                Task.Run(SelfTest);

            Logger.Info($"[{ModId}] initialized (v0.1.0).");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[{ModId}] init failed: {ex.Message}");
        }
    }

    private static void SelfTest()
    {
        try
        {
            var req = new WinrateHelperClient.WinrateRequest
            {
                character = "Ironclad",
                deck = new() { "STRIKE_IRONCLAD*5", "DEFEND_IRONCLAD*4", "BASH", "INFLAME" },
                relics = new() { "BurningBlood" },
                potions = new(),
                encounter = "SlimesNormal",
                trials = 3,
                seed = "selftest",
            };
            var r = WinrateHelperClient.Instance.Query(req);
            if (r.Ok)
                Logger.Info($"[{ModId}] selftest OK: {r.encounter} {r.wins}/{r.n} = {r.winrate:P0} ({r.seconds:F1}s)");
            else
                Logger.Warn($"[{ModId}] selftest FAILED: {r.error}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[{ModId}] selftest threw: {ex.Message}");
        }
    }
}
