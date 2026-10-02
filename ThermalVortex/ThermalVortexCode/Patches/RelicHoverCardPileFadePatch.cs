using Godot;
using HarmonyLib;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
internal static class RelicHoverCardPileFadeCombatActivatePatch
{
    private static void Postfix(NCombatUi __instance, CombatState __0)
    {
        RelicHoverCardPileFade.SetCombatUi(__instance);
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Deactivate))]
internal static class RelicHoverCardPileFadeCombatDeactivatePatch
{
    private static void Prefix(NCombatUi __instance)
    {
        RelicHoverCardPileFade.ClearCombatUi(__instance, "combat_deactivate");
    }
}

[HarmonyPatch(typeof(NCombatUi), "_ExitTree")]
internal static class RelicHoverCardPileFadeCombatExitTreePatch
{
    private static void Prefix(NCombatUi __instance)
    {
        RelicHoverCardPileFade.ClearCombatUi(__instance, "combat_exit_tree");
    }
}

[HarmonyPatch(typeof(CombatManager), "EndCombatInternal")]
internal static class RelicHoverCardPileFadeCombatEndPatch
{
    private static void Prefix()
    {
        RelicHoverCardPileFade.ClearAll("combat_end");
    }
}

[HarmonyPatch(typeof(CombatManager), "Reset", typeof(bool))]
internal static class RelicHoverCardPileFadeCombatResetPatch
{
    private static void Prefix()
    {
        RelicHoverCardPileFade.ClearAll("combat_reset");
    }
}

[HarmonyPatch(typeof(NRelicInventory), "OnRelicUnfocused")]
internal static class RelicHoverCardPileFadeInventoryUnfocusedPatch
{
    private static void Postfix()
    {
        RelicHoverCardPileFade.EndRelicHover("relic_inventory_unfocus");
    }
}

[HarmonyPatch(typeof(NRelicInventory), nameof(NRelicInventory._ExitTree))]
internal static class RelicHoverCardPileFadeInventoryExitTreePatch
{
    private static void Prefix()
    {
        RelicHoverCardPileFade.EndRelicHover("relic_inventory_exit_tree");
    }
}

[HarmonyPatch(typeof(NRelicInventoryHolder), "OnUnfocus")]
internal static class RelicHoverCardPileFadeInventoryHolderUnfocusPatch
{
    private static void Postfix()
    {
        RelicHoverCardPileFade.EndRelicHover("relic_holder_unfocus");
    }
}

[HarmonyPatch(typeof(NRelicInventoryHolder), nameof(NRelicInventoryHolder._ExitTree))]
internal static class RelicHoverCardPileFadeInventoryHolderExitTreePatch
{
    private static void Prefix()
    {
        RelicHoverCardPileFade.EndRelicHover("relic_holder_exit_tree");
    }
}

[HarmonyPatch(typeof(NRelicBasicHolder), "OnUnfocus")]
internal static class RelicHoverCardPileFadeBasicHolderUnfocusPatch
{
    private static void Postfix()
    {
        RelicHoverCardPileFade.EndRelicHover("basic_relic_holder_unfocus");
    }
}

[HarmonyPatch(typeof(NRelicBasicHolder), nameof(NRelicBasicHolder._ExitTree))]
internal static class RelicHoverCardPileFadeBasicHolderExitTreePatch
{
    private static void Prefix()
    {
        RelicHoverCardPileFade.EndRelicHover("basic_relic_holder_exit_tree");
    }
}

[HarmonyPatch(
    typeof(NHoverTipSet),
    nameof(NHoverTipSet.CreateAndShow),
    typeof(Control),
    typeof(IHoverTip),
    typeof(HoverTipAlignment))]
internal static class RelicHoverTipSetSingleCreatePatch
{
    private static void Postfix(Control __0, NHoverTipSet __result)
    {
        RelicHoverCardPileFade.BeginRelicHover(__0, __result);
    }
}

