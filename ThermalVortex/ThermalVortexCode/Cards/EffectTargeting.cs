using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Patches;

namespace ThermalVortex.ThermalVortexCode.Cards;

/// <summary>
/// Resolves targets for cards and effects that are played from inside another
/// effect. CardCmd.AutoPlay never opens the normal targeting UI, so callers
/// must collect and synchronize a target before invoking it.
/// </summary>
internal static class EffectTargeting
{
    private static readonly AsyncLocal<int> ImmediatePlayEvaluationDepth = new();

    internal static bool IsImmediatePlayEvaluation => ImmediatePlayEvaluationDepth.Value > 0;

    /// <summary>
    /// Checks the same gates used by an immediate AutoPlay without applying
    /// energy or hand-play requirements. This is safe to use while building a
    /// selection list; PlayImmediately repeats the check after target choice.
    /// </summary>
    internal static bool CanPlayImmediately(
        Player owner,
        CardModel card,
        bool excludeFromCardPlayCount = false)
    {
        using var immediatePlayEvaluation = EnterImmediatePlayEvaluation();
        if (owner?.Creature is null
            || card is null
            || CombatManager.Instance?.IsOverOrEnding == true)
        {
            return false;
        }

        using var countExemption = CardPlayCountExemption.Enter(
            card,
            excludeFromCardPlayCount);

        var combatState = owner.Creature.CombatState;
        return combatState is not null
            && Hook.ShouldPlay(combatState, card, out _, AutoPlayType.Default)
            && HasAvailableAutoPlayTarget(combatState, card);
    }

    /// <summary>
    /// Plays a card created or selected by another effect as one committed
    /// operation. Target selection happens before <paramref name="prepare"/>,
    /// so cancelling the targeting UI cannot leave a transient card in hand.
    /// </summary>
    internal static async Task<bool> PlayImmediately(
        PlayerChoiceContext ctx,
        Player owner,
        CardModel card,
        Creature preferredTarget = null,
        Func<Task> prepare = null,
        Func<bool> canPrepare = null,
        bool excludeFromCardPlayCount = false,
        Func<bool> canExecute = null,
        Func<bool> isOperationValid = null)
    {
        using var immediatePlayEvaluation = EnterImmediatePlayEvaluation();
        using var countExemption = CardPlayCountExemption.Enter(
            card,
            excludeFromCardPlayCount);

        if (isOperationValid?.Invoke() == false || !CanPlayImmediately(owner, card))
            return false;

        var target = await ResolveForAutoPlay(ctx, owner, card, preferredTarget, isOperationValid);
        if (target is null)
            return false;

        // Choice synchronization can yield long enough for powers, capacity,
        // combat state, or the selected creature to change. Do not run prepare
        // (which may move cards or alter their costs) for a stale selection.
        if (isOperationValid?.Invoke() == false
            || !CanPlayImmediately(owner, card)
            || !IsResolvedAutoPlayTargetValid(owner.Creature.CombatState, card, target)
            || (canPrepare is not null && !canPrepare()))
        {
            return false;
        }

        var history = CombatManager.Instance?.History;
        var finishedBefore = history?.CardPlaysFinished.Count(entry =>
            ReferenceEquals(entry.CardPlay.Card, card)) ?? 0;
        var wrapperCompleted = false;
        void MarkPlayed() => wrapperCompleted = true;

        card.Played += MarkPlayed;
        try
        {
            if (prepare is not null)
                await prepare();

            if (isOperationValid?.Invoke() == false
                || canExecute?.Invoke() == false
                || !CanPlayImmediately(owner, card)
                || !IsResolvedAutoPlayTargetValid(owner.Creature.CombatState, card, target))
            {
                return false;
            }

            await CardCmd.AutoPlay(ctx, card, target, AutoPlayType.Default, false, false);
        }
        finally
        {
            card.Played -= MarkPlayed;
        }

        // AutoPlay may silently route a rejected card to its result pile. A
        // history entry proves OnPlay completed; Played proves the wrapper and
        // result-pile lifecycle completed. Field is the monster commit marker.
        var finishedAfter = history?.CardPlaysFinished.Count(entry =>
            ReferenceEquals(entry.CardPlay.Card, card)) ?? finishedBefore;
        var onPlayCompleted = history is null
            ? wrapperCompleted
            : finishedAfter > finishedBefore;
        var played = onPlayCompleted && wrapperCompleted;
        if (played && MonsterFieldService.IsOnField(card))
        {
            // A nested immediate play has fully committed once AutoPlay returns.
            // Its field-order reservation remains useful, but retaining the
            // pending-capacity marker makes the finished monster count twice.
            MonsterFieldService.UnmarkPending(card, keepFieldOrderReservation: true);
        }

        return played;
    }

