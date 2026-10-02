using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Patches;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Cards;

internal enum CardSelectionOutcome
{
    Confirmed,
    Back,
    Invalidated
}

internal readonly record struct CardSelectionResult<T>(
    CardSelectionOutcome Outcome,
    IReadOnlyList<T> Selection,
    bool WasPresented)
    where T : CardModel
{
    internal bool Confirmed => Outcome == CardSelectionOutcome.Confirmed;
    internal bool WentBack => Outcome == CardSelectionOutcome.Back;
    internal T FirstOrDefault => Selection is { Count: > 0 } ? Selection[0] : null;
}

internal static class CardSelectionHelper
{
    private const string BackButtonScenePath = "res://scenes/ui/back_button.tscn";
    private static readonly System.Reflection.MethodInfo CompleteSimpleSelectionMethod =
        AccessTools.Method(typeof(NSimpleCardSelectScreen), "CompleteSelection");

    internal static async Task<CardModel> ChooseOne(
        PlayerChoiceContext ctx,
        Player owner,
        IReadOnlyList<CardModel> choices,
        string promptKey,
        bool cancelable = true,
        bool requireManualConfirmation = true,
        CardModel sourceCard = null,
        CardEffectSelectionPreview preview = null)
    {
        var selected = await ChooseMany(
            ctx,
            owner,
            choices,
            promptKey,
            1,
            1,
            cancelable,
            requireManualConfirmation: requireManualConfirmation,
            sourceCard: sourceCard,
            preview: preview);
        return selected.FirstOrDefault();
    }

    internal static async Task<CardModel> ChooseOptionalOne(
        PlayerChoiceContext ctx,
        Player owner,
        IReadOnlyList<CardModel> choices,
        string promptKey,
        CardModel sourceCard = null,
        CardEffectSelectionPreview preview = null)
    {
        var selected = await ChooseMany(
            ctx,
            owner,
            choices,
            promptKey,
            0,
            1,
            cancelable: true,
            automatedSelectionCount: 1,
            sourceCard: sourceCard,
            preview: preview);
        return selected.FirstOrDefault();
    }

    internal static async Task<IReadOnlyList<CardModel>> ChooseMany(
        PlayerChoiceContext ctx,
        Player owner,
        IReadOnlyList<CardModel> choices,
        string promptKey,
        int min,
        int max,
        bool cancelable = true,
        int? automatedSelectionCount = null,
        bool requireManualConfirmation = true,
        CardModel sourceCard = null,
        CardEffectSelectionPreview preview = null)
    {
        if (!IsValidChoiceRequest(ctx, owner, choices, min, max)
            || !TryPrepareChoiceOwners(owner, choices, promptKey))
            return [];

        if (IsAutomatedChoiceContext(ctx))
        {
            var selectionCount = Math.Clamp(automatedSelectionCount ?? min, min, Math.Min(max, choices.Count));
            return choices.Take(selectionCount).ToList();
        }

        var prefs = new CardSelectorPrefs(new LocString("cards", promptKey), min, max)
        {
            Cancelable = cancelable,
            RequireManualConfirmation = requireManualConfirmation,
            PretendCardsCanBePlayed = true
        };
        using var previewRequest = CardEffectPreviewUi.ForSelection(sourceCard, preview);
        var selected = await CardSelectCmd.FromSimpleGrid(ctx, choices, owner, prefs);
        return ResolveSelectedCards(selected, choices);
    }

    internal static Task<CardSelectionResult<T>> ChooseOneWithBack<T>(
        PlayerChoiceContext ctx,
        Player owner,
        IReadOnlyList<T> choices,
        string promptKey,
        bool allowBack,
        bool requireManualConfirmation = false,
        CardModel sourceCard = null,
        CardEffectSelectionPreview preview = null)
        where T : CardModel =>
        ChooseManyWithBack(
            ctx,
            owner,
            choices,
            promptKey,
            1,
            1,
            allowBack,
            requireManualConfirmation: requireManualConfirmation,
            sourceCard: sourceCard,
            preview: preview);

