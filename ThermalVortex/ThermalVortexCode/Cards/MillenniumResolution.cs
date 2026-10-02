using System.Runtime.CompilerServices;
using System.Threading;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Cards;

// CardModel's result-pile move happens after AfterCardPlayed. Keep the original
// choice context through that move, and settle hand effects only after the
// wrapper has finished. Direct summons participate through the same scope.
internal static class MillenniumResolution
{
    private static readonly AsyncLocal<Resolution> Current = new();
    private static readonly ConditionalWeakTable<CardModel, PreparedCount> PreparedCounts = new();

    // Fusion pays its materials and may stage the new monster in Hand before
    // OnPlayWrapper runs. Keep that earlier count only until this play begins.
    internal static void PrepareSummonCount(CardModel card)
    {
        ClearPreparedCount(card);
        if (card?.Owner?.PlayerCombatState is { } combat)
            PreparedCounts.Add(card, new PreparedCount(combat, MillenniumSeries.CountTotal(card.Owner)));
    }

    internal static void ClearPreparedCount(CardModel card)
    {
        if (card is not null)
            PreparedCounts.Remove(card);
    }

    internal static int GetCount(CardModel card)
    {
        var host = ChaosPhantomCopyService.ResolveEffectHost(card);
        if (host?.IsMutable != true)
            return 0;
        var resolution = Current.Value;
        if (resolution is { IsValid: true } && ReferenceEquals(resolution.Card, host))
            return resolution.Count;
        return GetPreparedCount(host) ?? MillenniumSeries.CountTotal(host?.Owner);
    }

    private static int? GetPreparedCount(CardModel card) =>
        card is not null && PreparedCounts.TryGetValue(card, out var prepared)
            && ReferenceEquals(prepared.Combat, card.Owner?.PlayerCombatState)
                ? prepared.Count
                : null;

    private sealed record PreparedCount(PlayerCombatState Combat, int Count);

    // These five Extra Deck monsters trigger the Pieces on a successful summon,
    // independently of name-based Millennium count and fusion-material identity.
    private static bool IsHeldEffectExtraDeckSummon(CardModel card) =>
        card is MillenniumGrandThief or ExodiaSummoner or MillenniumMasterKey
            or EvilExodia or ExodiaGuardian;

    private static PlayerChoiceContext GetChoiceContext(PlayerChoiceContext ctx)
    {
        ctx ??= new BlockingPlayerChoiceContext();
        // Some real revival hooks supply a blocking context. In a live combat
        // its choices should still be shown; the ordinary helper reserves that
        // exact context type for automated selections. Keep headless selectors
        // unchanged, and preserve all native signaling for other contexts.
        return ctx.GetType() == typeof(BlockingPlayerChoiceContext) && NCombatRoom.Instance is not null
            ? new InteractiveEntryChoiceContext()
            : ctx;
    }

    private sealed class InteractiveEntryChoiceContext : PlayerChoiceContext
    {
        public override Task SignalPlayerChoiceBegun(PlayerChoiceOptions options) => Task.CompletedTask;
        public override Task SignalPlayerChoiceEnded() => Task.CompletedTask;
    }

    internal static ResolutionHandle EnterPlay(CardModel card, PlayerChoiceContext ctx)
    {
        var previous = Current.Value;
        var resolution = new Resolution(card, ctx ?? previous?.ChoiceContext, captureHand: true);
        Current.Value = resolution;
        return new ResolutionHandle(previous, resolution, ownsResolution: true);
    }

    internal static ResolutionHandle EnterSummon(PlayerChoiceContext ctx, CardModel card, int? count = null)
    {
        var previous = Current.Value;
        // A full copy can commit the played host during its OnPlay. That entry
        // and this card's hand trigger are still a single fusion opportunity.
        if (previous is { Completing: false } && ReferenceEquals(previous.Card, card))
            return new ResolutionHandle(previous, previous, ownsResolution: false);

        var resolution = new Resolution(card, ctx ?? previous?.ChoiceContext, captureHand: false, count);
        Current.Value = resolution;
        return new ResolutionHandle(previous, resolution, ownsResolution: true);
    }

