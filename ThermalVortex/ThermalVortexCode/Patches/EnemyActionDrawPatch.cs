using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using System.Threading;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(MoveState), nameof(MoveState.PerformMove))]
internal static class EnemyActionDrawPatch
{
    [HarmonyPrefix]
    private static void CaptureBeforeEnemyAction(
        MoveState __instance,
        IEnumerable<Creature> __0,
        ref EnemyActionDrawResolution.ResolutionHandle __state)
    {
        var players = __0?
            .Select(creature => creature.Player)
            .Where(player => player is not null)
            .Distinct()
            .ToList() ?? [];

        __state = EnemyActionDrawResolution.Enter(__instance, players);
    }

    [HarmonyPostfix]
    private static void DrawAfterEnemyAction(
        ref Task __result,
        EnemyActionDrawResolution.ResolutionHandle __state)
    {
        if (__state is null)
            return;

        __state.DetachCaller();
        if (__result is null)
        {
            __state.Dispose();
            return;
        }

        __result = EnemyActionDrawResolution.CompleteAsync(__result, __state);
    }
}

internal static class EnemyActionDrawResolution
{
    private static readonly AsyncLocal<ResolutionContext> Current = new();

    internal static ResolutionHandle Enter(MoveState move, IReadOnlyList<Player> players) =>
        Enter(EnemyActionClassifier.Classify(move), players);

    internal static ResolutionHandle Enter(
        EnemyActionSummary action,
        IReadOnlyList<Player> players)
    {
        if (players is null || players.Count == 0)
            return null;

        var previous = Current.Value;
        var context = new ResolutionContext(action, players);
        Current.Value = context;
        return new ResolutionHandle(previous, context);
    }

    internal static void CaptureAttackSegment(Player player, object segmentToken) =>
        Current.Value?.CaptureAttackSegment(player, segmentToken);

    internal static void CaptureSummonSegment(Creature creature) =>
        Current.Value?.CaptureSummonSegment(creature);

    internal static void CaptureEnemyBenefitEffect(Creature creature) =>
        Current.Value?.CaptureEnemyBenefitEffect(creature);

    internal static void CapturePlayerNegativeEffect(Creature creature) =>
        Current.Value?.CapturePlayerNegativeEffect(creature);

    internal static void CapturePowerChange(
        EnemyActionDrawPower observer,
        PowerModel changedPower,
        decimal amount,
        Creature applier) =>
        Current.Value?.CapturePowerChange(observer, changedPower, amount, applier);

    internal static void CaptureStatusCard(
        EnemyActionDrawPower observer,
        CardModel card,
        PileType oldPileType,
        AbstractModel source) =>
        Current.Value?.CaptureStatusCard(observer, card, oldPileType, source);

    internal static async Task CompleteAsync(Task original, ResolutionHandle handle)
    {
        try
        {
            await original;
            await handle.Context.Resolve(new BlockingPlayerChoiceContext());
        }
        finally
        {
            handle.Dispose();
        }
    }

    internal static void ResetForRunTransition() => Current.Value = null;

    internal sealed class ResolutionHandle(
        ResolutionContext previous,
        ResolutionContext context) : IDisposable
    {
        private bool _disposed;

        internal ResolutionContext Context { get; } = context;

        internal void DetachCaller()
        {
            if (ReferenceEquals(Current.Value, Context))
                Current.Value = previous;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            DetachCaller();
        }
    }

    internal sealed class ResolutionContext
    {
        private static readonly EnemyActionSummary OneAttackSegment = new(
            EnemyActionKind.Any | EnemyActionKind.Attack,
            1,
            1,
            0,
            0);

        private static readonly EnemyActionSummary OneSummonSegment = new(
            EnemyActionKind.Any | EnemyActionKind.Summon,
            1,
            0,
            0,
            1);

