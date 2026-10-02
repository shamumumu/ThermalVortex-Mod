using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.CardInspection;

[HarmonyPatch(typeof(ActiveScreenContext), nameof(ActiveScreenContext.GetCurrentScreen))]
internal static class CardInspectionScreenContextPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref IScreenContext __result) => CardInspectionService.AdaptCurrentScreen(ref __result);
}

[HarmonyPatch(typeof(NInspectCardScreen), nameof(NInspectCardScreen._Ready))]
internal static class CardInspectionScreenReadyPatch
{
    [HarmonyPrefix]
    private static void Prefix(NInspectCardScreen __instance) => CardInspectionService.BeginReady(__instance);

    [HarmonyFinalizer]
    private static void Finalizer(NInspectCardScreen __instance) => CardInspectionService.EndReady(__instance);
}

[HarmonyPatch(typeof(NInspectCardScreen), nameof(NInspectCardScreen.Open))]
internal static class CardInspectionScreenOpenPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NInspectCardScreen __instance, List<CardModel> __0, out bool __state)
    {
        __state = CardInspectionService.BeginNativeOpen(__instance, __0);
        return __state;
    }

    [HarmonyPostfix]
    private static void Postfix(NInspectCardScreen __instance, bool __state)
    {
        if (__state)
            CardInspectionService.EndNativeOpen(__instance);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(NInspectCardScreen __instance, bool __state, Exception __exception)
    {
        if (__state && __exception is not null)
            CardInspectionService.NativeOpenFailed(__instance);
        return __exception;
    }
}

[HarmonyPatch(typeof(NInspectCardScreen), nameof(NInspectCardScreen.Close))]
internal static class CardInspectionScreenClosePatch
{
    [HarmonyPrefix]
    private static bool Prefix(NInspectCardScreen __instance) => CardInspectionService.BeginNativeClose(__instance);
}

[HarmonyPatch(typeof(NInspectCardScreen), "UpdateCardDisplay")]
internal static class CardInspectionScreenDisplayPatch
{
    [HarmonyPrefix]
    private static void Prefix() => CardInspectionHoverTips.ForceClear();

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var clone = AccessTools.Method(typeof(AbstractModel), nameof(AbstractModel.MutableClone));
        var upgrade = AccessTools.Method(typeof(NCard), nameof(NCard.ShowUpgradePreview));
        var update = AccessTools.Method(typeof(NCard), nameof(NCard.UpdateVisuals));
        // These are the three verified calls in this one rendering method. Leave
        // other model clones and every other card renderer completely untouched.
        if (code.Count(instruction => instruction.Calls(clone)) != 1
            || code.Count(instruction => instruction.Calls(upgrade)) != 1
            || code.Count(instruction => instruction.Calls(update)) != 1)
        {
            MainFile.Logger.Info("Card inspection display adapter: native method changed; retaining native rendering.");
            return code;
        }

        var cloneReplacement = AccessTools.Method(typeof(CardInspectionScreenDisplayPatch), nameof(CloneForInspection));
        var upgradeReplacement = AccessTools.Method(typeof(CardInspectionScreenDisplayPatch), nameof(ShowUpgrade));
        var updateReplacement = AccessTools.Method(typeof(CardInspectionScreenDisplayPatch), nameof(UpdateVisuals));
        foreach (var instruction in code)
        {
            if (instruction.Calls(clone))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = cloneReplacement;
            }
            else if (instruction.Calls(upgrade))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = upgradeReplacement;
            }
            else if (instruction.Calls(update))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = updateReplacement;
            }
        }
        return code;
    }

    private static AbstractModel CloneForInspection(AbstractModel source)
    {
        var clone = source.MutableClone();
        if (source is CardModel sourceCard && clone is CardModel displayCard)
            CyberDevourState.AttachInspectionDisplayState(sourceCard, displayCard);
        return clone;
    }

    private static void ShowUpgrade(NCard card)
    {
        var display = CardInspectionService.GetDisplayContext();
        CardInspectionScreenAccess.SetPreviewFlags(card, display);
        // The native singleton previously retained the last preview's pile. Use
        // None for ordinary native callers, or the captured source pile here.
        card.UpdateVisuals(display?.Pile ?? PileType.None, CardPreviewMode.Upgrade);
    }

    private static void UpdateVisuals(NCard card, PileType pile, CardPreviewMode mode)
    {
        var display = CardInspectionService.GetDisplayContext();
        CardInspectionScreenAccess.SetPreviewFlags(card, display);
        if (display is not null)
        {
            pile = display.Pile;
            // Unticking the native upgrade box must also clear source highlight.
            mode = display.Mode == CardPreviewMode.Upgrade ? CardPreviewMode.Normal : display.Mode;
        }
        card.UpdateVisuals(pile, mode);
    }
}

