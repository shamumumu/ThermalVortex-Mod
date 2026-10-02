using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(
    typeof(NCardGrid),
    nameof(NCardGrid.SetCards),
    [typeof(IReadOnlyList<CardModel>), typeof(PileType), typeof(List<SortingOrders>), typeof(Task)])]
internal static class CardLibraryRarityOrderPatch
{
    private static readonly MethodInfo SortingAlgorithmsGetter =
        AccessTools.PropertyGetter(typeof(NCardGrid), "SortingAlgorithms");

    [HarmonyPrefix]
    private static void ApplyThermalVortexLibraryOrder(
        NCardGrid __instance,
        ref IReadOnlyList<CardModel> cardsToDisplay,
        ref List<SortingOrders> sortingPriority)
    {
        if (__instance is not NCardLibraryGrid
            || cardsToDisplay is null
            || cardsToDisplay.Count <= 1
            || sortingPriority is null
            || sortingPriority.Count == 0
            || sortingPriority[0] is not (SortingOrders.RarityAscending or SortingOrders.RarityDescending)
            || cardsToDisplay.Any(card => card is not ThermalVortexCard))
        {
            return;
        }

        var nativeComparers = GetNativeComparers(__instance);
        if (nativeComparers is null)
            return;

        var descending = sortingPriority[0] == SortingOrders.RarityDescending;
        var secondaryOrders = sortingPriority.Skip(1).ToArray();
        var orderedCards = cardsToDisplay.ToList();
        orderedCards.Sort((left, right) => CompareCards(
            left,
            right,
            descending,
            secondaryOrders,
            nativeComparers));

        cardsToDisplay = orderedCards;
        sortingPriority = [SortingOrders.Ascending];
    }

    private static IReadOnlyDictionary<SortingOrders, Func<CardModel, CardModel, int>> GetNativeComparers(
        NCardGrid grid)
    {
        try
        {
            return SortingAlgorithmsGetter?.Invoke(grid, null)
                as IReadOnlyDictionary<SortingOrders, Func<CardModel, CardModel, int>>;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"Could not resolve card-library sorting algorithms: {ex}");
            return null;
        }
    }

    private static int CompareCards(
        CardModel left,
        CardModel right,
        bool descending,
        IReadOnlyList<SortingOrders> secondaryOrders,
        IReadOnlyDictionary<SortingOrders, Func<CardModel, CardModel, int>> nativeComparers)
    {
        var result = ComparePrimaryAscending(left, right);
        if (result != 0)
        {
            if (GetDeckGroup(left) != GetDeckGroup(right))
                return result;

            return descending ? -result : result;
        }

        foreach (var order in secondaryOrders)
        {
            if (!nativeComparers.TryGetValue(order, out var comparer))
                continue;

            result = comparer(left, right);
            if (result != 0)
                return result;
        }

        return left.Id.CompareTo(right.Id);
    }

    internal static int ComparePrimaryAscending(CardModel left, CardModel right)
    {
        var result = GetDeckGroup(left).CompareTo(GetDeckGroup(right));
        if (result != 0)
            return result;

        return GetVisualRarityRank(left?.Rarity ?? CardRarity.None)
            .CompareTo(GetVisualRarityRank(right?.Rarity ?? CardRarity.None));
    }

    internal static int GetDeckGroup(CardModel card) => card is XyzMonsterCard ? 1 : 0;

    internal static int GetVisualRarityRank(CardRarity rarity) => rarity switch
    {
        CardRarity.Basic or CardRarity.Common => 0,
        CardRarity.Uncommon => 1,
        CardRarity.Rare => 2,
        CardRarity.Ancient => 3,
        _ => 4 + (int)rarity
    };
}