    internal static async Task RemoveUncommittedTransientCard(CardModel card)
    {
        if (card is null || MonsterFieldService.IsOnField(card))
            return;

        MonsterFieldUpgradeLockService.CancelAwaitingFieldEntry(card);
        MonsterFieldDampenCompatibilityPatch.RemoveRestoreLevel(card);
        MonsterFieldService.UnmarkPending(card);
        if (card.Pile is not null)
            await CardPileCmd.RemoveFromCombat(card, false);
        else
            card.RemoveFromState();
        if (card.HasBeenRemovedFromState)
            CyberDevourState.ReleaseCombatState(card);
    }

    internal static async Task<Creature> ResolveForAutoPlay(
        PlayerChoiceContext ctx,
        Player owner,
        CardModel card,
        Creature preferredTarget = null,
        Func<bool> isOperationValid = null)
    {
        if (owner?.Creature is null || card is null || isOperationValid?.Invoke() == false)
            return null;

        if (card.TargetType == TargetType.Self)
            return owner.Creature;

        if (!RequiresCreatureChoice(card.TargetType))
            return owner.Creature;

        var candidates = GetValidTargets(owner.Creature.CombatState, card.IsValidTarget);
        var preferredIndex = FindTargetIndex(candidates, preferredTarget);
        if (preferredIndex >= 0)
            return candidates[preferredIndex];

        return await ChooseTarget(ctx, owner, card.TargetType, candidates, card.IsValidTarget, card, isOperationValid);
    }

    internal static async Task<Creature> ChooseEnemy(
        PlayerChoiceContext ctx,
        Player owner,
        Creature preferredTarget = null,
        CardModel sourceCard = null)
    {
        if (owner?.Creature?.CombatState is not { } state)
            return null;

        var candidates = GetValidTargets(
            state,
            creature => creature.IsEnemy && creature.IsHittable);
        var preferredIndex = FindTargetIndex(candidates, preferredTarget);
        if (preferredIndex >= 0)
            return candidates[preferredIndex];

        return await ChooseTarget(
            ctx,
            owner,
            TargetType.AnyEnemy,
            candidates,
            creature => creature.IsEnemy && creature.IsHittable,
            sourceCard);
    }

    private static bool RequiresCreatureChoice(TargetType targetType) =>
        targetType.IsSingleTarget()
        && targetType != TargetType.Self
        && targetType != TargetType.TargetedNoCreature;

    private static bool HasAvailableAutoPlayTarget(ICombatState state, CardModel card) =>
        !RequiresCreatureChoice(card.TargetType)
        || GetValidTargets(state, card.IsValidTarget).Count > 0;

    private static bool IsResolvedAutoPlayTargetValid(
        ICombatState state,
        CardModel card,
        Creature target)
    {
        if (state is null || target is null)
            return false;

        if (!RequiresCreatureChoice(card.TargetType))
            return true;

        return GetValidTargets(state, card.IsValidTarget)
            .Any(creature => ReferenceEquals(creature, target));
    }

    private static List<Creature> GetValidTargets(
        ICombatState state,
        Func<Creature, bool> isValid)
    {
        if (state?.Creatures is null)
            return [];

        return state.Creatures
            .Where(creature => creature is not null && creature.IsAlive && creature.IsHittable)
            .Where(isValid)
            .ToList();
    }

