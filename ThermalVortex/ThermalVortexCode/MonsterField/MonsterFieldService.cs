using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.MonsterField;

internal static class MonsterFieldService
{
    public const int BaseCapacity = 3;

    private static readonly HashSet<CardModel> PendingFieldCards =
        new(new ReferenceComparer<CardModel>());
    private static readonly Dictionary<CardModel, long> FieldOrderReservations = new(new ReferenceComparer<CardModel>());
    private static readonly Dictionary<Player, int> TemporaryCapacityBonuses = [];
    private static readonly Dictionary<Player, int> VisualMutationDepths = new(new ReferenceComparer<Player>());
    private static readonly Dictionary<Player, int> ManualRepositionUnlocks = [];
    private static readonly HashSet<CardPile> OrderingAttachedPiles = new(new ReferenceComparer<CardPile>());
    private static readonly HashSet<CardPile> NormalizingPiles = new(new ReferenceComparer<CardPile>());
    private static long NextFieldOrder;

    internal static event Action<Player> VisualMutationCompleted;

    public static bool IsFieldMonster(CardModel card) =>
        card is MonsterCard or XyzMonsterCard;

    internal static void ResetAll()
    {
        MonsterFieldUpgradeLockService.ResetAll();
        PendingFieldCards.Clear();
        FieldOrderReservations.Clear();
        TemporaryCapacityBonuses.Clear();
        VisualMutationDepths.Clear();
        ManualRepositionUnlocks.Clear();
        OrderingAttachedPiles.Clear();
        NormalizingPiles.Clear();
        NextFieldOrder = 0;
    }

    public static bool IsOnField(CardModel card) =>
        card?.Pile?.Type == MonsterFieldPile.FieldPileType;

    internal static Func<bool> CaptureMaterialUseValidity(Player player)
    {
        var creature = player?.Creature;
        var combatState = creature?.CombatState;
        var playerCombatState = player?.PlayerCombatState;
        var manager = CombatManager.Instance;
        return () => combatState is not null
            && playerCombatState is not null
            && ReferenceEquals(player?.Creature, creature)
            && ReferenceEquals(creature?.CombatState, combatState)
            && ReferenceEquals(player?.PlayerCombatState, playerCombatState)
            && ReferenceEquals(CombatManager.Instance, manager)
            && manager is { IsInProgress: true, IsOverOrEnding: false }
            && creature?.IsAlive == true;
    }

    public static bool CanBeAttackTarget(CardModel card) =>
        !CyberDevourState.PreventsEnemyAttackTarget(card)
        && (card is not IMonsterFieldAttackTargetRule rule || rule.CanBeAttackTarget);

    public static bool CanPlaceOnField(CardModel card) =>
        CanPlaceOnFieldAfterRemoving(card, Array.Empty<CardModel>());

    internal static bool CanNormalSummonOnField(CardModel card) =>
        CanPlaceOnField(card);

    internal static int GetRequiredFieldDeparturesForPlacement(CardModel card)
    {
        if (!IsFieldMonster(card) || IsOnField(card))
            return 0;

        var player = TryGetOwner(card);
        if (player?.PlayerCombatState is null)
            return 0;

        if (IsCapacityLimitIgnored(player))
            return 0;

        return Math.Max(0, CountFieldAndPending(player, card) - GetCapacity(player) + 1);
    }

    internal static bool CanPlaceOnFieldAfterRemoving(
        CardModel card,
        IEnumerable<CardModel> removedCards)
    {
        if (!IsFieldMonster(card) || IsOnField(card))
            return true;

        var player = TryGetOwner(card);
        return player?.PlayerCombatState is null
            || CanPlaceOnFieldAfterRemoving(player, removedCards, card);
    }

    internal static bool CanPlaceOnFieldAfterRemoving(
        Player player,
        IEnumerable<CardModel> removedCards) =>
        player?.PlayerCombatState is null
        || CanPlaceOnFieldAfterRemoving(player, removedCards, null);

    private static bool CanPlaceOnFieldAfterRemoving(
        Player player,
        IEnumerable<CardModel> removedCards,
        CardModel except)
    {
        var fieldMonsters = GetMonsters(player);
        var projectedRemovals = (removedCards ?? Array.Empty<CardModel>())
            .Where(card => card is not null && fieldMonsters.Any(field => ReferenceEquals(field, card)))
            .ToHashSet(new ReferenceComparer<CardModel>());

        if (IsCapacityLimitIgnored(player))
            return true;

        return CountFieldAndPending(player, except) - projectedRemovals.Count < GetCapacity(player);
    }

