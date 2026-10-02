using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using ThermalVortex.ThermalVortexCode.Character;

namespace ThermalVortex.ThermalVortexCode.Patches;

/// <summary>
/// Restores the base-game draw order for legacy custom energy counters.
/// BaseLib adds RotationLayers before the opaque Layer1, which hides Layer2 and Layer3.
/// </summary>
[HarmonyPatch(typeof(NEnergyCounter))]
internal static class EnergyCounterLayerOrderPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(nameof(NEnergyCounter.Create), [typeof(Player)])]
    private static void CreatePostfix(Player __0, NEnergyCounter __result)
    {
        if (__result is null || !MillenniumPuzzleCharacterArt.IsThermalVortex(__0))
            return;

        var layers = __result.GetNodeOrNull<Control>("%Layers")
            ?? __result.GetNodeOrNull<Control>("Layers");
        var rotationLayers = __result.GetNodeOrNull<Control>("%RotationLayers")
            ?? layers?.GetNodeOrNull<Control>("RotationLayers");
        if (layers is null || rotationLayers is null || rotationLayers.GetParent() != layers)
            return;

        // Match the original scene: Layer1, RotationLayers, Layer4, Layer5.
        layers.MoveChild(rotationLayers, 1);
    }
}