[HarmonyPatch(
    typeof(NHoverTipSet),
    nameof(NHoverTipSet.CreateAndShow),
    typeof(Control),
    typeof(IEnumerable<IHoverTip>),
    typeof(HoverTipAlignment))]
internal static class RelicHoverTipSetMultiCreatePatch
{
    private static void Postfix(Control __0, NHoverTipSet __result)
    {
        RelicHoverCardPileFade.BeginRelicHover(__0, __result);
    }
}

[HarmonyPatch(typeof(NHoverTipSet), nameof(NHoverTipSet.Clear))]
internal static class RelicHoverTipSetClearPatch
{
    private static void Prefix()
    {
        RelicHoverCardPileFade.EndRelicHover("hover_tip_clear");
    }
}

[HarmonyPatch(typeof(NHoverTipSet), nameof(NHoverTipSet.Remove))]
internal static class RelicHoverTipSetRemovePatch
{
    private static void Prefix(Control __0)
    {
        RelicHoverCardPileFade.EndRelicHoverForOwner(__0, "hover_tip_remove");
    }
}

[HarmonyPatch(typeof(NHoverTipSet), nameof(NHoverTipSet._Process))]
internal static class RelicHoverTipSetProcessPatch
{
    private static void Postfix(NHoverTipSet __instance)
    {
        RelicHoverCardPileFade.RefreshFadeForLayout(__instance);
    }
}

internal static class RelicHoverCardPileFade
{
    private const float MonsterFieldHoverAlpha = 0.18f;
    private static readonly FieldInfo TextHoverTipContainerField =
        AccessTools.Field(typeof(NHoverTipSet), "_textHoverTipContainer");

    private static NCombatUi CurrentCombatUi;
    private static Control ActiveRelicOwner;
    private static NHoverTipSet ActiveHoverTipSet;
    private static Control FadedMonsterFieldUi;
    private static Color OriginalMonsterFieldModulate;
    private static bool HasFadedMonsterField;

    internal static void SetCombatUi(NCombatUi ui)
    {
        EndRelicHover("combat_activate");
        CurrentCombatUi = ui;
    }

    internal static void ClearCombatUi(NCombatUi ui, string reason)
    {
        if (CurrentCombatUi is not null && !ReferenceEquals(CurrentCombatUi, ui))
        {
            return;
        }

        EndRelicHover(reason);
        CurrentCombatUi = null;
    }

    internal static void ClearAll(string reason)
    {
        EndRelicHover(reason);
        CurrentCombatUi = null;
    }

    internal static void BeginRelicHover(Control owner, NHoverTipSet hoverTipSet)
    {
        if (!IsValid(owner) || !IsValid(hoverTipSet))
            return;

        if (!ShouldFadeMonsterFieldFor(owner))
        {
            if (IsValid(ActiveHoverTipSet))
                EndRelicHover("new_non_topbar_hover");
            return;
        }

        EndRelicHover("new_relic_hover");
        ActiveRelicOwner = owner;
        ActiveHoverTipSet = hoverTipSet;
        MainFile.Logger.Info($"RelicHoverCardPileFade begin owner={owner.GetType().Name}");
        RefreshFadeForLayout(hoverTipSet);
    }

    internal static void EndRelicHover(string reason)
    {
        RestoreMonsterField(reason);
        ActiveRelicOwner = null;
        ActiveHoverTipSet = null;
    }

    internal static void EndRelicHoverForOwner(Control owner, string reason)
    {
        if (ReferenceEquals(ActiveRelicOwner, owner))
            EndRelicHover(reason);
    }