    public static async Task<bool> SpecialSummon(
        PlayerChoiceContext ctx,
        CardModel card,
        AbstractModel source)
    {
        if (!IsFieldMonster(card) || IsOnField(card) || !CanPlaceOnField(card))
            return false;

        using var resolution = MillenniumResolution.EnterSummon(ctx, card);
        var entered = false;
        void MarkEntered(MonsterFieldEnterEvent enterEvent)
        {
            if (ReferenceEquals(enterEvent.Card, card))
                entered = true;
        }
        MonsterFieldEventService.MonsterEnteredVisual += MarkEntered;
        MonsterFieldUpgradeLockService.BeginAwaitingFieldEntry(card);
        MarkPending(card);
        try
        {
            await CardPileCmd.Add(card, MonsterFieldPile.FieldPileType, CardPilePosition.Top, source, false);
            entered |= IsOnField(card);
            UnmarkPending(card, keepFieldOrderReservation: IsOnField(card));
            await resolution.Complete();
            return entered;
        }
        finally
        {
            MonsterFieldEventService.MonsterEnteredVisual -= MarkEntered;
            if (!IsOnField(card))
                MonsterFieldUpgradeLockService.CancelAwaitingFieldEntry(card);
            UnmarkPending(card, keepFieldOrderReservation: IsOnField(card));
        }
    }

    internal static async Task<bool> RestoreDestroyedMonster(
        PlayerChoiceContext ctx,
        CardModel card,
        AbstractModel source)
    {
        if (!IsFieldMonster(card) || IsOnField(card) || TryGetOwner(card)?.PlayerCombatState is null)
            return IsOnField(card);

        using var resolution = MillenniumResolution.EnterSummon(ctx, card);
        var entered = false;
        void MarkEntered(MonsterFieldEnterEvent enterEvent)
        {
            if (ReferenceEquals(enterEvent.Card, card))
                entered = true;
        }
        MonsterFieldEventService.MonsterEnteredVisual += MarkEntered;
        MonsterFieldUpgradeLockService.BeginAwaitingFieldEntry(card);
        MarkPending(card);
        try
        {
            await CardPileCmd.Add(card, MonsterFieldPile.FieldPileType, CardPilePosition.Top, source, false);
            entered |= IsOnField(card);
            UnmarkPending(card, keepFieldOrderReservation: IsOnField(card));
            await resolution.Complete();
            return entered;
        }
        finally
        {
            MonsterFieldEventService.MonsterEnteredVisual -= MarkEntered;
            if (!IsOnField(card))
                MonsterFieldUpgradeLockService.CancelAwaitingFieldEntry(card);
            UnmarkPending(card, keepFieldOrderReservation: IsOnField(card));
        }
    }

    public static void MarkPending(CardModel card)
    {
        if (!IsFieldMonster(card))
            return;

        PendingFieldCards.Add(card);
        EnsureExistingFieldOrders(card);
        EnsureFieldOrder(card);
    }

    internal static bool IsPending(CardModel card) =>
        card is not null && PendingFieldCards.Contains(card);

    public static void UnmarkPending(CardModel card, bool keepFieldOrderReservation = false)
    {
        if (card is null)
            return;

        PendingFieldCards.Remove(card);
        if (!keepFieldOrderReservation)
            FieldOrderReservations.Remove(card);
    }

    public static IReadOnlyList<CardModel> GetMonsters(Player player)
    {
        var pile = GetPile(player);
        if (pile is null)
            return [];

        var monsters = pile.Cards.Where(IsFieldMonster).ToList();
        MonsterFieldHealthService.Sync(player, monsters);
        return monsters;
    }

    /// <summary>
    /// Returns the committed monster sequence intended for presentation.  UI
    /// callers use this entry point so rule-only projections can remain an
    /// implementation detail of placement checks.
    /// </summary>
    public static IReadOnlyList<CardModel> GetDisplayMonsters(Player player) =>
        GetMonsters(player);

    internal static IReadOnlyList<CardModel> GetMonstersForPreview(Player player) =>
        player?.PlayerCombatState is not { } combatState
            ? []
            : CustomPiles.GetCustomPile(combatState, MonsterFieldPile.FieldPileType)?.Cards
                .Where(IsFieldMonster).ToList() ?? [];

    internal static bool HasFieldOrPendingMonsterForPreview<T>(Player player)
        where T : CardModel =>
        GetFieldOrPendingMonsters(player, forPreview: true).Any(MatchesMonsterIdentity<T>);

    public static bool HasFieldOrPendingMonster<T>(Player player)
        where T : CardModel
        => GetFieldOrPendingMonsters(player, forPreview: false).Any(MatchesMonsterIdentity<T>);

    internal static int CountFieldOrPendingMonsters(Player player, CardModel except) =>
        CountFieldAndPending(player, except);

    internal static int CountFieldOrPendingMonstersForPreview(Player player, CardModel except) =>
        GetFieldOrPendingMonsters(player, forPreview: true)
            .Count(card => !ReferenceEquals(card, except));

