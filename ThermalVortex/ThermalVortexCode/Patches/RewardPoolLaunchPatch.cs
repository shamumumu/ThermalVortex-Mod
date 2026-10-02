using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.RewardPools;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(
    typeof(NGame),
    nameof(NGame.StartNewSingleplayerRun),
    [
        typeof(CharacterModel),
        typeof(bool),
        typeof(IReadOnlyList<ActModel>),
        typeof(IReadOnlyList<ModifierModel>),
        typeof(string),
        typeof(GameMode),
        typeof(int),
        typeof(DateTimeOffset?)
    ])]
internal static class RewardPoolSingleplayerLaunchPatch
{
    [HarmonyPrefix]
    private static void BeginLaunch(
        CharacterModel __0,
        GameMode __5,
        out RewardPoolLaunchToken __state)
    {
        __state = RewardPoolLaunchCoordinator.BeginSingleplayer(__0, __5);
    }

    [HarmonyPostfix]
    private static void WrapLaunchTask(
        RewardPoolLaunchToken __state,
        ref Task<RunState> __result)
    {
        if (__result is not null)
            __result = RewardPoolLaunchCoordinator.CompleteLaunchAsync(__result, __state);
    }

    [HarmonyFinalizer]
    private static Exception FinishSynchronousFailure(
        RewardPoolLaunchToken __state,
        Exception __exception)
    {
        if (__exception is not null)
            RewardPoolLaunchCoordinator.EndLaunch(__state, "singleplayer_start_sync_failure");
        return __exception;
    }
}

[HarmonyPatch(typeof(NGame), nameof(NGame.StartNewMultiplayerRun))]
internal static class RewardPoolMultiplayerLaunchPatch
{
    [HarmonyPrefix]
    private static void RejectConstructedSetup()
    {
        RewardPoolLaunchCoordinator.BeginMultiplayer();
    }
}

[HarmonyPatch(
    typeof(RunState),
    nameof(RunState.CreateForNewRun),
    [
        typeof(IReadOnlyList<Player>),
        typeof(IReadOnlyList<ActModel>),
        typeof(IReadOnlyList<ModifierModel>),
        typeof(GameMode),
        typeof(int),
        typeof(string)
    ])]
internal static class RewardPoolFreshRunStatePatch
{
    [HarmonyPostfix]
    private static void ApplyConstructedSetup(RunState __result)
    {
        if (__result?.Players is null)
            return;

        if (RewardPoolMultiplayerLobby.TryApplyFreshRun(__result))
            return;

        var thermalPlayers = __result.Players
            .Where(player => player?.Character is ThermalVortexCharacter)
            .ToList();
        var consumed = RewardPoolLaunchCoordinator.TryConsumeForRun(
            __result,
            out var definition,
            out var reason);

        foreach (var player in thermalPlayers)
        {
            var core = player.GetRelic<ThermalVortexCore>();
            if (consumed && definition.Construction.Method == RewardPoolConstructionMethod.Draft && core is null)
                throw new InvalidOperationException("Cannot apply a draft reward pool without the starting core.");
            core?.ApplyRewardPoolDefinition(definition);
            RewardPoolForeignMechanics.ApplyPersistentCapabilities(
                player,
                RewardPoolCapabilityApplicationPhase.FreshRun);
        }

        if (consumed && definition.Construction.Method == RewardPoolConstructionMethod.Draft
            && !RewardPoolDraftService.TryMarkConsumed(definition.Construction.DraftSessionId, out var draftError))
            throw new InvalidOperationException("Could not persist draft consumption: " + draftError);

        MainFile.Logger.Info(
            $"Fresh ThermalVortex run reward pool initialized mode={definition.BuildMode} mainCards={definition.MainCardIds.Count} extraCards={definition.ExtraCardIds.Count} thermalPlayers={thermalPlayers.Count} consumed={consumed} reason={reason}");
    }
}

internal readonly record struct RewardPoolLaunchToken(
    long TokenId,
    string CharacterId,
    long PageGeneration,
    GameMode GameMode,
    NetGameType NetType)
{
    internal bool IsValid => TokenId > 0;
    internal bool IsSingleplayer => NetType == NetGameType.Singleplayer;
}

internal static class RewardPoolLaunchCoordinator
{
    private static readonly object StateLock = new();
    private static long _nextTokenId;
    private static RewardPoolLaunchToken? _activeToken;

    internal static RewardPoolLaunchToken BeginSingleplayer(
        CharacterModel character,
        GameMode gameMode)
    {
        RewardPoolMultiplayerLobby.ClearPendingLaunch();
        if (IsEligibleSingleplayerLaunch(character, gameMode))
        {
            if (RewardPoolSetupService.TryPeekPending(out var pending))
            {
                var validation = RewardPoolConstruction.ValidateForLaunch(pending.Definition);
                if (!validation.IsValid)
                    throw new InvalidOperationException("Cannot start the selected reward pool: " + validation.Message);
            }
            else if (RewardPoolDraftService.IsSelected)
            {
                throw new InvalidOperationException("Complete and select a new draft reward pool before embarking.");
            }
        }
        var characterId = character?.Id?.ToString() ?? string.Empty;
        var pageGeneration = TrySnapshotPendingGeneration(
            characterId,
            gameMode,
            NetGameType.Singleplayer);
        var token = new RewardPoolLaunchToken(
            Interlocked.Increment(ref _nextTokenId),
            characterId,
            pageGeneration,
            gameMode,
            NetGameType.Singleplayer);
        lock (StateLock)
            _activeToken = token;

        if (!IsEligibleSingleplayerLaunch(character, gameMode))
        {
            RewardPoolSetupService.Clear();
            MainFile.Logger.Info(
                $"Reward-pool pending rejected at singleplayer launch character={token.CharacterId} gameMode={gameMode}");
        }

        return token;
    }