    internal static void RefreshFadeForLayout(NHoverTipSet hoverTipSet)
    {
        if (!ReferenceEquals(ActiveHoverTipSet, hoverTipSet))
            return;

        if (!IsValid(ActiveRelicOwner)
            || !IsValid(hoverTipSet)
            || !hoverTipSet.IsVisibleInTree())
        {
            EndRelicHover("hover_tip_invalid_or_hidden");
            return;
        }

        var monsterFieldUi = FindMonsterFieldUi();
        var textHoverTipContainer = FindTextHoverTipContainer(hoverTipSet);
        var overlaps = IsVisibleControl(monsterFieldUi)
            && IsVisibleControl(textHoverTipContainer)
            && RectsOverlap(monsterFieldUi.GetGlobalRect(), textHoverTipContainer.GetGlobalRect());

        if (overlaps)
        {
            if (!HasFadedMonsterField)
                FadeMonsterField(monsterFieldUi);
        }
        else
        {
            RestoreMonsterField("tooltip_no_longer_overlaps");
        }
    }

    private static void FadeMonsterField(Control monsterFieldUi)
    {
        if (!IsValid(monsterFieldUi))
        {
            MainFile.Logger.Info($"RelicHoverCardPileFade target=monsterField missing alpha={MonsterFieldHoverAlpha:0.##}");
            return;
        }

        FadedMonsterFieldUi = monsterFieldUi;
        OriginalMonsterFieldModulate = monsterFieldUi.Modulate;
        HasFadedMonsterField = true;

        var color = OriginalMonsterFieldModulate;
        color.A *= MonsterFieldHoverAlpha;
        monsterFieldUi.Modulate = color;

        MainFile.Logger.Info($"RelicHoverCardPileFade target=monsterField alpha={MonsterFieldHoverAlpha:0.##}");
    }

    private static void RestoreMonsterField(string reason)
    {
        if (!HasFadedMonsterField)
        {
            return;
        }

        if (IsValid(FadedMonsterFieldUi))
        {
            FadedMonsterFieldUi.Modulate = OriginalMonsterFieldModulate;
        }

        MainFile.Logger.Info($"RelicHoverCardPileFade restore target=monsterField reason={reason}");
        FadedMonsterFieldUi = null;
        OriginalMonsterFieldModulate = default;
        HasFadedMonsterField = false;
    }

    private static Control FindMonsterFieldUi()
    {
        if (IsValid(CurrentCombatUi)
            && CurrentCombatUi.FindChild(MonsterFieldUi.NodeName, true, false) is Control monsterFieldUi)
        {
            return monsterFieldUi;
        }

        var root = ActiveRelicOwner?.GetTree()?.Root ?? ActiveHoverTipSet?.GetTree()?.Root;
        return root?.FindChild(MonsterFieldUi.NodeName, true, false) as Control;
    }

    private static Control FindTextHoverTipContainer(NHoverTipSet hoverTipSet)
    {
        if (!IsValid(hoverTipSet) || TextHoverTipContainerField is null)
            return null;

        return TextHoverTipContainerField.GetValue(hoverTipSet) as Control;
    }

    private static bool IsVisibleControl(Control control) =>
        IsValid(control)
        && control.IsVisibleInTree()
        && control.Size.X > 0f
        && control.Size.Y > 0f;

    private static bool RectsOverlap(Rect2 first, Rect2 second)
    {
        var firstEnd = first.Position + first.Size;
        var secondEnd = second.Position + second.Size;
        return first.Position.X < secondEnd.X
            && firstEnd.X > second.Position.X
            && first.Position.Y < secondEnd.Y
            && firstEnd.Y > second.Position.Y;
    }

    private static bool ShouldFadeMonsterFieldFor(Control owner)
    {
        for (Node node = owner; node is not null; node = node.GetParent())
        {
            if (node is NRelicInventory or NRelicInventoryHolder or NRelicBasicHolder or NTopBar)
            {
                return true;
            }

            // The portrait tip owns the character/ascension description. Keep the
            // direct type-name check in case a game scene places it outside NTopBar.
            if (node.GetType().Name == "NTopBarPortraitTip")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValid(GodotObject instance) =>
        instance is not null && GodotObject.IsInstanceValid(instance);
}