    private static bool MatchesMonsterIdentity<T>(CardModel card) where T : CardModel
        => MonsterIdentity.Matches<T>(card);

    public static int Count(Player player) => GetMonsters(player).Count;

    public static bool IsFull(Player player) =>
        player?.PlayerCombatState is not null
        && !IsCapacityLimitIgnored(player)
        && CountFieldAndPending(player, null) >= GetCapacity(player);

    public static bool IsCapacityLimitIgnored(Player player) =>
        player?.Creature?.HasPower<LimiterRemovalPower>() == true;

    public static int GetVisibleSlotCount(Player player) =>
        Math.Max(GetDisplayCapacity(player), GetDisplayMonsters(player).Count);

    public static int GetCapacity(Player player) =>
        CalculateCapacity(player, includeTemporaryBonuses: true);

    /// <summary>
    /// Capacity represented by persistent combat state. Temporary bonuses are
    /// rule projections used to validate replacements before their source has
    /// left the field; exposing them would create phantom UI slots.
    /// </summary>
    public static int GetDisplayCapacity(Player player) =>
        CalculateCapacity(player, includeTemporaryBonuses: false);

    private static int CalculateCapacity(Player player, bool includeTemporaryBonuses)
    {
        var capacity = BaseCapacity;

        var creature = player?.Creature;
        if (creature is not null)
        {
            capacity += creature.Powers
                .OfType<IMonsterFieldCapacityModifier>()
                .Sum(modifier => modifier.MonsterFieldCapacityBonus);
        }

        if (player?.Relics is not null)
        {
            capacity += player.Relics
                .OfType<IMonsterFieldCapacityModifier>()
                .Sum(modifier => modifier.MonsterFieldCapacityBonus);
        }

        if (includeTemporaryBonuses
            && player is not null
            && TemporaryCapacityBonuses.TryGetValue(player, out var temporaryBonus))
        {
            capacity += temporaryBonus;
        }

        return Math.Max(0, capacity);
    }

    public static int GetCapacity(Creature creature) => GetCapacity(creature?.Player);

    public static Task SendToDiscard(IEnumerable<CardModel> cards, AbstractModel source) =>
        SendToDiscard(null, cards, source, MonsterFieldLeaveReason.FusionMaterial);

    public static Task SendToDiscard(
        PlayerChoiceContext ctx,
        IEnumerable<CardModel> cards,
        AbstractModel source,
        MonsterFieldLeaveReason reason) =>
        SendToDiscardIf(ctx, cards, source, reason, null);

    // Damage queues must still refer to the same field incarnation after
    // earlier deaths and leaving callbacks have finished.
    internal static async Task SendToDiscardIf(
        PlayerChoiceContext ctx,
        IEnumerable<CardModel> cards,
        AbstractModel source,
        MonsterFieldLeaveReason reason,
        Func<CardModel, bool> canLeave,
        Action<CardModel> onDeparted = null)
    {
        var cardList = cards?.ToList() ?? [];
        var isMaterialUseValid = CaptureMaterialUseValidity(cardList, reason);
        using var visualMutation = BeginVisualMutation(cardList.Where(IsOnField));
        foreach (var card in cardList)
        {
            if (isMaterialUseValid?.Invoke() == false)
                return;

            if (canLeave?.Invoke(card) == false)
                continue;

            var wasOnField = IsOnField(card);
            if (!wasOnField)
            {
                if (IsFieldMonster(card) && isMaterialUseValid is not null)
                {
                    var offFieldExhaustInsteadOfDiscard = ShouldExhaustInsteadOfDiscard(card);
                    await ResolveOffFieldMaterialUse(
                        ctx,
                        card,
                        source,
                        reason,
                        isMaterialUseValid,
                        () => SendUsedCardToDiscardOrExhaust(
                            ctx, card, source, offFieldExhaustInsteadOfDiscard));
                }

                continue;
            }

            using var copyLeave = ChaosPhantom.BeginFieldLeave(card);
            MonsterFieldHealthService.RememberCopyMaxHp(card);
            // Capture the result before leave listeners run. A card that was
            // marked to exhaust when it was used must not fall back to discard
            // if a listener changes its transient state during resolution.
            var exhaustInsteadOfDiscard = reason != MonsterFieldLeaveReason.Transformation
                && ShouldExhaustInsteadOfDiscard(card);
            var health = MonsterFieldHealthService.PeekHealth(card);
            var leaveEvent = new MonsterFieldLeaveEvent(card, health, reason, source);
            await MonsterFieldEventService.NotifyMonsterLeavingField(
                ctx,
                leaveEvent,
                canLeave is null ? null : () => canLeave(card));
            if (isMaterialUseValid?.Invoke() == false)
                return;

            if (canLeave?.Invoke(card) == false)
                continue;

            UnmarkPending(card);
            var departingPile = card.Pile;
            void RecordDeparture(CardModel removed)
            {
                if (ReferenceEquals(removed, card))
                    onDeparted?.Invoke(card);
            }
            if (onDeparted is not null)
                departingPile.CardRemoved += RecordDeparture;
            try
            {
                await SendUsedCardToDiscardOrExhaust(
                    ctx,
                    card,
                    source,
                    exhaustInsteadOfDiscard);
            }
            finally
            {
                if (onDeparted is not null)
                    departingPile.CardRemoved -= RecordDeparture;
            }
            if (isMaterialUseValid?.Invoke() == false)
                return;

            var leftField = !IsOnField(card);
            if (leftField)
                MonsterFieldHealthService.Clear(card);

            if (leftField || isMaterialUseValid is not null)
                await MonsterFieldEventService.NotifyMonsterLeftFieldResolved(ctx, leaveEvent);
        }
    }

