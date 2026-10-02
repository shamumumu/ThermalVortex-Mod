using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.RewardPools;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal enum RewardPoolCapabilityApplicationPhase
{
    FreshRun,
    LoadedRun
}

internal enum RewardPoolOrbSlotMigration
{
    None,
    AdoptedLegacyDefectContribution,
    RemovedLegacyInvalidDefectSlots
}

internal readonly record struct RewardPoolOrbSlotCoordination(
    int BaseOrbSlotCount,
    int Contribution,
    bool ContributionKnown,
    RewardPoolOrbSlotMigration Migration);

internal static class RewardPoolForeignMechanics
{
    private static readonly ConditionalWeakTable<NStarCounter, StarCounterContext> StarCounterContexts = new();
    private const int LegacyDefectOrbSlotCount = 3;

    /// <summary>
    /// Reapplies run-persistent capabilities implied by the selected reward
    /// pool. Safe to call after both new-player creation and full save load.
    /// </summary>
    internal static void ApplyPersistentCapabilities(
        Player player,
        RewardPoolCapabilityApplicationPhase phase)
    {
        if (player?.Character is not ThermalVortexCharacter)
            return;

        var core = player.GetRelic<ThermalVortexCore>();
        if (core is null)
            return;

        var hasDefectCards = TryGetManualDefinition(player, out var definition)
            && RewardPoolCatalog.DefinitionContainsOrigin(definition, RewardPoolCardOrigin.Defect);
        var defectOrbSlots = ModelDb.Character<Defect>()?.BaseOrbSlotCount ?? 0;
        var coordination = CoordinateOrbSlots(
            player.BaseOrbSlotCount,
            player.Character.BaseOrbSlotCount,
            defectOrbSlots,
            hasDefectCards,
            core.RewardPoolOrbSlotContribution,
            core.HasRewardPoolOrbSlotContributionMetadata,
            core.RewardPoolDefinitionLoadState,
            phase);

        var previousBaseOrbSlots = player.BaseOrbSlotCount;
        if (coordination.BaseOrbSlotCount != previousBaseOrbSlots)
        {
            player.BaseOrbSlotCount = coordination.BaseOrbSlotCount;
            ReconcileLoadedCombatOrbQueue(
                player,
                coordination.BaseOrbSlotCount - previousBaseOrbSlots,
                phase);
        }

        if (coordination.ContributionKnown)
            core.SetRewardPoolOrbSlotContribution(coordination.Contribution);

        switch (coordination.Migration)
        {
            case RewardPoolOrbSlotMigration.AdoptedLegacyDefectContribution:
                MainFile.Logger.Info(
                    $"Reward pool orb-slot migration adopted legacy Defect contribution={coordination.Contribution} slots={coordination.BaseOrbSlotCount}");
                break;
            case RewardPoolOrbSlotMigration.RemovedLegacyInvalidDefectSlots:
                MainFile.Logger.Info(
                    $"Reward pool orb-slot migration removed legacy invalid-snapshot slots previous={LegacyDefectOrbSlotCount} restored={coordination.BaseOrbSlotCount}");
                break;
        }
    }

    /// <summary>
    /// Pure coordination hook used by diagnostics. The contribution is the
    /// portion of the persistent player slot count owned by this reward-pool
    /// feature, never the character baseline or another mod's later increase.
    /// </summary>
    internal static RewardPoolOrbSlotCoordination CoordinateOrbSlots(
        int currentBaseOrbSlots,
        int characterBaseOrbSlots,
        int defectBaseOrbSlots,
        bool hasDefectCards,
        int savedContribution,
        bool hasContributionMetadata,
        RewardPoolDefinitionLoadState definitionLoadState,
        RewardPoolCapabilityApplicationPhase phase)
    {
        var current = Math.Max(0, currentBaseOrbSlots);
        var baseline = Math.Max(0, characterBaseOrbSlots);
        var defectSlots = Math.Max(0, defectBaseOrbSlots);
        var contribution = Math.Max(0, savedContribution);

        if (hasDefectCards)
        {
            var contributionKnown = hasContributionMetadata;
            var migration = RewardPoolOrbSlotMigration.None;
            var maximumOwnedAtCurrentCount = Math.Max(0, current - baseline);

            if (hasContributionMetadata)
            {
                contribution = Math.Min(contribution, maximumOwnedAtCurrentCount);
            }
            else if (phase == RewardPoolCapabilityApplicationPhase.LoadedRun
                     && definitionLoadState == RewardPoolDefinitionLoadState.ValidSavedSnapshot)
            {
                // Saves produced before contribution tracking already contain
                // the old max(..., DefectBase) adjustment. Adopt at most that
                // legacy adjustment, leaving any slots above it to other mods.
                var legacyContributionCeiling = Math.Max(0, defectSlots - baseline);
                contribution = Math.Min(maximumOwnedAtCurrentCount, legacyContributionCeiling);
                contributionKnown = true;
                migration = RewardPoolOrbSlotMigration.AdoptedLegacyDefectContribution;
            }
            else
            {
                contribution = 0;
            }

            var target = Math.Max(current, defectSlots);
            var added = target - current;
            if (added > 0)
            {
                contribution += added;
                contributionKnown = true;
            }

            // A fresh run establishes an authoritative contribution even when
            // another source had already supplied all required slots.
            if (phase == RewardPoolCapabilityApplicationPhase.FreshRun)
                contributionKnown = true;

            return new RewardPoolOrbSlotCoordination(
                target,
                contribution,
                contributionKnown,
                migration);
        }

        if (hasContributionMetadata)
        {
            // The saved count identifies exactly what this feature owns. Never
            // lower the player below the character's native baseline.
            return new RewardPoolOrbSlotCoordination(
                Math.Max(baseline, current - contribution),
                0,
                true,
                RewardPoolOrbSlotMigration.None);
        }

        // Current releases previously wrote the Defect-adjusted player count
        // without provenance. Only repair the exact known legacy signature when
        // the definition is positively corrupt. A missing snapshot is common
        // for old saves and must never trigger this heuristic.
        if (phase == RewardPoolCapabilityApplicationPhase.LoadedRun
            && definitionLoadState == RewardPoolDefinitionLoadState.InvalidSavedSnapshot
            && baseline == 0
            && defectSlots == LegacyDefectOrbSlotCount
            && current == LegacyDefectOrbSlotCount)
        {
            return new RewardPoolOrbSlotCoordination(
                baseline,
                0,
                true,
                RewardPoolOrbSlotMigration.RemovedLegacyInvalidDefectSlots);
        }

        return new RewardPoolOrbSlotCoordination(
            current,
            0,
            phase == RewardPoolCapabilityApplicationPhase.FreshRun,
            RewardPoolOrbSlotMigration.None);
    }