internal static class CardInspectionScreenAccess
{
    internal static readonly AccessTools.FieldRef<NInspectCardScreen, List<CardModel>> Cards =
        AccessTools.FieldRefAccess<NInspectCardScreen, List<CardModel>>("_cards");
    internal static readonly AccessTools.FieldRef<NInspectCardScreen, int> Index =
        AccessTools.FieldRefAccess<NInspectCardScreen, int>("_index");
    private static readonly AccessTools.FieldRef<NInspectCardScreen, bool> ViewAllUpgraded =
        AccessTools.FieldRefAccess<NInspectCardScreen, bool>("_viewAllUpgraded");
    private static readonly AccessTools.FieldRef<NInspectCardScreen, Tween> OpenTween =
        AccessTools.FieldRefAccess<NInspectCardScreen, Tween>("_openTween");
    private static readonly AccessTools.FieldRef<NInspectCardScreen, Tween> CardTween =
        AccessTools.FieldRefAccess<NInspectCardScreen, Tween>("_cardTween");
    private static readonly AccessTools.FieldRef<NCard, Creature> PreviewTarget =
        AccessTools.FieldRefAccess<NCard, Creature>("_previewTarget");
    private static readonly AccessTools.FieldRef<NCard, bool> ForceUnpowered =
        AccessTools.FieldRefAccess<NCard, bool>("_forceUnpoweredPreview");
    private static readonly AccessTools.FieldRef<NCard, bool> PretendPlayable =
        AccessTools.FieldRefAccess<NCard, bool>("_pretendCardCanBePlayed");
    private static readonly Action<NInspectCardScreen, int> SetCard =
        AccessTools.Method(typeof(NInspectCardScreen), "SetCard", [typeof(int)])
            .CreateDelegate<Action<NInspectCardScreen, int>>();

    internal static void ReplaceCards(NInspectCardScreen screen, List<CardModel> cards, int index, bool viewUpgraded)
    {
        Cards(screen) = cards;
        ViewAllUpgraded(screen) = viewUpgraded;
        SetCard(screen, index);
    }

    internal static void SetPreviewFlags(NCard card, CardInspectionDisplayContext display)
    {
        // SetPreviewTarget itself renders once; setting its verified backing
        // field lets the native final UpdateVisuals perform the only refresh.
        PreviewTarget(card) = display?.PreviewTarget;
        ForceUnpowered(card) = display?.ForceUnpowered ?? false;
        PretendPlayable(card) = display?.PretendPlayable ?? false;
    }

    internal static void FinishAnimations(NInspectCardScreen screen)
    {
        Complete(OpenTween(screen));
        Complete(CardTween(screen));
    }

    internal static void StopCardAnimation(NInspectCardScreen screen)
    {
        Kill(CardTween(screen));
        CardTween(screen) = null;
    }

    internal static void StopAnimations(NInspectCardScreen screen)
    {
        Kill(OpenTween(screen));
        OpenTween(screen) = null;
        StopCardAnimation(screen);
    }

    private static void Complete(Tween tween)
    {
        if (tween is not null && GodotObject.IsInstanceValid(tween) && tween.IsValid())
            tween.FastForwardToCompletion();
    }

    private static void Kill(Tween tween)
    {
        if (tween is not null && GodotObject.IsInstanceValid(tween))
            tween.Kill();
    }
}