    /// <summary>
    /// Routes a used card to its result pile without publishing material-use
    /// or field-leave events. Material payments use the explicit reason on
    /// SendToDiscard so their own effects resolve as well.
    /// </summary>
    internal static Task SendUsedCardToDiscardOrExhaust(
        PlayerChoiceContext ctx,
        CardModel card,
        AbstractModel source) =>
        SendUsedCardToDiscardOrExhaust(
            ctx,
            card,
            source,
            ShouldExhaustInsteadOfDiscard(card));

    public static Task SendToDraw(IEnumerable<CardModel> cards, AbstractModel source) =>
        SendToDraw(null, cards, source, MonsterFieldLeaveReason.FusionMaterial, CardPilePosition.Top);

    public static async Task SendToDraw(
        PlayerChoiceContext ctx,
        IEnumerable<CardModel> cards,
        AbstractModel source,
        MonsterFieldLeaveReason reason,
        CardPilePosition position = CardPilePosition.Top,
        Func<XyzMonsterCard, Task> returnExtraDeckMaterial = null)
    {
        var cardList = cards?.ToList() ?? [];
        var isMaterialUseValid = CaptureMaterialUseValidity(cardList, reason);
        using var visualMutation = BeginVisualMutation(cardList.Where(IsOnField));
        foreach (var card in cardList)
        {
            if (isMaterialUseValid?.Invoke() == false)
                return;

            if (!IsFieldMonster(card))
                continue;

            var wasOnField = IsOnField(card);
            if (wasOnField)
            {
                using var copyLeave = ChaosPhantom.BeginFieldLeave(card);
                MonsterFieldHealthService.RememberCopyMaxHp(card);
                var health = MonsterFieldHealthService.PeekHealth(card);
                var leaveEvent = new MonsterFieldLeaveEvent(card, health, reason, source);
                await MonsterFieldEventService.NotifyMonsterLeavingField(
                    ctx,
                    leaveEvent);
                if (isMaterialUseValid?.Invoke() == false)
                    return;

                UnmarkPending(card);

                if (card is XyzMonsterCard fieldExtraMonster)
                    await MoveExtraDeckMaterial(fieldExtraMonster);
                else
                    await CardPileCmd.Add(card, PileType.Draw, position, source, false);

                if (isMaterialUseValid?.Invoke() == false)
                    return;

                var leftField = !IsOnField(card);
                if (leftField)
                    MonsterFieldHealthService.Clear(card);

                if (leftField || isMaterialUseValid is not null)
                    await MonsterFieldEventService.NotifyMonsterLeftFieldResolved(ctx, leaveEvent);

                continue;
            }

            if (isMaterialUseValid is not null)
            {
                await ResolveOffFieldMaterialUse(
                    ctx,
                    card,
                    source,
                    reason,
                    isMaterialUseValid,
                    () => card is XyzMonsterCard offFieldExtraMonster
                        ? MoveExtraDeckMaterial(offFieldExtraMonster)
                        : CardPileCmd.Add(card, PileType.Draw, position, source, false));
            }
            else if (card is XyzMonsterCard extraMonster)
                await MoveExtraDeckMaterial(extraMonster);
            else
                await CardPileCmd.Add(card, PileType.Draw, position, source, false);
        }

        // Cyberload stages its Extra Deck material here without exhausting it.
        // The caller finalizes the virtual return only after this method has
        // completed both field and off-field resolved material effects.
        Task MoveExtraDeckMaterial(XyzMonsterCard card) =>
            returnExtraDeckMaterial is not null
                ? returnExtraDeckMaterial(card)
                : ExhaustIfNeeded(ctx, card);
    }

    public static Task SendToExhaust(
        PlayerChoiceContext ctx,
        IEnumerable<CardModel> cards,
            AbstractModel source) =>
        SendToExhaust(ctx, cards, source, MonsterFieldLeaveReason.Exhaust);