    private static async Task<Creature> ChooseTarget(
        PlayerChoiceContext ctx,
        Player owner,
        TargetType targetType,
        IReadOnlyList<Creature> candidates,
        Func<Creature, bool> isValid,
        CardModel sourceCard,
        Func<bool> isOperationValid = null)
    {
        if (candidates.Count == 0 || isOperationValid?.Invoke() == false)
            return null;

        if (CardSelectionHelper.IsAutomatedChoiceContext(ctx))
            return candidates[0];

        var runManager = RunManager.Instance;
        var synchronizer = runManager?.PlayerChoiceSynchronizer;
        if (ctx is null || synchronizer is null)
            return candidates[0];

        var choiceId = synchronizer.ReserveChoiceId(owner);
        await ctx.SignalPlayerChoiceBegun(PlayerChoiceOptions.None);
        try
        {
            int? selectedIndex;
            if (ShouldSelectLocally(owner, runManager))
            {
                selectedIndex = await SelectLocalTarget(
                    owner.Creature,
                    targetType,
                    candidates,
                    isValid,
                    sourceCard,
                    isOperationValid);
                synchronizer.SyncLocalChoice(
                    owner,
                    choiceId,
                    PlayerChoiceResult.FromIndex(selectedIndex));
            }
            else
            {
                var result = await synchronizer.WaitForRemoteChoice(owner, choiceId);
                selectedIndex = result.AsIndexOrNull();
            }

            return isOperationValid?.Invoke() != false
                && IsCurrentlyValid(candidates, selectedIndex, isValid)
                ? candidates[selectedIndex.Value]
                : null;
        }
        finally
        {
            await ctx.SignalPlayerChoiceEnded();
        }
    }

    private static bool ShouldSelectLocally(Player owner, RunManager runManager) =>
        LocalContext.IsMe(owner)
        && runManager?.NetService?.Type != NetGameType.Replay;

    private static async Task<int?> SelectLocalTarget(
        Creature source,
        TargetType targetType,
        IReadOnlyList<Creature> candidates,
        Func<Creature, bool> isValid,
        CardModel sourceCard,
        Func<bool> isOperationValid)
    {
        while (true)
        {
            if (isOperationValid?.Invoke() == false)
                return null;

            var (selected, interrupted) = await SelectLocalTargetOnce(
                source, targetType, candidates, isValid, sourceCard, isOperationValid);
            if (interrupted)
                return null;
            var selectedIndex = FindTargetIndex(candidates, selected);
            if (IsCurrentlyValid(candidates, selectedIndex, isValid))
                return selectedIndex;

            if (!candidates.Any(creature => IsCurrentlyValid(creature, isValid))
                || CombatManager.Instance?.IsOverOrEnding == true)
            {
                return null;
            }

            // Target choice is mandatory after the player has committed to the
            // generated/summoned card. If they cancel, reopen targeting on the
            // next continuation instead of silently choosing the first enemy.
            await Task.Yield();
        }
    }

