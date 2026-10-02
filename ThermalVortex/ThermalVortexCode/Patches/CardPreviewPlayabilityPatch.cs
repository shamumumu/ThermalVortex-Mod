using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Patches;

// Native cost colors honor pretend-playable previews, but their crossed-out
// overlays still call Model.CanPlay. Fusion/material and targeting previews
// use Hand visuals for live effect values, without being playable hand cards.
[HarmonyPatch(typeof(NCard), "UpdateEnergyCostVisuals")]
internal static class CardPreviewEnergyPlayabilityPatch
{
    private static void Postfix(
        NCard __instance,
        bool ____pretendCardCanBePlayed,
        TextureRect ____unplayableEnergyIcon)
    {
        if (__instance.Model is ThermalVortexCard && ____pretendCardCanBePlayed
            && GodotObject.IsInstanceValid(____unplayableEnergyIcon))
            ____unplayableEnergyIcon.Visible = false;
    }
}

[HarmonyPatch(typeof(NCard), "UpdateStarCostVisuals")]
internal static class CardPreviewStarPlayabilityPatch
{
    private static void Postfix(
        NCard __instance,
        bool ____pretendCardCanBePlayed,
        TextureRect ____unplayableStarIcon)
    {
        if (__instance.Model is ThermalVortexCard && ____pretendCardCanBePlayed
            && GodotObject.IsInstanceValid(____unplayableStarIcon))
            ____unplayableStarIcon.Visible = false;
    }
}