    public static async Task SendToExhaust(
        PlayerChoiceContext ctx,
        IEnumerable<CardModel> cards,
        AbstractModel source,
        MonsterFieldLeaveReason reason)
    {
        var cardList = cards?.ToList() ?? [];
        var isMaterialUseValid = CaptureMaterialUseValidity(cardList, reason);
        using var visualMutation = BeginVisualMutation(cardList.Where(IsOnField));
        foreach (var card in cardList)
        {
            if (isMaterialUseValid?.Invoke() == false)
                return;

            if (!IsFieldMonster(card))
                continue;

            var wasOnField = IsOnField(card);
            if (wasOnField)
            {
                using var copyLeave = ChaosPhantom.BeginFieldLeave(card);
                MonsterFieldHealthService.RememberCopyMaxHp(card);
                var health = MonsterFieldHealthService.PeekHealth(card);
                var leaveEvent = new MonsterFieldLeaveEvent(card, health, reason, source);
                await MonsterFieldEventService.NotifyMonsterLeavingField(
                    ctx,
                    leaveEvent);
                if (isMaterialUseValid?.Invoke() == false)
                    return;

                UnmarkPending(card);

                await ExhaustIfNeeded(ctx, card);
                if (isMaterialUseValid?.Invoke() == false)
                    return;

                var leftField = !IsOnField(card);
                if (leftField)
                    MonsterFieldHealthService.Clear(card);

                if (leftField || isMaterialUseValid is not null)
                    await MonsterFieldEventService.NotifyMonsterLeftFieldResolved(ctx, leaveEvent);

                continue;
            }

            if (isMaterialUseValid is not null)
            {
                await ResolveOffFieldMaterialUse(
                    ctx,
                    card,
                    source,
                    reason,
                    isMaterialUseValid,
                    () => ExhaustIfNeeded(ctx, card));
            }
            else
                await ExhaustIfNeeded(ctx, card);
        }
    }

    private static Func<bool> CaptureMaterialUseValidity(
        IReadOnlyList<CardModel> cards,
        MonsterFieldLeaveReason reason) =>
        reason is MonsterFieldLeaveReason.FusionMaterial or MonsterFieldLeaveReason.Material
            ? CaptureMaterialUseValidity(TryGetOwner(cards.FirstOrDefault(IsFieldMonster)))
            : null;

    private static async Task ResolveOffFieldMaterialUse(
        PlayerChoiceContext ctx,
        CardModel card,
        AbstractModel source,
        MonsterFieldLeaveReason reason,
        Func<bool> isMaterialUseValid,
        Func<Task> moveCard)
    {
        if (!isMaterialUseValid())
            return;

        // The source was captured before any callbacks. An exhaust listener
        // may summon this card, but that must not turn this material payment
        // into a second field departure or suppress its resolved effects.
        var leaveEvent = CreateOffFieldMaterialEvent(card, source, reason);
        await MonsterFieldEventService.NotifyOffFieldMaterialUsed(ctx, leaveEvent);
        if (!isMaterialUseValid())
            return;

        await moveCard();
        if (!isMaterialUseValid())
            return;

        await MonsterFieldEventService.NotifyOffFieldMaterialUseResolved(ctx, leaveEvent);
    }

    public static Task UseMaterialsToDiscard(
        PlayerChoiceContext ctx,
        IEnumerable<CardModel> cards,
        AbstractModel source) =>
        SendToDiscard(ctx, cards, source, MonsterFieldLeaveReason.Material);

    internal static async Task<bool> TryUseFieldMaterialToDiscard(
        PlayerChoiceContext ctx,
        Player owner,
        CardModel material,
        AbstractModel source)
    {
        var isMaterialUseValid = CaptureMaterialUseValidity(owner);
        if (!isMaterialUseValid())
            return false;

        var fieldPile = GetPile(owner);
        bool IsCurrentMaterial() =>
            fieldPile is not null
            && IsFieldMonster(material)
            && ReferenceEquals(material.Owner, owner)
            && ReferenceEquals(GetPile(owner), fieldPile)
            && ReferenceEquals(material.Pile, fieldPile)
            && fieldPile.Cards.Any(card => ReferenceEquals(card, material));
        if (!IsCurrentMaterial())
            return false;

        var history = CombatManager.Instance.History;
        var exhaustInsteadOfDiscard = ShouldExhaustInsteadOfDiscard(material);
        if (exhaustInsteadOfDiscard && history is null)
            return false;

        bool committed;
        using (BeginVisualMutation([material]))
        using (ChaosPhantom.BeginFieldLeave(material))
        {
            MonsterFieldHealthService.RememberCopyMaxHp(material);
            var health = MonsterFieldHealthService.PeekHealth(material);
            var leaveEvent = new MonsterFieldLeaveEvent(
                material, health, MonsterFieldLeaveReason.Material, source);
            await MonsterFieldEventService.NotifyMonsterLeavingField(ctx, leaveEvent);
            if (!isMaterialUseValid() || !IsCurrentMaterial())
                return false;

            UnmarkPending(material);
            if (exhaustInsteadOfDiscard)
            {
                var historyCount = history.Entries.Count();
                await CardCmd.Exhaust(ResolveContext(ctx), material, false, false);
                committed = history.Entries.Skip(historyCount)
                    .OfType<CardExhaustedEntry>()
                    .Any(entry => ReferenceEquals(entry.Card, material));
            }
            else
            {
                var result = await CardPileCmd.Add(
                    material, PileType.Discard, CardPilePosition.Top, source, false);
                committed = result.success && ReferenceEquals(result.cardAdded, material);
            }

            // A completed payment remains valid if its callbacks summon the
            // original monster again. Its final pile is not the payment receipt.
            if (!committed || !isMaterialUseValid())
                return false;

            if (!IsOnField(material))
                MonsterFieldHealthService.Clear(material);
            await MonsterFieldEventService.NotifyMonsterLeftFieldResolved(ctx, leaveEvent);
        }

        return committed && isMaterialUseValid();
    }

