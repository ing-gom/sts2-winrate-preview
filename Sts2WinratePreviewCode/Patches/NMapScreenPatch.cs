using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace Sts2WinratePreview.Patches;

/// <summary>
/// Every time the map screen opens: make sure our floating panel is attached to
/// it (child of NMapScreen → visibility follows the screen for free) and kick a
/// win-rate refresh. The refresh is fingerprint-cached, so re-opening the map
/// without deck/relic/potion/encounter changes costs nothing.
/// </summary>
[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
internal static class NMapScreenOpenPatch
{
    private static void Postfix(NMapScreen __instance)
    {
        try
        {
            if (__instance.GetNodeOrNull<MapPreviewPanel>(MapPreviewPanel.NodeName) == null)
                __instance.AddChild(new MapPreviewPanel());

            WinratePreviewService.Instance.Refresh();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] map-open hook failed: {ex.Message}");
        }
    }
}