    internal static bool ShouldForceStarCounterVisible(Player player) =>
        TryGetManualDefinition(player, out var definition)
        && RewardPoolCatalog.DefinitionContainsOrigin(definition, RewardPoolCardOrigin.Regent);

    private static void ReconcileLoadedCombatOrbQueue(
        Player player,
        int baseSlotDelta,
        RewardPoolCapabilityApplicationPhase phase)
    {
        if (phase != RewardPoolCapabilityApplicationPhase.LoadedRun
            || baseSlotDelta == 0
            || player?.PlayerCombatState?.OrbQueue is not { } queue)
        {
            return;
        }

        // A combat save already owns an OrbQueue created from the serialized
        // player slot count.  Applying a migrated persistent contribution
        // after RunState.FromSerializable must mirror only that base-slot
        // delta, preserving unrelated temporary/permanent capacity changes.
        if (baseSlotDelta > 0)
            queue.AddCapacity(baseSlotDelta);
        else
            queue.RemoveCapacity(Math.Min(-baseSlotDelta, queue.Capacity));
    }

    internal static void RegisterStarCounter(NStarCounter counter, Player player)
    {
        if (counter is null)
            return;

        StarCounterContexts.Remove(counter);
        if (player is not null)
            StarCounterContexts.Add(counter, new StarCounterContext(player));
    }

    internal static void ApplyStarCounterVisibility(NStarCounter counter)
    {
        if (counter is not null
            && StarCounterContexts.TryGetValue(counter, out var context)
            && ShouldForceStarCounterVisible(context.Player))
        {
            counter.Visible = true;
        }
    }

    private static bool TryGetManualDefinition(
        Player player,
        out RewardPoolDefinition definition)
    {
        definition = RewardPoolDefinition.Standard;
        if (player?.Character is not ThermalVortexCharacter)
            return false;

        var core = player.GetRelic<ThermalVortexCore>();
        if (core?.IsConstructedRewardPoolEnabled != true)
            return false;

        definition = core.CurrentRewardPoolDefinition;
        return definition?.IsManual == true;
    }

    private sealed class StarCounterContext(Player player)
    {
        internal Player Player { get; } = player;
    }
}

[HarmonyPatch(
    typeof(NStarCounter),
    nameof(NStarCounter.Initialize),
    [typeof(Player)])]
internal static class RewardPoolStarCounterInitializePatch
{
    [HarmonyPrefix]
    private static void RememberPlayer(NStarCounter __instance, Player __0)
    {
        RewardPoolForeignMechanics.RegisterStarCounter(__instance, __0);
    }

    [HarmonyPostfix]
    private static void ForceVisibilityAfterInitialize(NStarCounter __instance)
    {
        RewardPoolForeignMechanics.ApplyStarCounterVisibility(__instance);
    }
}

[HarmonyPatch(typeof(NStarCounter), "RefreshVisibility")]
internal static class RewardPoolStarCounterRefreshVisibilityPatch
{
    [HarmonyPostfix]
    private static void ForceVisibilityForRegentPool(NStarCounter __instance)
    {
        RewardPoolForeignMechanics.ApplyStarCounterVisibility(__instance);
    }
}