        private static readonly EnemyActionSummary OnePowerChange = new(
            EnemyActionKind.Any | EnemyActionKind.PowerChange,
            1,
            0,
            1,
            0);

        private readonly IReadOnlyList<Player> _players;
        private readonly Dictionary<Player, int> _pendingDraws = [];
        private readonly Dictionary<PowerModel, (decimal CurrentAmount, decimal Delta)> _capturedPowerChanges =
            new(new ReferenceComparer<PowerModel>());
        private readonly HashSet<CardModel> _capturedStatusCards = new(new ReferenceComparer<CardModel>());
        private readonly HashSet<object> _capturedAttackSegments = new(new ReferenceComparer<object>());
        private readonly bool _hasAdvertisedAttack;
        private readonly bool _hasAdvertisedPowerChange;
        private readonly bool _hasAdvertisedSummon;
        private int _remainingFallbackOtherCount;

        internal ResolutionContext(EnemyActionSummary action, IReadOnlyList<Player> players)
        {
            _players = players;

            var measuredIntentCount = action.AttackCount
                + action.SummonCount
                + action.PowerChangeCount;
            // Unclassified intent entries are only a fallback. Common concrete
            // effects are captured at their successful command boundary; any
            // remainder is evaluated at action completion so a source already
            // removed by an earlier segment cannot trigger afterward.
            _hasAdvertisedAttack = action.AttackCount > 0;
            _hasAdvertisedPowerChange = action.PowerChangeCount > 0;
            _hasAdvertisedSummon = action.SummonCount > 0;
            _remainingFallbackOtherCount = Math.Max(0, action.AnyCount - measuredIntentCount);
        }

        internal void CaptureAttackSegment(Player player, object segmentToken)
        {
            if (player is null || !_players.Contains(player))
                return;

            if (!_hasAdvertisedAttack
                && _capturedAttackSegments.Add(segmentToken ?? new object()))
            {
                ConsumeFallbackOtherIntent();
            }

            Capture(OneAttackSegment, [player]);
        }

        internal void CaptureSummonSegment(Creature creature)
        {
            if (creature?.IsEnemy != true)
                return;

            if (!_hasAdvertisedSummon)
                ConsumeFallbackOtherIntent();

            Capture(OneSummonSegment, _players);
        }

        internal void CaptureEnemyBenefitEffect(Creature creature)
        {
            if (creature?.IsEnemy != true)
                return;

            if (!_hasAdvertisedPowerChange)
                ConsumeFallbackOtherIntent();

            Capture(OnePowerChange, _players);
        }

        internal void CapturePlayerNegativeEffect(Creature creature)
        {
            if (creature?.IsPlayer != true || creature.Player is not { } player || !_players.Contains(player))
                return;

            if (!_hasAdvertisedPowerChange)
                ConsumeFallbackOtherIntent();

            Capture(OnePowerChange, [player]);
        }

        internal void CapturePowerChange(
            EnemyActionDrawPower observer,
            PowerModel changedPower,
            decimal amount,
            Creature applier)
        {
            if (observer is null
                || changedPower?.Owner is null
                || amount <= 0
                || (applier is not null && !applier.IsEnemy))
            {
                return;
            }

            var isEnemyBuff = changedPower.Type == PowerType.Buff && changedPower.Owner.IsEnemy;
            var isPlayerDebuff = changedPower.Type == PowerType.Debuff
                && changedPower.Owner.IsPlayer
                && ReferenceEquals(changedPower.Owner, observer.Owner);
            if (!isEnemyBuff && !isPlayerDebuff)
                return;

            var signature = (changedPower.Amount, amount);
            if (!_capturedPowerChanges.TryGetValue(changedPower, out var previous)
                || previous != signature)
            {
                _capturedPowerChanges[changedPower] = signature;
                if (!_hasAdvertisedPowerChange)
                    ConsumeFallbackOtherIntent();
            }

            Capture(observer, OnePowerChange);
        }

