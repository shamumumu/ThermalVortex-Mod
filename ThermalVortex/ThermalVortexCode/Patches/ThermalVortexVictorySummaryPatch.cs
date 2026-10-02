using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Runs;
using ThermalVortex.ThermalVortexCode.Character;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NGameOverScreen), "AnimateIn")]
internal static class ThermalVortexVictorySummaryPatch
{
    private static readonly MethodInfo OpenSummaryScreen =
        AccessTools.Method(typeof(NGameOverScreen), "OpenSummaryScreen");
    private static readonly MethodInfo UpdateBackstopMaterial =
        AccessTools.Method(typeof(NGameOverScreen), "UpdateBackstopMaterial");
    private static readonly FieldInfo BannerShowPosition =
        AccessTools.Field(typeof(NCommonBanner), "_showPos");

    [HarmonyPrefix]
    private static bool OpenVictorySummaryDirectly(
        NGameOverScreen __instance,
        Player ____localPlayer,
        RunHistory ____history,
        bool ____isAnimatingSummary,
        Control ____uiNode,
        Control ____creatureContainer,
        ColorRect ____fullBlackBackstop,
        ColorRect ____summaryBackstop,
        NCommonBanner ____banner,
        NGameOverContinueButton ____continueButton,
        ref Task __result)
    {
        // The Architect's closing attack can leave a victorious player at zero HP.
        // Use the same recorded victory that the native game-over screen displays.
        if (____history?.Win != true
            || !MillenniumPuzzleCharacterArt.IsThermalVortex(____localPlayer)
            || OpenSummaryScreen is null || UpdateBackstopMaterial is null || BannerShowPosition is null)
            return true;

        __result = Task.CompletedTask;
        if (____isAnimatingSummary)
            return false;

        // The retained Visuals include both the Architect and Yugi's complete
        // guard scene. Hide their shared layer before any summary UI appears;
        // keep the guard's ownership until exit so it cannot restore the portrait.
        ____creatureContainer.Hide();

        // _Ready and AfterOverlayOpened have already initialized the score, quote,
        // controls and creature layers. Set only the skipped intro's final visuals;
        // running that intro concurrently would enable Continue again afterwards.
        ____uiNode.Modulate = Colors.White;
        if (NEventRoom.Instance is not null)
        {
            ____fullBlackBackstop.Modulate = Colors.White;
            ____fullBlackBackstop.Show();
        }
        UpdateBackstopMaterial.Invoke(__instance, [1f]);
        ____summaryBackstop.Modulate = Colors.White;
        ____banner.GlobalPosition = (Vector2)BannerShowPosition.GetValue(____banner);
        ____banner.Modulate = Colors.White;
        ____continueButton.Hide();

        // Keep the native score, badge, discovery and navigation sequence, including
        // the existing OpenSummaryScreen patches for the character's victory quote.
        OpenSummaryScreen.Invoke(__instance, [____continueButton]);
        MainFile.Logger.Info("GameOver skipped ThermalVortex victory damage page; opened run summary.");
        return false;
    }
}
