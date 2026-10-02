using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs.History;

namespace ThermalVortex.ThermalVortexCode.Patches;

// The four-argument native overload delegates here. Patching only this entry
// adds proxies once and preserves the native CanEnchant/additionalFilter pass.
[HarmonyPatch(
    typeof(CardSelectCmd),
    nameof(CardSelectCmd.FromDeckForEnchantment),
    [typeof(Player), typeof(EnchantmentModel), typeof(int), typeof(Func<CardModel, bool>), typeof(CardSelectorPrefs)])]
internal static class ExtraDeckEnchantmentSelectPatch
{
    [HarmonyPrefix]
    private static void AddExtraDeckCards(
        Player __0,
        EnchantmentModel __1,
        out ExtraDeckSelectionProxyScope __state)
    {
        __state = ExtraDeckSelectionProxyScope.BeginEnchantment(__0, __1);
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
}

internal static class ExtraDeckEnchantmentProxyLifecycle
{
    internal static void CleanupProxyCards() => ExtraDeckSelectionProxyScope.CleanupAll();
}

[HarmonyPatch(
    typeof(CardCmd),
    nameof(CardCmd.Enchant),
    [typeof(EnchantmentModel), typeof(CardModel), typeof(decimal)])]
internal static class ExtraDeckEnchantmentApplyPatch
{
    [HarmonyPostfix]
    private static void WriteBack(EnchantmentModel __0, CardModel __1)
    {
        if (!ExtraDeckSelectionProxyScope.TryGetEnchantmentTarget(__1, out var core, out var index)
            || !core.TryUpdateExtraDeckEnchantment(index, __1.Enchantment?.ToSerializable()))
            return;

        // Native Enchant already records proxies still leased into Deck. A completed
        // selection detaches its proxy before applying the permanent enchantment.
        if (__1.Pile?.Type == PileType.Deck)
            return;

        var player = __1.Owner;
        player.RunState.CurrentMapPointHistoryEntry?.GetEntry(player.NetId)
            .CardsEnchanted.Add(new CardEnchantmentHistoryEntry(__1, __0.Id));
    }
}

[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.ClearEnchantment), [typeof(CardModel)])]
internal static class ExtraDeckEnchantmentClearPatch
{
    [HarmonyPostfix]
    private static void WriteBack(CardModel __0)
    {
        if (ExtraDeckSelectionProxyScope.TryGetEnchantmentTarget(__0, out var core, out var index))
            core.TryClearExtraDeckEnchantment(index);
    }
}

[HarmonyPatch(typeof(SelfHelpBook), "GenerateInitialOptions", new Type[0])]
internal static class SelfHelpBookExtraDeckEnchantmentPatch
{
    // Preserve the native Sharp/Nimble/Swift generic calls and their complete
    // DeckFilter. Patching closed generic methods would share a replacement
    // across reference-type arguments and lose the actual enchantment type.
    [HarmonyPrefix]
    private static void IncludeExtraDeckCards(
        SelfHelpBook __instance,
        out ExtraDeckSelectionProxyScope __state) =>
        __state = ExtraDeckSelectionProxyScope.BeginAvailability(__instance.Owner);

    [HarmonyPostfix]
    private static void FinishAvailability(ExtraDeckSelectionProxyScope __state) =>
        __state?.Dispose();

    [HarmonyFinalizer]
    private static Exception CleanupSynchronousFailure(
        ExtraDeckSelectionProxyScope __state,
        Exception __exception)
    {
        __state?.Dispose();
        return __exception;
    }
}
