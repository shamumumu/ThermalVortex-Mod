using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch]
internal static class MonsterFieldUpgradeLockPatch
{
    [HarmonyPatch(
        typeof(CardCmd),
        nameof(CardCmd.Upgrade),
        [typeof(IEnumerable<CardModel>), typeof(CardPreviewStyle)])]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void DeferUpgrades(ref IEnumerable<CardModel> __0)
    {
        if (__0 is null)
            return;

        var immediateCards = new List<CardModel>();
        foreach (var card in __0.ToList())
        {
            if (!MonsterFieldUpgradeLockService.TryDeferUpgrade(card))
                immediateCards.Add(card);
        }

        __0 = immediateCards;
    }

    [HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Downgrade), [typeof(CardModel)])]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool DeferDowngrade(CardModel __0) =>
        !MonsterFieldUpgradeLockService.TryDeferDowngrade(__0);
}

[HarmonyPatch]
internal static class MonsterFieldDampenCompatibilityPatch
{
    private static readonly System.Reflection.FieldInfo InternalDataField =
        AccessTools.Field(typeof(PowerModel), "_internalData");
    private static readonly System.Reflection.FieldInfo RestoreLevelsField =
        AccessTools.Field(
            AccessTools.Inner(typeof(DampenPower), "Data"),
            "downgradedCardsToOldUpgradeLevels");

    [HarmonyPatch(
        typeof(DampenPower),
        nameof(DampenPower.AfterApplied),
        [typeof(Creature), typeof(CardModel)])]
    [HarmonyPrefix]
    private static void CaptureProjectedLevels(DampenPower __instance, out List<DampenCardState> __state)
    {
        __state = [];
        var restoreLevels = GetRestoreLevels(__instance);
        if (restoreLevels is null)
            return;

        var cards = __instance.Owner?.Player?.PlayerCombatState?.AllCards?.ToList() ?? [];
        foreach (var card in cards)
        {
            if (!MonsterFieldUpgradeLockService.TryGetProjectedUpgradeLevel(card, out var projectedLevel))
                continue;

            var hadRestoreLevel = restoreLevels.TryGetValue(card, out var restoreLevel);
            __state.Add(new DampenCardState(card, projectedLevel, hadRestoreLevel, restoreLevel));
            if (hadRestoreLevel)
                restoreLevels.Remove(card);
        }
    }

    [HarmonyPatch(
        typeof(DampenPower),
        nameof(DampenPower.AfterApplied),
        [typeof(Creature), typeof(CardModel)])]
    [HarmonyPostfix]
    private static void ApplyProjectedLevels(DampenPower __instance, List<DampenCardState> __state)
    {
        var restoreLevels = GetRestoreLevels(__instance);
        if (restoreLevels is null || __state is null)
            return;

        foreach (var state in __state)
        {
            var restoreLevel = state.HadRestoreLevel
                ? state.RestoreLevel
                : state.ProjectedLevel;
            if (restoreLevel > 0)
                restoreLevels[state.Card] = restoreLevel;
            else
                restoreLevels.Remove(state.Card);

            MonsterFieldUpgradeLockService.TryDeferDowngrade(state.Card);
        }
    }

    internal static void CopyRestoreLevelForReplay(CardModel source, CardModel duplicate)
    {
        var dampen = source?.Owner?.Creature?.GetPower<DampenPower>();
        var restoreLevels = GetRestoreLevels(dampen);
        if (restoreLevels?.TryGetValue(source, out var restoreLevel) == true)
            restoreLevels[duplicate] = restoreLevel;
    }

    internal static void RemoveRestoreLevel(CardModel card)
    {
        var dampen = card?.Owner?.Creature?.GetPower<DampenPower>();
        GetRestoreLevels(dampen)?.Remove(card);
    }

    private static Dictionary<CardModel, int> GetRestoreLevels(DampenPower dampen)
    {
        var data = dampen is null ? null : InternalDataField?.GetValue(dampen);
        return data is null
            ? null
            : RestoreLevelsField?.GetValue(data) as Dictionary<CardModel, int>;
    }

    private readonly record struct DampenCardState(
        CardModel Card,
        int ProjectedLevel,
        bool HadRestoreLevel,
        int RestoreLevel);
}