    private static async Task<(Creature Target, bool Interrupted)> SelectLocalTargetOnce(
        Creature source,
        TargetType targetType,
        IReadOnlyList<Creature> candidates,
        Func<Creature, bool> isValid,
        CardModel sourceCard,
        Func<bool> isOperationValid)
    {
        var room = NCombatRoom.Instance;
        if (!IsCurrentTargetingRoom(room) || isOperationValid?.Invoke() == false)
            return (null, true);
        var targetManager = ResolveTargetManager();
        if (!IsCurrentTargetManager(targetManager))
            return (null, true);

        // Native FinishTargeting completes its task before clearing the target
        // mode and arrow. A continuation from a previous card/target choice can
        // still be inside that method: cancelling here would SetResult twice.
        if (!await WaitForTargetingFrame(room, targetManager)
            || isOperationValid?.Invoke() == false)
            return (null, true);

        NPlayerHand.Instance?.CancelAllCardPlay();
        if (targetManager.IsInSelection)
            targetManager.CancelTargeting();

        var candidateNodes = room.CreatureNodes
            .Where(node => FindTargetIndex(candidates, node?.Entity) >= 0
                && IsCurrentlyValid(node.Entity, isValid))
            .ToList();
        if (candidateNodes.Count == 0)
            return (candidates.FirstOrDefault(), false);

        var sourceNode = room.CreatureNodes
            .FirstOrDefault(node => ReferenceEquals(node?.Entity, source));
        var sourceControl = sourceNode?.Hitbox ?? candidateNodes[0].Hitbox;
        var useController = NControllerManager.Instance?.IsUsingController == true;
        var preview = CardEffectPreviewUi.ShowTargeting(room, sourceCard);
        void PreviewHovered(NCreature node) =>
            preview?.SetTarget(node?.Entity is { } target && IsCurrentlyValid(target, isValid)
                ? target
                : null);
        void PreviewUnhovered(NCreature _) => preview?.SetTarget(null);

        var interrupted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Node> selectionTask = null;
        var ownsSelection = false;
        void CancelOwnedSelection()
        {
            if (!ownsSelection)
                return;
            ownsSelection = false;
            if (!IsCurrentTargetManager(targetManager) || !targetManager.IsInSelection
                || selectionTask?.IsCompleted == true)
                return;
            try
            {
                targetManager.CancelTargeting();
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"Effect target selection cleanup unavailable error={ex}");
            }
        }
        void RoomExiting()
        {
            interrupted.TrySetResult(true);
            CancelOwnedSelection();
        }
        void TargetingInterrupted()
        {
            // A different selection or a departing manager owns its own cleanup.
            ownsSelection = false;
            interrupted.TrySetResult(true);
        }