    internal static void BeginMultiplayer()
    {
        RewardPoolSetupService.Clear();
        Cancel("multiplayer_launch");
    }

    internal static bool TryConsumeForRun(
        RunState runState,
        out RewardPoolDefinition definition,
        out string reason)
    {
        definition = RewardPoolDefinition.Standard;
        RewardPoolLaunchToken token;
        lock (StateLock)
        {
            if (_activeToken is not { } active)
            {
                reason = "missing_launch_token";
                RewardPoolSetupService.Clear();
                return false;
            }

            token = active;
        }

        var players = runState?.Players;
        var thermalPlayers = players?
            .Where(player => player?.Character is ThermalVortexCharacter)
            .ToList() ?? [];
        if (!token.IsSingleplayer
            || token.GameMode != GameMode.Standard
            || runState?.GameMode != GameMode.Standard
            || players is not { Count: 1 }
            || thermalPlayers.Count != 1
            || !string.Equals(
                thermalPlayers[0].Character.Id?.ToString(),
                token.CharacterId,
                StringComparison.Ordinal))
        {
            reason = "ineligible_run_context";
            RejectAndEnd(token, reason);
            return false;
        }

        if (!RewardPoolSetupService.TryPeekPending(out var pending))
        {
            reason = "no_pending_setup";
            EndLaunch(token, reason);
            return false;
        }

        var context = pending.Context;
        if (!IsPendingContextCompatible(context, token))
        {
            reason = "pending_context_mismatch";
            RejectAndEnd(token, reason);
            return false;
        }

        var validation = RewardPoolConstruction.ValidateForLaunch(pending.Definition);
        if (!validation.IsValid)
        {
            reason = $"invalid_pending_setup:{validation.Code}";
            RejectAndEnd(token, reason);
            return false;
        }

        // Re-check the complete launch contract while consuming under the
        // setup-service lock.  The earlier peek is only for diagnostics; it
        // must not create a TOCTOU window where a different pending setup can
        // be consumed by this run.
        if (!RewardPoolSetupService.TryConsumePending(
                candidate => IsPendingContextCompatible(candidate.Context, token)
                    && RewardPoolConstruction.ValidateForLaunch(candidate.Definition).IsValid,
                out var consumed))
        {
            reason = "pending_setup_changed_before_consume";
            RejectAndEnd(token, reason);
            return false;
        }

        definition = consumed.Definition.Clone();
        reason = "applied";
        EndLaunch(token, reason, clearPending: false);
        return true;
    }

    internal static async Task<RunState> CompleteLaunchAsync(
        Task<RunState> launchTask,
        RewardPoolLaunchToken token)
    {
        try
        {
            return await launchTask;
        }
        finally
        {
            EndLaunch(token, "singleplayer_launch_complete");
        }
    }

    internal static void EndLaunch(
        RewardPoolLaunchToken token,
        string reason,
        bool clearPending = true)
    {
        var ended = false;
        lock (StateLock)
        {
            if (_activeToken is { } active && active.TokenId == token.TokenId)
            {
                _activeToken = null;
                ended = true;
            }
        }

        if (!ended)
            return;
        if (clearPending)
            RewardPoolSetupService.Clear();
        MainFile.Logger.Info($"Reward-pool launch token ended id={token.TokenId} reason={reason}");
    }

    internal static void Cancel(string reason)
    {
        RewardPoolLaunchToken? canceled;
        lock (StateLock)
        {
            canceled = _activeToken;
            _activeToken = null;
        }

        if (canceled is not null)
            MainFile.Logger.Info(
                $"Reward-pool launch token canceled id={canceled.Value.TokenId} reason={reason}");
    }

    internal static bool IsEligibleSingleplayerLaunch(
        CharacterModel character,
        GameMode gameMode) =>
        character is ThermalVortexCharacter
        && gameMode == GameMode.Standard;

    internal static bool IsPendingContextCompatible(
        RewardPoolPendingContext context,
        RewardPoolLaunchToken token) =>
        token.IsValid
        && token.IsSingleplayer
        && token.PageGeneration > 0
        && token.GameMode == GameMode.Standard
        && token.NetType == NetGameType.Singleplayer
        && context.IsScoped
        && context.Generation == token.PageGeneration
        && context.GameMode == GameMode.Standard
        && context.NetType == NetGameType.Singleplayer
        && string.Equals(context.CharacterId, token.CharacterId, StringComparison.Ordinal);

    private static long TrySnapshotPendingGeneration(
        string characterId,
        GameMode gameMode,
        NetGameType netType)
    {
        if (!RewardPoolSetupService.TryPeekPending(out var pending))
            return 0L;

        var context = pending.Context;
        return context.IsScoped
            && context.GameMode == gameMode
            && context.NetType == netType
            && string.Equals(context.CharacterId, characterId, StringComparison.Ordinal)
                ? context.Generation
                : 0L;
    }

    private static void RejectAndEnd(RewardPoolLaunchToken token, string reason)
    {
        RewardPoolSetupService.Clear();
        EndLaunch(token, reason, clearPending: false);
    }
}