    internal static async Task<CardSelectionResult<T>> ChooseManyWithBack<T>(
        PlayerChoiceContext ctx,
        Player owner,
        IReadOnlyList<T> choices,
        string promptKey,
        int min,
        int max,
        bool allowBack,
        int? automatedSelectionCount = null,
        bool requireManualConfirmation = true,
        CardModel sourceCard = null,
        CardEffectSelectionPreview preview = null)
        where T : CardModel
    {
        if (!IsValidChoiceRequest(ctx, owner, choices, min, max)
            || !TryPrepareChoiceOwners(owner, choices, promptKey))
            return Invalidated<T>();

        if (CombatManager.Instance?.IsEnding == true)
            return Invalidated<T>();

        if (min == 0 && (max == 0 || choices.Count == 0))
            return Confirmed<T>([], wasPresented: false);

        // A mandatory choice with one candidate is fixed rather than a step the
        // player can revisit. Keep the same automatic behavior as SimpleGrid.
        if (min == 1 && max == 1 && choices.Count == 1)
            return Confirmed<T>([choices[0]], wasPresented: false);

        if (!requireManualConfirmation && choices.Count == min)
            return Confirmed(choices.ToList(), wasPresented: false);

        if (IsAutomatedChoiceContext(ctx))
        {
            var upperBound = Math.Min(max, choices.Count);
            var selectionCount = Math.Clamp(automatedSelectionCount ?? min, min, upperBound);
            return Confirmed(choices.Take(selectionCount).ToList(), wasPresented: false);
        }

        if (CardSelectCmd.Selector is { } selector)
        {
            var selected = await selector.GetSelectedCards(choices, min, max);
            return TryResolveSelection(selected, choices, min, max, out var resolved)
                ? Confirmed(resolved, wasPresented: false)
                : Invalidated<T>();
        }

        var runManager = RunManager.Instance;
        var synchronizer = runManager?.PlayerChoiceSynchronizer;
        if (synchronizer is null)
            return Invalidated<T>();

        var choiceId = synchronizer.ReserveChoiceId(owner);
        var choiceBegun = false;
        try
        {
            await ctx.SignalPlayerChoiceBegun(PlayerChoiceOptions.None);
            choiceBegun = true;

            List<int> taggedIndexes;
            if (ShouldSelectLocally(owner, runManager))
            {
                taggedIndexes = await SelectLocalTaggedIndexes(
                    choices,
                    promptKey,
                    min,
                    max,
                    allowBack,
                    requireManualConfirmation,
                    sourceCard,
                    preview);
                synchronizer.SyncLocalChoice(
                    owner,
                    choiceId,
                    PlayerChoiceResult.FromIndexes(taggedIndexes));
            }
            else
            {
                var remoteChoice = await synchronizer.WaitForRemoteChoice(owner, choiceId);
                try
                {
                    taggedIndexes = remoteChoice?.AsIndexes();
                }
                catch (InvalidOperationException)
                {
                    taggedIndexes = null;
                }
            }

            return DecodeTaggedIndexes(taggedIndexes, choices, min, max, allowBack);
        }
        finally
        {
            if (choiceBegun)
                await ctx.SignalPlayerChoiceEnded();
        }
    }

    internal static IReadOnlyList<CardModel> DrawPileCards(Player owner) =>
        PileCards(owner, PileType.Draw);

    internal static IReadOnlyList<CardModel> ExhaustPileCards(Player owner) =>
        PileCards(owner, PileType.Exhaust);

    internal static IReadOnlyList<CardModel> DiscardPileCards(Player owner) =>
        PileCards(owner, PileType.Discard);

