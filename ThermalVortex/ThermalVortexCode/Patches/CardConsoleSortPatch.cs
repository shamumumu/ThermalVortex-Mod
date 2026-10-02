using System.Diagnostics;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(CardConsoleCmd), nameof(CardConsoleCmd.GetArgumentCompletions))]
internal static class CardConsoleAutocompleteSortPatch
{
    [HarmonyPostfix]
    private static void SortCandidates(CompletionResult __result)
    {
        ConsoleCompletionSort.SortCandidates(__result);
    }
}

[HarmonyPatch(typeof(EventConsoleCmd), nameof(EventConsoleCmd.GetArgumentCompletions))]
internal static class EventConsoleAutocompleteSortPatch
{
    [HarmonyPostfix]
    private static void SortCandidates(CompletionResult __result)
    {
        ConsoleCompletionSort.SortCandidates(__result);
    }
}

[HarmonyPatch(typeof(AncientConsoleCmd), nameof(AncientConsoleCmd.GetArgumentCompletions))]
internal static class AncientConsoleAutocompleteSortPatch
{
    [HarmonyPostfix]
    private static void SortCandidates(CompletionResult __result)
    {
        ConsoleCompletionSort.SortCandidates(__result);
    }
}

internal static class ConsoleCompletionSort
{
    internal static void SortCandidates(CompletionResult result)
    {
        result?.Candidates?.Sort(StringComparer.CurrentCultureIgnoreCase);
    }
}

[HarmonyPatch(
    typeof(NSimpleCardSelectScreen),
    nameof(NSimpleCardSelectScreen.Create),
    [typeof(IReadOnlyList<CardModel>), typeof(CardSelectorPrefs)])]
internal static class CardConsoleSelectScreenSortPatch
{
    [HarmonyPrefix]
    private static void SortCardConsoleChoices(ref IReadOnlyList<CardModel> cards, ref CardSelectorPrefs prefs)
    {
        if (cards is null || cards.Count <= 1 || !IsCardConsoleCommand())
            return;

        if (prefs.Comparison is null)
            prefs = prefs with { Comparison = CompareCardsByTitle };

        cards = cards.OrderBy(card => card, Comparer<CardModel>.Create(CompareCardsByTitle)).ToList();
    }

    private static bool IsCardConsoleCommand()
    {
        var frames = new StackTrace().GetFrames();
        if (frames is null)
            return false;

        return frames.Any(frame =>
            frame.GetMethod()?.DeclaringType?.FullName?.Contains(nameof(CardConsoleCmd), StringComparison.Ordinal) == true);
    }

    private static int CompareCardsByTitle(CardModel left, CardModel right)
    {
        var result = string.Compare(left?.Title, right?.Title, StringComparison.CurrentCultureIgnoreCase);
        if (result != 0)
            return result;

        return string.Compare(left?.Id?.ToString(), right?.Id?.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
