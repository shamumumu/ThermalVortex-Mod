using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

/// <summary>
/// Describes whether the constructed reward pool may participate in a card
/// grant. Fixed pools deliberately remain native: events and relics use them
/// to deliver authored cards rather than generic rewards.
/// </summary>
internal enum RewardGrantPolicy
{
    StandardLegacy,
    GenericRandomReward,
    FixedSingle,
    FixedEventPool,
    Transform,
    Generated,
    CurseStatus,
    CopyReturn,
    Ancient
}

internal static class RewardGrantPolicies
{
    private static readonly AsyncLocal<RewardGrantPolicy?> ScopedOverride = new();

    internal static RewardGrantPolicy ForCreationOptions(
        Player player,
        CardCreationOptions options)
    {
        // Keep this as the first branch. Standard mode must not inspect or
        // enumerate an options pool, because doing so can change native lazy
        // evaluation, exception, and RNG behavior.
        if (!IsConstructedMode(player))
            return RewardGrantPolicy.StandardLegacy;

        // CardCreationOptions.CustomCardPool is not a semantic marker: generic
        // rewards can use it as an implementation detail. Explicit scopes take
        // precedence, then the shared audited source registry distinguishes
        // authored/special grants from generic random rewards.
        if (ScopedOverride.Value is { } scoped)
            return scoped;

        return RewardGrantDiagnostics.TryResolveRuntimePolicy(out var registered)
            ? registered
            : RewardGrantPolicy.GenericRandomReward;
    }

    internal static RewardGrantPolicy ForGenericReward(Player player)
    {
        if (!IsConstructedMode(player))
            return RewardGrantPolicy.StandardLegacy;

        if (ScopedOverride.Value is { } scoped)
            return scoped;

        return RewardGrantDiagnostics.TryResolveRuntimePolicy(out var registered)
            ? registered
            : RewardGrantPolicy.GenericRandomReward;
    }

    internal static bool IsConstructedMode(Player player) =>
        player?.Character is ThermalVortexCharacter
        && player.GetRelic<ThermalVortexCore>()?.IsConstructedRewardPoolEnabled == true;

    internal static bool IsManualMode(Player player) =>
        IsConstructedMode(player)
        && player.GetRelic<ThermalVortexCore>()?.CurrentRewardPoolDefinition?.IsManual == true;

    internal static IDisposable EnterFixedSingle() =>
        EnterOverride(RewardGrantPolicy.FixedSingle);

    internal static IDisposable EnterFixedEventPool() =>
        EnterOverride(RewardGrantPolicy.FixedEventPool);

    // Compatibility name used by the pure diagnostic suite. Runtime callers
    // should prefer the more explicit FixedEventPool terminology above.
    internal static IDisposable EnterFixedPoolPassthrough() =>
        EnterFixedEventPool();

    internal static IDisposable EnterPassthrough(RewardGrantPolicy policy)
    {
        if (!IsPassthrough(policy))
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "The policy is not a passthrough grant policy.");

        return EnterOverride(policy);
    }

    /// <summary>
    /// Only procedural permanent rewards and permanent transformations are
    /// constrained by the manually selected whitelist. Authored fixed cards,
    /// generated/derived cards, curses/statuses, copies/returns, and Ancient
    /// mechanics preserve their native behavior.
    /// </summary>
    internal static bool RequiresSelectedPool(RewardGrantPolicy policy) => policy is
        RewardGrantPolicy.GenericRandomReward
        or RewardGrantPolicy.Transform;

    internal static bool IsPassthrough(RewardGrantPolicy policy) => policy is
        RewardGrantPolicy.FixedSingle
        or RewardGrantPolicy.FixedEventPool
        or RewardGrantPolicy.Generated
        or RewardGrantPolicy.CurseStatus
        or RewardGrantPolicy.CopyReturn
        or RewardGrantPolicy.Ancient;

    internal static IDisposable EnterOverride(RewardGrantPolicy policy)
    {
        var previous = ScopedOverride.Value;
        ScopedOverride.Value = policy;
        return new Scope(previous);
    }

    internal static void ResetScopesForRunTransition() =>
        ScopedOverride.Value = null;

    private sealed class Scope(RewardGrantPolicy? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ScopedOverride.Value = previous;
        }
    }
}