    internal static async Task<bool> ReturnToDeck(
        CardModel card,
        CardPilePosition position,
        AbstractModel source)
    {
        if (card is null
            || card.HasBeenRemovedFromState
            || card.Pile?.IsCombatPile != true
            || MonsterFieldService.IsPending(card)
            || MonsterFieldService.IsOnField(card))
        {
            return false;
        }

        // A resolved Extra Deck monster has no pending-summon rollback ticket.
        // Transfer its existing identity and combat state directly, without
        // briefly inserting an undrawable monster into the ordinary draw pile.
        if (card is XyzMonsterCard extraMonster)
            return await ThermalVortexCore.ReturnToExtraDeck(extraMonster);

        var result = await CardPileCmd.Add(card, PileType.Draw, position, source, false);
        return result.success && ReferenceEquals(result.cardAdded, card);
    }

    internal static bool HasCardsAvailableForDraw(Player owner, int requiredCount)
    {
        if (requiredCount <= 0)
            return true;

        try
        {
            return DrawPileCards(owner).Count
                + DiscardPileCards(owner).Count(card => card is not XyzMonsterCard) >= requiredCount;
        }
        catch
        {
            return false;
        }
    }

    internal static async Task RefillDrawPileFromDiscardIfNeeded(
        PlayerChoiceContext ctx,
        Player owner,
        int requiredCount,
        AbstractModel source,
        Func<bool> isValid = null)
    {
        bool IsValid() => isValid?.Invoke() != false;
        if (requiredCount <= 0
            || !IsValid()
            || DrawPileCards(owner).Count >= requiredCount)
        {
            return;
        }

        // Extra Deck monsters return to their own deck before shuffling the
        // ordinary discard cards, and cannot pay an upcoming draw-pile cost.
        foreach (var extraMonster in DiscardPileCards(owner).OfType<XyzMonsterCard>().ToList())
        {
            if (!IsValid())
                return;
            if (IsCurrentPileCard(owner, extraMonster, PileType.Discard))
                await ReturnToDeck(extraMonster, CardPilePosition.Bottom, source);
        }
        if (!IsValid())
            return;

        var refill = DiscardPileCards(owner).Where(card => card is not XyzMonsterCard).ToList();
        var rng = owner?.RunState?.Rng?.Shuffle;
        if (refill.Count == 0 || rng is null)
            return;

        // Keep the existing draw-pile prefix in place. Only the discard pile
        // is shuffled, then appended so the original top cards stay on top.
        refill.StableShuffle(rng);
        Hook.ModifyShuffleOrder(owner.Creature.CombatState, owner, refill, false);
        var movedAny = false;
        foreach (var card in refill)
        {
            if (!IsValid())
                return;
            if (!IsCurrentPileCard(owner, card, PileType.Discard))
                continue;

            movedAny |= await ReturnToDeck(card, CardPilePosition.Bottom, source);
        }

        if (movedAny && IsValid())
            await Hook.AfterShuffle(owner.Creature.CombatState, ctx, owner);
    }

    internal static IReadOnlyList<CardModel> PileCards(Player owner, PileType pileType)
    {
        if (owner?.PlayerCombatState is null)
            return [];

        var pile = CardPile.Get(pileType, owner);
        return pile.Cards
            .Where(card =>
                ReferenceEquals(card?.Owner, owner)
                && ReferenceEquals(card.Pile, pile)
                && !MonsterFieldService.IsPending(card))
            .Distinct(new ReferenceComparer<CardModel>())
            .ToList();
    }

    internal static bool IsCurrentPileCard(Player owner, CardModel card, PileType pileType)
    {
        if (owner?.PlayerCombatState is null
            || card is null
            || !ReferenceEquals(card.Owner, owner)
            || MonsterFieldService.IsPending(card))
        {
            return false;
        }

        var pile = CardPile.Get(pileType, owner);
        return ReferenceEquals(card.Pile, pile)
            && pile.Cards.Any(candidate => ReferenceEquals(candidate, card));
    }

    internal static IReadOnlyList<CardModel> DrawPileMonsters(Player owner) =>
        DrawPileCards(owner).Where(MonsterFieldService.IsFieldMonster).ToList();

