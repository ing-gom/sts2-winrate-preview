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

    /// The in-combat chip is dev-only while it's experimental — enabled only when a
    /// developer sets STS2_WINRATE_COMBAT_CHIP=1. Normal players never see it.
    private static bool CombatChipEnabled =>
        !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("STS2_WINRATE_COMBAT_CHIP"));

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
            {
                tree.CreateTimer(0.0).Timeout += ModConfigBridge.TryRegister;
                // In-combat "win from here" overlay (persistent root node, shows only
                // while a fight is the foreground screen). DEV-ONLY for now — still
                // experimental, so it's gated behind STS2_WINRATE_COMBAT_CHIP=1 and
                // stays off for normal players. The shipped feature is the map-node
                // band preview; this chip is enabled only when a developer opts in.
                if (CombatChipEnabled)
                {
                    tree.CreateTimer(0.0).Timeout += CombatOverlay.Install;
                    Logger.Info($"[{ModId}] in-combat chip ENABLED (dev mode).");
                }
            }

            // Opt-in in-game smoke test: proves the live mod <-> helper-process
            // roundtrip end-to-end. Run off the main thread so the game never stalls.
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("STS2_WINRATE_SELFTEST")))
                Task.Run(SelfTest);

            Logger.Info($"[{ModId}] initialized (v0.1.3).");
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