        internal void CaptureStatusCard(
            EnemyActionDrawPower observer,
            CardModel card,
            PileType oldPileType,
            AbstractModel source)
        {
            if (observer is null
                || card?.Owner?.Creature?.IsPlayer != true
                || !ReferenceEquals(card.Owner.Creature, observer.Owner)
                || card.Type != CardType.Status
                || card.Pile?.Type is not (PileType.Draw or PileType.Hand or PileType.Discard)
                || oldPileType is PileType.Draw or PileType.Hand or PileType.Discard)
            {
                return;
            }

            // A player response nested in an enemy action remains the player's
            // effect. Skip this observer's callback scope when the event has no
            // explicit source, but keep every other source boundary intact.
            if (AshBlossomActionNegation.ResolveObservedEffectSource(observer, source)?.IsEnemy != true)
                return;

            // Negation listeners run later in the same pile-change event. A
            // read-only prediction keeps their cancelled status cards out of
            // the draw ledger without spending Ash's charge from an observer.
            if (AshBlossomActionNegation.WouldNegateStatusCard(card, oldPileType, source, observer)
                || SolemnStrikeActionNegation.WouldNegateStatusCard(
                    card, card.Pile?.Type, source, observer))
                return;

            if (_capturedStatusCards.Add(card))
            {
                if (!_hasAdvertisedPowerChange)
                    ConsumeFallbackOtherIntent();
            }

            Capture(observer, OnePowerChange);
        }

        internal async Task Resolve(PlayerChoiceContext ctx)
        {
            if (_remainingFallbackOtherCount > 0)
            {
                Capture(
                    new EnemyActionSummary(
                        EnemyActionKind.Any,
                        _remainingFallbackOtherCount,
                        0,
                        0,
                        0),
                    _players);
                _remainingFallbackOtherCount = 0;
            }

            var pending = _pendingDraws.ToList();
            _pendingDraws.Clear();

            foreach (var (player, amount) in pending)
            {
                if (player?.Creature is null || amount <= 0)
                    continue;

                await CardPileCmd.Draw(ctx, amount, player, false);
            }
        }

        private void Capture(EnemyActionSummary action, IEnumerable<Player> players)
        {
            // Mixed enemy actions can have no generic/attack count here while
            // still carrying a power-change or summon trigger.
            if (action.Kind == EnemyActionKind.None)
                return;

            foreach (var player in players)
            {
                var powers = player.Creature?.Powers
                    .OfType<EnemyActionDrawPower>()
                    .ToList() ?? [];

                foreach (var power in powers)
                {
                    var amount = power.CaptureDrawAmount(action);
                    if (amount <= 0)
                        continue;

                    _pendingDraws.TryGetValue(player, out var current);
                    _pendingDraws[player] = current + amount;
                }
            }
        }

        private void Capture(EnemyActionDrawPower power, EnemyActionSummary action)
        {
            if (power?.Owner?.Player is not { } player || !_players.Contains(player))
                return;

            var amount = power.CaptureDrawAmount(action);
            if (amount <= 0)
                return;

            _pendingDraws.TryGetValue(player, out var current);
            _pendingDraws[player] = current + amount;
        }

        private void ConsumeFallbackOtherIntent()
        {
            if (_remainingFallbackOtherCount > 0)
                _remainingFallbackOtherCount--;
        }

    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Add), new Type[] { typeof(Creature) })]
internal static class EnemyActionDrawSummonPatch
{
    [HarmonyPostfix]
    private static void CaptureActualSummon(Creature creature, bool __runOriginal, ref Task __result)
    {
        if (!__runOriginal || creature?.IsEnemy != true || __result is null)
            return;

        __result = CaptureAfterAddAsync(__result, creature);
    }

    private static async Task CaptureAfterAddAsync(Task original, Creature creature)
    {
        await original;
        EnemyActionDrawResolution.CaptureSummonSegment(creature);
    }
}