    public static Task UseMaterialsToExhaust(
        PlayerChoiceContext ctx,
        IEnumerable<CardModel> cards,
        AbstractModel source) =>
        SendToExhaust(ctx, cards, source, MonsterFieldLeaveReason.Material);

    public static Task ClearFieldToDiscard(
        PlayerChoiceContext ctx,
        Player player,
        AbstractModel source) =>
        SendToDiscard(ctx, GetMonsters(player), source, MonsterFieldLeaveReason.FieldClear);

    private static async Task ExhaustIfNeeded(PlayerChoiceContext ctx, CardModel card)
    {
        if (card?.Pile?.Type == PileType.Exhaust)
            return;

        await CardCmd.Exhaust(ResolveContext(ctx), card, false, false);
    }

    internal static bool ShouldExhaustInsteadOfDiscard(CardModel card) =>
        card is XyzMonsterCard
        || card?.ExhaustOnNextPlay == true
        || card?.Keywords.Contains(CardKeyword.Exhaust) == true;

    private static async Task SendUsedCardToDiscardOrExhaust(
        PlayerChoiceContext ctx,
        CardModel card,
        AbstractModel source,
        bool exhaustInsteadOfDiscard)
    {
        if (card is null)
            return;

        if (exhaustInsteadOfDiscard)
            await ExhaustIfNeeded(ctx, card);
        else
            await CardPileCmd.Add(card, PileType.Discard, CardPilePosition.Top, source, false);
    }

    private static PlayerChoiceContext ResolveContext(PlayerChoiceContext ctx) =>
        ctx ?? new BlockingPlayerChoiceContext();

    private static MonsterFieldLeaveEvent CreateOffFieldMaterialEvent(
        CardModel card,
        AbstractModel source,
        MonsterFieldLeaveReason reason)
    {
        var maxHp = Math.Max(1, MonsterFieldHealthService.GetInitialMaxHp(card));
        return new MonsterFieldLeaveEvent(
            card,
            new MonsterFieldHealth(maxHp, maxHp),
            reason,
            source);
    }

    public static bool IsManualRepositionUnlocked(Player player) =>
        player is not null
        && ManualRepositionUnlocks.TryGetValue(player, out var unlockCount)
        && unlockCount > 0;

    public static bool MoveMonster(Player player, int fromIndex, int toIndex) =>
        IsManualRepositionUnlocked(player) && MoveMonsterInternal(player, fromIndex, toIndex);

    public static bool SwapMonsters(Player player, int leftIndex, int rightIndex) =>
        IsManualRepositionUnlocked(player) && SwapMonstersInternal(player, leftIndex, rightIndex);

    public static bool MoveMonsterByCardEffect(Player player, int fromIndex, int toIndex) =>
        MoveMonsterInternal(player, fromIndex, toIndex);

    public static bool SwapMonstersByCardEffect(Player player, int leftIndex, int rightIndex) =>
        SwapMonstersInternal(player, leftIndex, rightIndex);

    public static bool MoveMonsterToLeftByCardEffect(CardModel card)
    {
        var player = TryGetOwner(card);
        if (player is null || !IsOnField(card))
            return false;

        var monsters = GetMonsters(player).ToList();
        var index = monsters.FindIndex(monster => ReferenceEquals(monster, card));
        return index > 0 && MoveMonsterInternal(player, index, 0);
    }

    public static IDisposable PushManualRepositionUnlock(Player player)
    {
        if (player is null)
            return NoopDisposable.Instance;

        ManualRepositionUnlocks.TryGetValue(player, out var currentUnlocks);
        ManualRepositionUnlocks[player] = currentUnlocks + 1;
        return new ManualRepositionUnlockHandle(player);
    }