    internal static async Task CompletePlay(Task original, ResolutionHandle handle)
    {
        var previous = Current.Value;
        Current.Value = handle.Resolution;
        try
        {
            await original;
            await handle.Complete();
        }
        finally
        {
            Current.Value = previous;
            handle.Dispose();
        }
    }

    internal static async Task ResolveFieldEntry(
        MonsterFieldEnterEvent enterEvent, AbstractModel source, PileType oldPileType)
    {
        var card = enterEvent.Card;
        // A raw pile move can bypass the summon helpers. Reconstruct its
        // pre-move contribution, and never borrow a different card's scope.
        var countBeforeMove = MillenniumSeries.CountTotal(card.Owner)
            - (MillenniumSeries.IsMillenniumMonster(card) ? 1 : 0)
            + (oldPileType == PileType.Hand && MillenniumSeries.IsMillenniumCard(card) ? 1 : 0);
        using var fallback = Current.Value is not { Completing: false } || !ReferenceEquals(Current.Value.Card, card)
            ? EnterSummon(null, card, Math.Max(0, countBeforeMove))
            : null;
        var resolution = Current.Value;
        if (resolution is null || !resolution.IsValid || !MonsterFieldService.IsOnField(card))
            return;

        // Capture the field ability at the real entry boundary, once across
        // native, copied, and swallowed right-leg sources.
        var fusionEnabled = MonsterFieldService.GetMonsters(card.Owner)
            .Any(MillenniumPieceEffects.HasFusionSummonAbility);
        resolution.CaptureEntry(card, fusionEnabled);
        var ctx = resolution.ChoiceContext;
        ctx.PushModel(card);
        try
        {
            await MillenniumPieceEffects.ResolveNativeSummonReward(ctx, card);
            if (!resolution.IsValid)
                return;

            await MonsterFieldEventService.NotifyMonsterEnteredFieldResolved(ctx, enterEvent);
            if (!resolution.IsValid)
                return;

            if (MonsterFieldService.IsOnField(card))
                await CyberDevourState.TriggerSummonEffects(ctx, card, source ?? card);
            if (!resolution.IsValid)
                return;

            if (MonsterIdentity.Matches<CyberLarva>(card)
                && MonsterIdentity.GetUpgradeLevel(card) <= 0
                && MonsterFieldService.IsOnField(card))
            {
                await MonsterFieldService.SendToExhaust(ctx, [card], card);
            }
        }
        finally
        {
            ctx.PopModel(card);
        }

        if (fallback is not null)
            await fallback.Complete();
    }

