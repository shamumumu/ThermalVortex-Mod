using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(CookRestSiteOption), "GetRemovableCardCount", [typeof(Player)])]
internal static class ExtraDeckCookAvailabilityPatch
{
    [HarmonyPrefix]
    private static void IncludeExtraDeckCards(Player __0, out ExtraDeckSelectionProxyScope __state) =>
        __state = ExtraDeckSelectionProxyScope.BeginAvailability(__0);

    [HarmonyPostfix]
    private static void FinishCount(ExtraDeckSelectionProxyScope __state) => __state?.Dispose();

    [HarmonyFinalizer]
    private static Exception CleanupSynchronousFailure(
        ExtraDeckSelectionProxyScope __state,
        Exception __exception)
    {
        __state?.Dispose();
        return __exception;
    }
}

[HarmonyPatch(
    typeof(CardSelectCmd),
    nameof(CardSelectCmd.FromDeckForRemoval),
    [typeof(Player), typeof(CardSelectorPrefs), typeof(Func<CardModel, bool>)])]
internal static class ExtraDeckRemovalSelectPatch
{
    [HarmonyPrefix]
    private static void AddExtraDeckCards(Player __0, out ExtraDeckSelectionProxyScope __state) =>
        __state = ExtraDeckSelectionProxyScope.BeginRemoval(__0);

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

// The single-card overload delegates here. Reattach only selected proxies so
// the native removal retains its hooks, history, preview, and state cleanup.
[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.RemoveFromDeck),
    [typeof(IReadOnlyList<CardModel>), typeof(bool)])]
internal static class ExtraDeckRemovalApplyPatch
{
    [HarmonyPrefix]
    private static void PrepareRemoval(IReadOnlyList<CardModel> __0, out ExtraDeckRemovalApplication __state) =>
        __state = ExtraDeckRemovalApplication.Begin(__0);

    [HarmonyPostfix]
    private static void FinishRemoval(ExtraDeckRemovalApplication __state, ref Task __result)
    {
        if (__state is not null)
            __result = __state.AwaitNative(__result);
    }

    [HarmonyFinalizer]
    private static Exception CleanupSynchronousFailure(
        ExtraDeckRemovalApplication __state,
        Exception __exception)
    {
        if (__exception is not null)
            __state?.Dispose();
        return __exception;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.RemoveFromCurrentPile), [typeof(bool)])]
internal static class ExtraDeckRemovalCommitPatch
{
    [HarmonyPrefix]
    private static void CaptureRemoval(CardModel __instance, out bool __state) =>
        __state = ExtraDeckRemovalApplication.IsPending(__instance) && __instance.Pile?.Type == PileType.Deck;

    [HarmonyPostfix]
    private static void RemoveOwnedEntry(CardModel __instance, bool __state)
    {
        if (__state && __instance.Pile is null)
            ExtraDeckRemovalApplication.Commit(__instance);
    }
}

internal sealed class ExtraDeckRemovalApplication : IDisposable
{
    private static readonly Dictionary<CardModel, RemovalTarget> Pending = new(new ReferenceComparer<CardModel>());
    private static readonly HashSet<ExtraDeckRemovalApplication> Active = [];
    private readonly List<RemovalTarget> _targets = [];
    private bool _disposed;

    internal static ExtraDeckRemovalApplication Begin(IReadOnlyList<CardModel> cards)
    {
        ExtraDeckRemovalApplication application = null;
        try
        {
            foreach (var card in cards.Distinct(new ReferenceComparer<CardModel>()))
            {
                if (!ExtraDeckSelectionProxyScope.IsRemovalProxy(card))
                    continue;
                if (!ExtraDeckSelectionProxyScope.TryGetRemovalTarget(card, out var core, out var identity))
                    throw new InvalidOperationException("The selected Extra Deck entry is no longer owned.");
                if (Pending.ContainsKey(card))
                    throw new InvalidOperationException("The selected Extra Deck entry is already being removed.");

                application ??= new ExtraDeckRemovalApplication();
                Active.Add(application);
                var target = new RemovalTarget(card, core, identity);
                application._targets.Add(target);
                Pending.Add(card, target);
                if (!card.Owner.Deck.Cards.Any(candidate => ReferenceEquals(candidate, card)))
                {
                    target.AddedToDeck = true;
                    card.Owner.Deck.AddInternal(card, card.Owner.Deck.Cards.Count, false);
                }
            }
            return application;
        }
        catch
        {
            application?.Dispose();
            throw;
        }
    }

    internal static bool IsPending(CardModel card) => Pending.ContainsKey(card);

    internal static void Commit(CardModel card)
    {
        if (!Pending.TryGetValue(card, out var target))
            return;
        if (!ExtraDeckSelectionProxyScope.TryGetRemovalTarget(card, out var core, out var identity)
            || !ReferenceEquals(core, target.Core)
            || !ReferenceEquals(identity, target.Identity)
            || !core.TryRemoveOwnedExtraDeckEntry(identity))
        {
            throw new InvalidOperationException("The selected Extra Deck entry changed before removal completed.");
        }
        Pending.Remove(card);
    }

    internal async Task AwaitNative(Task nativeTask)
    {
        try
        {
            if (nativeTask is not null)
                await nativeTask;
        }
        finally
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Active.Remove(this);
        foreach (var target in _targets)
        {
            if (!Pending.TryGetValue(target.Card, out var current) || !ReferenceEquals(current, target))
                continue;
            Pending.Remove(target.Card);
            if (!target.AddedToDeck)
                continue;
            try
            {
                var deck = target.Core.Owner.Deck;
                if (deck.Cards.Any(card => ReferenceEquals(card, target.Card)))
                    deck.RemoveInternal(target.Card, false);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"ExtraDeck removal proxy cleanup failed card={target.Card.GetType().Name} error={ex}");
            }
        }
    }

    internal static void CleanupAll()
    {
        foreach (var application in Active.ToList())
            application.Dispose();
    }

    private sealed class RemovalTarget(CardModel card, ThermalVortexCore core, object identity)
    {
        internal CardModel Card { get; } = card;
        internal ThermalVortexCore Core { get; } = core;
        internal object Identity { get; } = identity;
        internal bool AddedToDeck { get; set; }
    }
}