    private static bool MoveMonsterInternal(Player player, int fromIndex, int toIndex)
    {
        var monsters = GetMonsters(player).ToList();
        if (fromIndex < 0
            || fromIndex >= monsters.Count
            || toIndex < 0
            || toIndex >= monsters.Count
            || fromIndex == toIndex)
        {
            return false;
        }

        var pile = GetPile(player);
        if (pile?.Cards is not IList<CardModel> pileCards)
            return false;

        var movingCard = monsters[fromIndex];
        var targetCard = monsters[toIndex];
        var fromPileIndex = pileCards.IndexOf(movingCard);
        var toPileIndex = pileCards.IndexOf(targetCard);
        if (fromPileIndex < 0 || toPileIndex < 0)
            return false;

        pileCards.RemoveAt(fromPileIndex);
        pileCards.Insert(toPileIndex, movingCard);
        ReassignFieldOrdersInCurrentOrder(pileCards);
        pile.InvokeContentsChanged();
        return true;
    }

    private static bool SwapMonstersInternal(Player player, int leftIndex, int rightIndex)
    {
        var monsters = GetMonsters(player).ToList();
        if (leftIndex < 0
            || leftIndex >= monsters.Count
            || rightIndex < 0
            || rightIndex >= monsters.Count
            || leftIndex == rightIndex)
        {
            return false;
        }

        var pile = GetPile(player);
        if (pile?.Cards is not IList<CardModel> pileCards)
            return false;

        var leftCard = monsters[leftIndex];
        var rightCard = monsters[rightIndex];
        var leftPileIndex = pileCards.IndexOf(leftCard);
        var rightPileIndex = pileCards.IndexOf(rightCard);
        if (leftPileIndex < 0 || rightPileIndex < 0)
            return false;

        pileCards[leftPileIndex] = rightCard;
        pileCards[rightPileIndex] = leftCard;
        ReassignFieldOrdersInCurrentOrder(pileCards);
        pile.InvokeContentsChanged();
        return true;
    }

    public static CardPile GetPile(Player player)
    {
        if (player?.PlayerCombatState is null)
            return null;

        var pile = CustomPiles.GetCustomPile(player.PlayerCombatState, MonsterFieldPile.FieldPileType);
        MonsterFieldUpgradeLockService.Attach(pile);
        // Health emits the synchronous field-entry event. Normalize first so
        // its visual snapshot points at the monster's final reserved slot.
        AttachOrdering(pile);
        MonsterFieldHealthService.Attach(player, pile);
        return pile;
    }

    private static void AttachOrdering(CardPile pile)
    {
        if (pile is null || !OrderingAttachedPiles.Add(pile))
            return;

        pile.CardAdded += card => NormalizeMonsterOrderAfterAdd(pile, card);
        pile.CardRemoved += card => UnmarkPending(card);
    }

    private static void NormalizeMonsterOrderAfterAdd(CardPile pile, CardModel card)
    {
        if (!IsFieldMonster(card)
            || pile?.Cards is not IList<CardModel> pileCards
            || !NormalizingPiles.Add(pile))
        {
            return;
        }

        try
        {
            if (pileCards.IndexOf(card) < 0)
                return;

            EnsureFieldOrdersInCurrentOrder(pileCards.Where(pileCard => !ReferenceEquals(pileCard, card)));
            EnsureFieldOrder(card);

            var orderedCards = pileCards
                .Select((pileCard, index) => new
                {
                    Card = pileCard,
                    Index = index,
                    Order = GetFieldOrder(pileCard)
                })
                .OrderBy(entry => entry.Order)
                .ThenBy(entry => entry.Index)
                .Select(entry => entry.Card)
                .ToList();

            if (IsSameOrder(pileCards, orderedCards))
                return;

            pileCards.Clear();
            foreach (var orderedCard in orderedCards)
                pileCards.Add(orderedCard);

            pile.InvokeContentsChanged();
        }
        finally
        {
            NormalizingPiles.Remove(pile);
        }
    }

    private static void EnsureExistingFieldOrders(CardModel pendingCard)
    {
        var player = TryGetOwner(pendingCard);
        if (player?.PlayerCombatState is null)
            return;

        var pile = GetPile(player);
        if (pile?.Cards is not null)
            EnsureFieldOrdersInCurrentOrder(pile.Cards);
    }

    private static void EnsureFieldOrdersInCurrentOrder(IEnumerable<CardModel> cards)
    {
        foreach (var card in cards)
            EnsureFieldOrder(card);
    }

    private static void EnsureFieldOrder(CardModel card)
    {
        if (!IsFieldMonster(card) || FieldOrderReservations.ContainsKey(card))
            return;

        FieldOrderReservations[card] = ++NextFieldOrder;
    }