    internal sealed class ResolutionHandle(
        Resolution previous,
        Resolution resolution,
        bool ownsResolution) : IDisposable
    {
        private bool _disposed;
        internal Resolution Resolution { get; } = resolution;

        internal void DetachCaller()
        {
            if (ownsResolution && ReferenceEquals(Current.Value, Resolution))
                Current.Value = previous;
        }

        internal async Task Complete()
        {
            if (!ownsResolution)
                return;

            // OnPlayWrapper has already popped its model when its Task ends.
            // Choice commands still need a source while resolving these effects.
            Resolution.ChoiceContext.PushModel(Resolution.Card);
            try
            {
                await Resolution.Complete();
            }
            finally
            {
                Resolution.ChoiceContext.PopModel(Resolution.Card);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (ownsResolution)
                Resolution.Dispose();
            DetachCaller();
        }
    }

    internal sealed class Resolution : IDisposable
    {
        private readonly Player _owner;
        private readonly PlayerCombatState _combat;
        private readonly bool _captureHand;
        private readonly bool _heldEffectsRequireSummon;
        private readonly bool _control;
        private readonly bool _draw;
        private readonly bool _energy;
        private readonly bool _handFusion;
        private readonly List<bool> _otherEntryFusions = [];
        private bool _ownEntrySeen;
        private bool _ownEntryFusion;
        private bool _played;
        private bool _completed;

        internal Resolution(CardModel card, PlayerChoiceContext ctx, bool captureHand, int? count = null)
        {
            Card = card;
            ChoiceContext = GetChoiceContext(ctx);
            _owner = card?.Owner;
            _combat = _owner?.PlayerCombatState;
            Count = GetPreparedCount(card) ?? count ?? MillenniumSeries.CountTotal(_owner);
            ClearPreparedCount(card);
            _captureHand = captureHand;
            _heldEffectsRequireSummon = IsHeldEffectExtraDeckSummon(card);
            if (captureHand && card is not null)
                card.Played += MarkPlayed;

            if (_combat is null
                || (!_heldEffectsRequireSummon
                    && (!captureHand || !MillenniumSeries.IsMillenniumCard(card))))
                return;

            // Exclude the played entity, not its type: another copy left in
            // hand still supplies the unique effect. Newly drawn pieces do not.
            // Extra Deck play begins after material payment; direct summons
            // capture here too, before their committed-entry rewards resolve.
            var held = CardPile.GetCards(_owner, PileType.Hand)
                .Where(candidate => !ReferenceEquals(candidate, card)).ToList();
            _control = held.Any(MonsterIdentity.Matches<ExodiaLeftArm>);
            _draw = held.Any(MonsterIdentity.Matches<ExodiaRightArm>);
            _energy = held.Any(MonsterIdentity.Matches<ExodiaLeftLeg>);
            _handFusion = held.Any(MonsterIdentity.Matches<ExodiaRightLeg>);
        }

        internal CardModel Card { get; }
        internal int Count { get; }
        internal PlayerChoiceContext ChoiceContext { get; }
        internal bool Completing { get; private set; }
        internal bool IsValid => _combat is not null
            && ReferenceEquals(_owner?.PlayerCombatState, _combat)
            && _owner.Creature is { IsAlive: true }
            && CombatManager.Instance?.IsOverOrEnding != true
            && !ExodiaTorso.IsVictoryResolving(_owner);

        private void MarkPlayed() => _played = true;

        internal void CaptureEntry(CardModel card, bool fusionEnabled)
        {
            if (!Completing && ReferenceEquals(card, Card) && !_ownEntrySeen)
            {
                _ownEntrySeen = true;
                _ownEntryFusion = fusionEnabled;
            }
            else
            {
                _otherEntryFusions.Add(fusionEnabled);
            }
        }

        internal async Task Complete()
        {
            if (_completed)
                return;
            _completed = true;
            Completing = true;
            if (!IsValid)
                return;

            var resolveHeldEffects = _heldEffectsRequireSummon
                ? _ownEntrySeen
                : _captureHand && _played;
            if (resolveHeldEffects)
            {
                if (_control)
                    await MillenniumPieceEffects.ControlTopCards(
                        ChoiceContext, _owner, MillenniumPieceEffects.HeldControlCount, Card);
                if (!IsValid)
                    return;
                if (_draw)
                    await CardPileCmd.Draw(ChoiceContext, MillenniumPieceEffects.HeldDrawCount, _owner, false);
                if (!IsValid)
                    return;
                if (_energy)
                    await PlayerCmd.GainEnergy(MillenniumPieceEffects.HeldEnergy, _owner);
            }

            if (!IsValid)
                return;
            if (_ownEntryFusion || (resolveHeldEffects && _handFusion))
                await OfferFusion();

            // Every later successful entry is distinct. Nested fusion plays
            // create their own scopes and re-check the live field at entry.
            for (var index = 0; index < _otherEntryFusions.Count && IsValid; index++)
            {
                if (_otherEntryFusions[index])
                    await OfferFusion();
            }
        }

        private async Task OfferFusion()
        {
            if (!IsValid)
                return;
            var core = _owner.GetRelic<ThermalVortexCore>();
            if (core?.CanUpgradedXyzSummon == true)
                await core.MillenniumFusionSummon(ChoiceContext);
        }

        public void Dispose()
        {
            if (_captureHand && Card is not null)
                Card.Played -= MarkPlayed;
        }
    }
}