        try
        {
            room.TreeExiting += RoomExiting;
            targetManager.TreeExiting += TargetingInterrupted;
            targetManager.CreatureHovered += PreviewHovered;
            targetManager.CreatureUnhovered += PreviewUnhovered;
            targetManager.StartTargeting(
                targetType,
                sourceControl,
                useController ? TargetMode.Controller : TargetMode.ClickMouseToTarget,
                () => isOperationValid?.Invoke() == false
                    || !candidates.Any(creature => IsCurrentlyValid(creature, isValid)),
                node => node is NCreature creatureNode
                    && FindTargetIndex(candidates, creatureNode.Entity) >= 0
                    && IsCurrentlyValid(creatureNode.Entity, isValid));
            ownsSelection = true;
            selectionTask = targetManager.SelectionFinished();
            targetManager.TargetingBegan += TargetingInterrupted;

            if (useController)
            {
                room.RestrictControllerNavigation(candidateNodes.Select(node => node.Hitbox));
                var focusedNode = candidateNodes.FirstOrDefault(node =>
                        ReferenceEquals(node.Entity, room.LastTargetedCreature))
                    ?? candidateNodes[0];
                focusedNode.Hitbox.GrabFocus();
            }

            // Native _ExitTree does not complete SelectionFinished. Wait for
            // this view's lifetime as well, without reopening a departed view.
            await Task.WhenAny(selectionTask, interrupted.Task);
            if (interrupted.Task.IsCompleted)
                return (null, true);
            var selectedNode = await selectionTask;
            // Do not resume material effects or another immediate play from
            // inside native SetResult. Even TargetingEnded fires before native
            // cleanup finishes and would let it overwrite the next selection.
            if (!await WaitForTargetingFrame(room, targetManager)
                || interrupted.Task.IsCompleted)
                return (null, true);
            // Selection may finish because the combat view was closed. Never
            // resume UI work against a detached room or a freed target node.
            if (!IsCurrentTargetingRoom(room) || isOperationValid?.Invoke() == false)
                return (null, true);
            if (selectedNode is not null
                && (!GodotObject.IsInstanceValid(selectedNode) || selectedNode.IsQueuedForDeletion()
                    || !selectedNode.IsInsideTree()))
                return (null, false);
            var selected = (selectedNode as NCreature)?.Entity;
            if (selected is not null)
                room.LastTargetedCreature = selected;

            return (selected, false);
        }
        finally
        {
            CancelOwnedSelection();
            if (GodotObject.IsInstanceValid(targetManager))
            {
                targetManager.TreeExiting -= TargetingInterrupted;
                targetManager.TargetingBegan -= TargetingInterrupted;
                targetManager.CreatureHovered -= PreviewHovered;
                targetManager.CreatureUnhovered -= PreviewUnhovered;
            }
            if (GodotObject.IsInstanceValid(room))
                room.TreeExiting -= RoomExiting;
            preview?.Release();
            if (useController && !interrupted.Task.IsCompleted && IsCurrentTargetingRoom(room))
                room.EnableControllerNavigation();
        }
    }

    private static async Task<bool> WaitForTargetingFrame(
        NCombatRoom room,
        NTargetManager targetManager)
    {
        if (!IsCurrentTargetingRoom(room) || !IsCurrentTargetManager(targetManager))
            return false;

        var tree = room.GetTree();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void NextFrame() => completed.TrySetResult(true);
        void Exiting() => completed.TrySetResult(false);

        tree.ProcessFrame += NextFrame;
        room.TreeExiting += Exiting;
        targetManager.TreeExiting += Exiting;
        try
        {
            return await completed.Task
                && IsCurrentTargetingRoom(room)
                && IsCurrentTargetManager(targetManager);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(tree))
                tree.ProcessFrame -= NextFrame;
            if (GodotObject.IsInstanceValid(room))
                room.TreeExiting -= Exiting;
            if (GodotObject.IsInstanceValid(targetManager))
                targetManager.TreeExiting -= Exiting;
        }
    }

    private static bool IsCurrentTargetingRoom(NCombatRoom room) =>
        GodotObject.IsInstanceValid(room) && !room.IsQueuedForDeletion()
        && room.IsInsideTree() && room.IsNodeReady() && ReferenceEquals(room, NCombatRoom.Instance);

    private static bool IsCurrentTargetManager(NTargetManager manager) =>
        GodotObject.IsInstanceValid(manager) && !manager.IsQueuedForDeletion()
        && manager.IsInsideTree() && manager.IsNodeReady() && ReferenceEquals(manager, ResolveTargetManager());

    private static NTargetManager ResolveTargetManager()
    {
        // NTargetManager.Instance dereferences NRun.Instance without a null
        // check. A pending selection can resume after the run has been removed.
        var run = NRun.Instance;
        if (!GodotObject.IsInstanceValid(run) || run.IsQueuedForDeletion())
            return null;
        var globalUi = run.GlobalUi;
        return GodotObject.IsInstanceValid(globalUi) && !globalUi.IsQueuedForDeletion()
            ? globalUi.TargetManager
            : null;
    }

    private static int FindTargetIndex(
        IReadOnlyList<Creature> candidates,
        Creature target)
    {
        if (target is null)
            return -1;

        for (var i = 0; i < candidates.Count; i++)
        {
            if (ReferenceEquals(candidates[i], target))
                return i;
        }

        return -1;
    }

    private static bool IsCurrentlyValid(
        IReadOnlyList<Creature> candidates,
        int? index,
        Func<Creature, bool> isValid) =>
        index is >= 0 && index < candidates.Count
        && IsCurrentlyValid(candidates[index.Value], isValid);

    private static bool IsCurrentlyValid(Creature creature, Func<Creature, bool> isValid) =>
        creature is not null
        && creature.IsAlive
        && creature.IsHittable
        && isValid(creature);

    private static IDisposable EnterImmediatePlayEvaluation()
    {
        ImmediatePlayEvaluationDepth.Value++;
        return new ImmediatePlayEvaluationHandle();
    }

    private sealed class ImmediatePlayEvaluationHandle : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ImmediatePlayEvaluationDepth.Value = Math.Max(0, ImmediatePlayEvaluationDepth.Value - 1);
        }
    }
}
