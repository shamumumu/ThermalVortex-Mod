using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Patches;

// Add the same proxies on every peer before the native command counts its
// candidates, takes an automatic selection, or branches into local/remote UI.
[HarmonyPatch(
    typeof(CardSelectCmd),
    nameof(CardSelectCmd.FromDeckForUpgrade),
    [typeof(Player), typeof(CardSelectorPrefs)])]
internal static class ExtraDeckUpgradeSelectPatch
{
    [HarmonyPrefix]
    private static void AddExtraDeckCards(Player __0, out ExtraDeckSelectionProxyScope __state)
    {
        __state = ExtraDeckSelectionProxyScope.BeginUpgrade(__0);
    }

    [HarmonyPostfix]
    private static void FinishSelection(
        ExtraDeckSelectionProxyScope __state,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (__state is not null)
            __result = __state.AwaitSelection(__result);
    }

    [HarmonyFinalizer]
    private static Exception CleanupSynchronousFailure(
        ExtraDeckSelectionProxyScope __state,
        Exception __exception)
    {
        if (__exception is not null)
            __state?.Dispose();
        return __exception;
    }

    internal static void CleanupProxyCards() => ExtraDeckSelectionProxyScope.CleanupAll();
}

[HarmonyPatch(typeof(SmithRestSiteOption), nameof(SmithRestSiteOption.IsEnabled), MethodType.Getter)]
internal static class ExtraDeckSmithAvailabilityPatch
{
    private static readonly System.Reflection.MethodInfo OwnerGetter =
        AccessTools.PropertyGetter(typeof(RestSiteOption), "Owner");

    [HarmonyPostfix]
    private static void IncludeOwnedExtraDeck(SmithRestSiteOption __instance, ref bool __result)
    {
        if (!__result
            && OwnerGetter?.Invoke(__instance, null) is Player player
            && player.GetRelic<ThermalVortexCore>()?.HasUpgradeableExtraDeckCard == true)
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class ExtraDeckUpgradeApplyPatch
{
    [HarmonyPatch(
        typeof(CardCmd),
        nameof(CardCmd.Upgrade),
        [typeof(CardModel), typeof(CardPreviewStyle)])]
    [HarmonyPostfix]
    private static void UpgradeSingle(CardModel __0) => TryWriteBack(__0);

    [HarmonyPatch(
        typeof(CardCmd),
        nameof(CardCmd.Upgrade),
        [typeof(IEnumerable<CardModel>), typeof(CardPreviewStyle)])]
    [HarmonyPostfix]
    private static void UpgradeMany(IEnumerable<CardModel> __0)
    {
        foreach (var card in __0)
            TryWriteBack(card);
    }

    private static void TryWriteBack(CardModel card)
    {
        if (ExtraDeckSelectionProxyScope.TryGetUpgradeTarget(card, out var core, out var index))
            core.TryUpdateExtraDeckUpgrade(index, card.CurrentUpgradeLevel);
    }
}