    private static void ReassignFieldOrdersInCurrentOrder(IEnumerable<CardModel> cards)
    {
        foreach (var card in cards.Where(IsFieldMonster))
            FieldOrderReservations[card] = ++NextFieldOrder;
    }

    private static long GetFieldOrder(CardModel card) =>
        IsFieldMonster(card) && FieldOrderReservations.TryGetValue(card, out var order)
            ? order
            : long.MaxValue;

    private static bool IsSameOrder(IList<CardModel> currentCards, IReadOnlyList<CardModel> orderedCards)
    {
        if (currentCards.Count != orderedCards.Count)
            return false;

        for (var index = 0; index < currentCards.Count; index++)
        {
            if (!ReferenceEquals(currentCards[index], orderedCards[index]))
                return false;
        }

        return true;
    }

    internal static IDisposable PushTemporaryCapacityBonus(Player player, int bonus)
    {
        if (player is null || bonus == 0)
            return NoopDisposable.Instance;

        TemporaryCapacityBonuses.TryGetValue(player, out var currentBonus);
        TemporaryCapacityBonuses[player] = currentBonus + bonus;
        return new TemporaryCapacityBonusHandle(player, bonus);
    }

    /// <summary>
    /// Keeps room for a replacement that will be committed after exhaustion
    /// hooks finish. This prevents an immediate re-summon (for example Alice)
    /// from consuming the replacement's final field slot.
    /// </summary>
    internal static IDisposable ReserveCapacitySlots(Player player, int slots = 1) =>
        slots <= 0
            ? NoopDisposable.Instance
            : PushTemporaryCapacityBonus(player, -slots);

    internal static bool IsVisualMutationInProgress(Player player) =>
        player is not null
        && VisualMutationDepths.TryGetValue(player, out var depth)
        && depth > 0;

    private static IDisposable BeginVisualMutation(IEnumerable<CardModel> cards)
    {
        var players = cards
            .Select(TryGetOwner)
            .Where(player => player is not null)
            .Distinct(new ReferenceComparer<Player>())
            .ToList();
        if (players.Count == 0)
            return NoopDisposable.Instance;

        foreach (var player in players)
        {
            VisualMutationDepths.TryGetValue(player, out var depth);
            VisualMutationDepths[player] = depth + 1;
        }

        return new VisualMutationHandle(players);
    }

    private static int CountFieldAndPending(Player player, CardModel except) =>
        GetFieldOrPendingMonsters(player, forPreview: false)
            .Count(card => !ReferenceEquals(card, except));

    private static IReadOnlyList<CardModel> GetFieldOrPendingMonsters(
        Player player,
        bool forPreview)
    {
        var fieldMonsters = forPreview
            ? GetMonstersForPreview(player)
            : GetMonsters(player);

        // A nested play marks a monster after its own summon step, but the
        // native wrapper only commits it to the field pile while unwinding.
        // Build a reference-identity union because one entity can briefly be
        // present in both sources, and separate copies can share the same ID.
        return fieldMonsters
            .Concat(PendingFieldCards.Where(card => TryGetOwner(card) == player))
            .Where(IsFieldMonster)
            .Distinct(new ReferenceComparer<CardModel>())
            .ToList();
    }

    private static Player TryGetOwner(CardModel card)
    {
        try
        {
            return card?.Owner;
        }
        catch
        {
            return null;
        }
    }

    private sealed class TemporaryCapacityBonusHandle(Player player, int bonus) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (!TemporaryCapacityBonuses.TryGetValue(player, out var currentBonus))
                return;

            var updatedBonus = currentBonus - bonus;
            if (updatedBonus == 0)
                TemporaryCapacityBonuses.Remove(player);
            else
                TemporaryCapacityBonuses[player] = updatedBonus;
        }
    }

    private sealed class VisualMutationHandle(IReadOnlyList<Player> players) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            var completed = new List<Player>();
            foreach (var player in players)
            {
                if (!VisualMutationDepths.TryGetValue(player, out var depth))
                    continue;

                if (depth <= 1)
                {
                    VisualMutationDepths.Remove(player);
                    completed.Add(player);
                }
                else
                {
                    VisualMutationDepths[player] = depth - 1;
                }
            }

            foreach (var player in completed)
            {
                try
                {
                    VisualMutationCompleted?.Invoke(player);
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Info($"Monster field visual refresh failed after mutation error={ex}");
                }
            }
        }
    }

    private sealed class ManualRepositionUnlockHandle(Player player) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (!ManualRepositionUnlocks.TryGetValue(player, out var currentUnlocks))
                return;

            var updatedUnlocks = currentUnlocks - 1;
            if (updatedUnlocks <= 0)
                ManualRepositionUnlocks.Remove(player);
            else
                ManualRepositionUnlocks[player] = updatedUnlocks;
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        private NoopDisposable()
        {
        }

        public void Dispose()
        {
        }
    }
}