    internal static IReadOnlyList<CardModel> ExhaustPileMonsters(Player owner) =>
        ExhaustPileCards(owner).Where(MonsterFieldService.IsFieldMonster).ToList();

    internal static IReadOnlyList<CardModel> DiscardPileMonsters(Player owner) =>
        DiscardPileCards(owner).Where(MonsterFieldService.IsFieldMonster).ToList();

    internal static IReadOnlyList<CardModel> DiscardPileCyberMonsters(Player owner) =>
        DiscardPileCards(owner).Where(CyberSeries.IsCyberMonster).ToList();

    private static IReadOnlyList<CardModel> ResolveSelectedCards(
        IEnumerable<CardModel> selected,
        IReadOnlyList<CardModel> choices)
    {
        if (selected is null)
            return [];

        var remaining = choices.ToList();
        var resolved = new List<CardModel>();
        foreach (var selectedCard in selected)
        {
            var index = remaining.FindIndex(candidate => ReferenceEquals(candidate, selectedCard));
            if (index < 0)
                return [];

            resolved.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return resolved;
    }

    private static async Task<List<int>> SelectLocalTaggedIndexes<T>(
        IReadOnlyList<T> choices,
        string promptKey,
        int min,
        int max,
        bool allowBack,
        bool requireManualConfirmation,
        CardModel sourceCard,
        CardEffectSelectionPreview preview)
        where T : CardModel
    {
        var prefs = new CardSelectorPrefs(new LocString("cards", promptKey), min, max)
        {
            Cancelable = false,
            RequireManualConfirmation = requireManualConfirmation,
            PretendCardsCanBePlayed = true
        };
        using var previewRequest = CardEffectPreviewUi.ForSelection(sourceCard, preview);
        var screen = NSimpleCardSelectScreen.Create(choices.Cast<CardModel>().ToList(), prefs);
        var backRequested = false;

        if (allowBack)
        {
            AttachBackButton(screen, () =>
            {
                if (backRequested)
                    return;

                backRequested = true;
                try
                {
                    CompleteSimpleSelectionMethod.Invoke(screen, null);
                }
                catch
                {
                    NOverlayStack.Instance?.Remove(screen);
                }
            });
        }

        NPlayerHand.Instance?.CancelAllCardPlay();
        NOverlayStack.Instance.Push(screen);

        IEnumerable<CardModel> selected;
        try
        {
            selected = await screen.CardsSelected();
        }
        catch (OperationCanceledException)
        {
            return backRequested ? [0] : [choices.Count + 1];
        }

        if (backRequested)
            return [0];

        if (!TryResolveSelection(selected, choices, min, max, out var resolved))
            return [choices.Count + 1];

        return resolved
            .Select(card => FindReferenceIndex(choices, card) + 1)
            .ToList();
    }

    private static NBackButton AttachBackButton(
        NSimpleCardSelectScreen screen,
        Action onBack)
    {
        if (CompleteSimpleSelectionMethod is null)
            throw new MissingMethodException(typeof(NSimpleCardSelectScreen).FullName, "CompleteSelection");

        var backButton = PreloadManager.Cache
            .GetScene(BackButtonScenePath)
            .Instantiate<NBackButton>();
        backButton.Name = "ThermalVortexChoiceBackButton";
        backButton.Released += _ => onBack();
        backButton.Ready += backButton.Enable;
        screen.AddChild(backButton);
        return backButton;
    }

    private static CardSelectionResult<T> DecodeTaggedIndexes<T>(
        IReadOnlyList<int> taggedIndexes,
        IReadOnlyList<T> choices,
        int min,
        int max,
        bool allowBack)
        where T : CardModel
    {
        if (taggedIndexes is null)
            return Invalidated<T>(wasPresented: true);

        if (taggedIndexes.Count == 0)
        {
            return min == 0
                ? Confirmed<T>([], wasPresented: true)
                : Invalidated<T>(wasPresented: true);
        }

        if (taggedIndexes.Count == 1 && taggedIndexes[0] == 0)
        {
            return allowBack
                ? Back<T>()
                : Invalidated<T>(wasPresented: true);
        }

        if (taggedIndexes.Count < min || taggedIndexes.Count > max)
            return Invalidated<T>(wasPresented: true);

        var selected = new List<T>(taggedIndexes.Count);
        var seenIndexes = new HashSet<int>();
        foreach (var taggedIndex in taggedIndexes)
        {
            var index = taggedIndex - 1;
            if (taggedIndex <= 0
                || index >= choices.Count
                || !seenIndexes.Add(index))
            {
                return Invalidated<T>(wasPresented: true);
            }

            selected.Add(choices[index]);
        }

        return Confirmed(selected, wasPresented: true);
    }

    private static bool TryResolveSelection<T>(
        IEnumerable<CardModel> selected,
        IReadOnlyList<T> choices,
        int min,
        int max,
        out List<T> resolved)
        where T : CardModel
    {
        resolved = [];
        if (selected is null)
            return false;

        var remaining = choices.ToList();
        foreach (var selectedCard in selected)
        {
            var index = remaining.FindIndex(candidate => ReferenceEquals(candidate, selectedCard));
            if (index < 0)
                return false;

            resolved.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return resolved.Count >= min && resolved.Count <= max;
    }

    private static int FindReferenceIndex<T>(IReadOnlyList<T> choices, T selected)
        where T : CardModel
    {
        for (var index = 0; index < choices.Count; index++)
        {
            if (ReferenceEquals(choices[index], selected))
                return index;
        }

        return -1;
    }

    private static bool IsValidChoiceRequest<T>(
        PlayerChoiceContext ctx,
        Player owner,
        IReadOnlyList<T> choices,
        int min,
        int max)
        where T : CardModel =>
        ctx is not null
        && owner is not null
        && choices is not null
        && min >= 0
        && max >= min
        && choices.Count >= min;

    private static bool TryPrepareChoiceOwners<T>(
        Player owner,
        IReadOnlyList<T> choices,
        string promptKey)
        where T : CardModel
    {
        // Owner itself asserts mutability. Reject invalid entries before reading
        // it or changing any candidate, while the choice UI has not been opened.
        if (choices.Any(card => card is null || !card.IsMutable))
        {
            MainFile.Logger.Info(
                $"Card selection rejected prompt={promptKey}: choices must be non-null mutable cards.");
            return false;
        }

        // In combat, NSimpleCardSelectScreen initializes its pile controls from
        // the first card's Owner. An ownerless display option interrupts _Ready
        // and leaves the selection unusable (e.g. Grand Thief's gold offers).
        // Bind the same instances: native synchronization uses list indexes and
        // callers rely on reference identity and option-specific fields. These
        // display cards need no combat registration or pile insertion.
        foreach (var card in choices)
        {
            if (card.Owner is null)
                card.Owner = owner;
        }

        return true;
    }

    private static bool ShouldSelectLocally(Player owner, RunManager runManager) =>
        LocalContext.IsMe(owner)
        && runManager?.NetService?.Type != NetGameType.Replay;

    private static CardSelectionResult<T> Confirmed<T>(
        IReadOnlyList<T> selection,
        bool wasPresented)
        where T : CardModel =>
        new(CardSelectionOutcome.Confirmed, selection, wasPresented);

    private static CardSelectionResult<T> Back<T>()
        where T : CardModel =>
        new(CardSelectionOutcome.Back, [], true);

    private static CardSelectionResult<T> Invalidated<T>(bool wasPresented = false)
        where T : CardModel =>
        new(CardSelectionOutcome.Invalidated, [], wasPresented);

    internal static bool IsAutomatedChoiceContext(PlayerChoiceContext ctx) =>
        ctx?.GetType().Name == "BlockingPlayerChoiceContext";
}
