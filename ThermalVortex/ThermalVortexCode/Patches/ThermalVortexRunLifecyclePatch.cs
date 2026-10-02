using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.RewardPools;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(
    typeof(Player),
    nameof(Player.CreateForNewRun),
    [typeof(CharacterModel), typeof(UnlockState), typeof(ulong)])]
internal static class ThermalVortexFreshPlayerPatch
{
    [HarmonyPrefix]
    private static void BeforeCreatePlayer()
    {
        ThermalVortexRunLifecycle.PrepareForFreshRun();
    }

    [HarmonyPostfix]
    private static void AfterCreatePlayer(Player __result)
    {
        if (__result?.Character is not ThermalVortexCharacter)
            return;

        var startingHp = Math.Max(1, __result.Character.StartingHp);
        if (__result.Creature.MaxHp != startingHp)
            __result.Creature.SetMaxHpInternal(startingHp);
        if (__result.Creature.CurrentHp != startingHp)
            __result.Creature.SetCurrentHpInternal(startingHp);

        var core = __result.GetRelic<ThermalVortexCore>();
        core?.ResetForNewRun();
        MainFile.Logger.Info(
            $"Fresh ThermalVortex player initialized hp={__result.Creature.CurrentHp}/{__result.Creature.MaxHp} extraDeck={core?.OwnedExtraDeckSave ?? "missing"}; reward-pool setup deferred until run context is known");
    }
}

[HarmonyPatch(typeof(RunState), nameof(RunState.FromSerializable), [typeof(SerializableRun)])]
internal static class ThermalVortexLoadRunLifecyclePatch
{
    [HarmonyPrefix]
    private static void BeforeLoadRun()
    {
        ThermalVortexRunLifecycle.PrepareForLoadedRun();
    }

    [HarmonyPostfix]
    private static void AfterLoadRun(RunState __result)
    {
        if (__result?.Players is null)
            return;

        foreach (var player in __result.Players)
        {
            RewardPoolForeignMechanics.ApplyPersistentCapabilities(
                player,
                RewardPoolCapabilityApplicationPhase.LoadedRun);
        }
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp), [typeof(bool)])]
internal static class ThermalVortexRunCleanupPatch
{
    [HarmonyPostfix]
    private static void AfterRunCleanup()
    {
        ThermalVortexRunLifecycle.ClearTransientState("run_cleanup");
        RewardPoolSetupService.Clear();
        RewardPoolLaunchCoordinator.Cancel("run_cleanup");
    }
}

internal static class ThermalVortexRunLifecycle
{
    internal static void PrepareForFreshRun()
    {
        ClearTransientState("fresh_run");
        ThermalVortexRunSummaryText.ResetForRunTransition();
        ResetCanonicalStarterRelic();
    }

    internal static void PrepareForLoadedRun()
    {
        ClearTransientState("load_run");
        RewardPoolSetupService.Clear();
        RewardPoolLaunchCoordinator.Cancel("load_run");
        ThermalVortexRunSummaryText.ResetForRunTransition();
        ResetCanonicalStarterRelic();
    }

    internal static void ClearTransientState(string reason)
    {
        ExtraDeckTopBarUi.DetachAll();
        MonsterFieldUi.DetachAll();
        RelicHoverCardPileFade.ClearAll(reason);
        ExtraDeckUpgradeSelectPatch.CleanupProxyCards();
        ExtraDeckEnchantmentProxyLifecycle.CleanupProxyCards();
        ThermalVortexCore.ClearAllCombatState();
        MonsterFieldHealthService.ResetAll();
        MonsterFieldService.ResetAll();
        CyberSeries.ResetAll();
        EnemyActionDrawResolution.ResetForRunTransition();
        SolemnStrikeActionNegation.ResetForRunTransition();
        AshBlossomActionNegation.ResetForRunTransition();
        MonsterFieldDamageGuard.ResetForRunTransition();
        ThermalVortexNeowRewardFilterScope.ResetForRunTransition();
        RaRevivalService.ResetForRunTransition();
        MainFile.Logger.Info($"ThermalVortex run-scoped state cleared reason={reason}");
    }

    private static void ResetCanonicalStarterRelic()
    {
        ModelDb.Relic<ThermalVortexCore>()?.ResetForNewRun();
    }
}
