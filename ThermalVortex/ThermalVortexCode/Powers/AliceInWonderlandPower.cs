using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using System.Runtime.CompilerServices;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class AliceInWonderlandPower : ThermalVortexPower
{
    private const int LifeLoss = 3;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterCardExhausted(PlayerChoiceContext ctx, CardModel card, bool causedByEthereal)
    {
        var player = Owner?.Player;
        var combat = Owner?.CombatState;
        var playerCombat = player?.PlayerCombatState;
        var manager = CombatManager.Instance;
        var turnNumber = playerCombat?.TurnNumber;
        RevivalAttempt attempt = null;

        // Turn-end effects still belong to this player until native side
        // switching. Other players' extra turns do not belong to this owner.
        bool IsCurrentAttempt() => player is not null
            && combat is not null
            && playerCombat is not null
            && manager is not null
            && ReferenceEquals(CombatManager.Instance, manager)
            && ReferenceEquals(manager.DebugOnlyGetState(), combat)
            && !manager.IsOverOrEnding
            && manager.IsPartOfPlayerTurn(player)
            && ReferenceEquals(player.PlayerCombatState, playerCombat)
            && playerCombat.TurnNumber == turnNumber
            && ReferenceEquals(player.Creature, Owner)
            && Owner.CurrentHp > 0
            && Amount > 0
            && ReferenceEquals(Owner.GetPower<AliceInWonderlandPower>(), this)
            && ReferenceEquals(card?.Owner, player)
            && !card.HasBeenRemovedFromState
            && (attempt?.IsValid ?? true);

        if (card is not ThermalVortexCard monster
            || !IsCurrentAttempt()
            || !MonsterFieldService.IsFieldMonster(card)
            || card is WingedDragonOfRaSphereMode
            || card.HasBeenRemovedFromState
            || card.Pile?.Type != PileType.Exhaust)
        {
            return;
        }

        using var currentAttempt = RevivalAttempt.Begin(card, player);
        attempt = currentAttempt;
        if (!monster.TryConsumeAliceSpecialSummonQuota(Amount))
            return;

        // Spend the per-turn quota before paying life or attempting the replay.
        // A failed attempt still spends one of this instance's opportunities.
        Flash();
        await CreatureCmd.SetCurrentHp(Owner, Math.Max(0, Owner.CurrentHp - LifeLoss));
        if (!IsCurrentAttempt() || !MonsterFieldService.CanPlaceOnField(card)
            || playerCombat.Hand.Cards.Count >= CardPile.MaxCardsInHand)
            return;

        var played = false;
        var extraMonster = card as XyzMonsterCard;
        // Reuse the consumed entity and its materials, upgrades and inherited
        // effects. This authorization belongs only to Alice's immediate play;
        // it neither withdraws nor restores any virtual Extra Deck entry.
        attempt.AuthorizeExtraMonster();
        try
        {
            played = await EffectTargeting.PlayImmediately(
                ctx,
                player,
                card,
                prepare: async () =>
                {
                    extraMonster?.PrepareForExtraDeckPlayCost();
                    card.SetToFreeThisTurn();
                    // Native Add reroutes a full hand to Discard. Recheck at
                    // the transfer boundary after target choice and cost hooks;
                    // a paid failed attempt must keep its original Exhaust card.
                    if (playerCombat.Hand.Cards.Count >= CardPile.MaxCardsInHand)
                        return;
                    attempt.BeginHandTransfer();
                    try
                    {
                        await CardPileCmd.Add(card, PileType.Hand, CardPilePosition.Top, this, false);
                    }
                    finally
                    {
                        attempt.EndHandTransfer();
                    }
                },
                canPrepare: () => IsCurrentAttempt()
                    && card.Pile?.Type == PileType.Exhaust,
                canExecute: () => IsCurrentAttempt()
                    && attempt.OwnsHandCard,
                isOperationValid: IsCurrentAttempt);
        }
        finally
        {
            attempt.ClearExtraMonsterAuthorization();
            if (attempt.OwnsHandCard && !played && !card.HasBeenRemovedFromState
                && ReferenceEquals(player.PlayerCombatState, playerCombat)
                && ReferenceEquals(player.Creature.CombatState, combat)
                && !MonsterFieldService.IsOnField(card))
            {
                MonsterFieldService.UnmarkPending(card);
                // Restore the original exhaust without publishing a second
                // exhaust event or changing its already-spent Alice quota.
                await CardPileCmd.Add(card, PileType.Exhaust, CardPilePosition.Top, this, false);
            }
        }
    }

    // Pile identity alone cannot distinguish Exhaust -> elsewhere -> Exhaust
    // while an awaited choice is pending. A departure permanently invalidates
    // this episode, except for the one transfer that this request owns.
    private sealed class RevivalAttempt : IDisposable
    {
        private static readonly ConditionalWeakTable<CardModel, AttemptState> Active = new();
        private readonly CardModel _card;
        private readonly AttemptState _state;
        private readonly CardPile _exhaust;
        private readonly CardPile _hand;
        private bool _invalid;
        private bool _expectExhaustRemoval;
        private bool _expectHandEntry;
        private bool _ownsHandCard;

        private RevivalAttempt(CardModel card, Player player, AttemptState state)
        {
            _card = card;
            _state = state;
            _exhaust = card.Pile;
            _hand = CardPile.Get(PileType.Hand, player);
            _exhaust.CardRemoved += OnExhaustRemoved;
            _hand.CardAdded += OnHandAdded;
            _hand.CardRemoved += OnHandRemoved;
        }

        internal static RevivalAttempt Begin(CardModel card, Player player)
        {
            var state = Active.GetValue(card, _ => new AttemptState());
            state.Current?.Invalidate();
            var attempt = new RevivalAttempt(card, player, state);
            state.Current = attempt;
            return attempt;
        }

        internal bool IsLatest => ReferenceEquals(_state.Current, this);
        internal bool IsValid => !_invalid && IsLatest;
        internal bool OwnsHandCard => IsValid && _ownsHandCard
            && ReferenceEquals(_card.Pile, _hand);

        internal void AuthorizeExtraMonster()
        {
            if (_card is not XyzMonsterCard extraMonster)
                return;
            _state.AuthorizationOwner = this;
            extraMonster.AuthorizeExtraDeckSummonPlay();
        }

        internal void ClearExtraMonsterAuthorization()
        {
            if (!ReferenceEquals(_state.AuthorizationOwner, this))
                return;
            (_card as XyzMonsterCard)?.ClearExtraDeckSummonPlayAuthorization();
            _state.AuthorizationOwner = null;
        }

        internal void BeginHandTransfer()
        {
            _expectExhaustRemoval = true;
            _expectHandEntry = true;
        }

        internal void EndHandTransfer()
        {
            _expectExhaustRemoval = false;
            _expectHandEntry = false;
            if (!OwnsHandCard)
                Invalidate();
        }

        private void OnExhaustRemoved(CardModel card)
        {
            if (!ReferenceEquals(card, _card))
                return;
            if (IsValid && _expectExhaustRemoval)
                _expectExhaustRemoval = false;
            else
                Invalidate();
        }

        private void OnHandAdded(CardModel card)
        {
            if (!ReferenceEquals(card, _card))
                return;
            if (IsValid && _expectHandEntry && !_expectExhaustRemoval)
            {
                _expectHandEntry = false;
                _ownsHandCard = true;
            }
            else
                Invalidate();
        }

        private void OnHandRemoved(CardModel card)
        {
            if (ReferenceEquals(card, _card))
                Invalidate();
        }

        private void Invalidate()
        {
            _invalid = true;
            _ownsHandCard = false;
        }

        public void Dispose()
        {
            _exhaust.CardRemoved -= OnExhaustRemoved;
            _hand.CardAdded -= OnHandAdded;
            _hand.CardRemoved -= OnHandRemoved;
            if (IsLatest)
                _state.Current = null;
        }

        private sealed class AttemptState
        {
            internal RevivalAttempt Current;
            internal RevivalAttempt AuthorizationOwner;
        }
    }
}
