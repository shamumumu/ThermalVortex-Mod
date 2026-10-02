using MegaCrit.Sts2.Core.Combat;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Relics;

// The string remains the permanent-format portion of a reservation. Combat
// identity and devour growth travel beside it and are never written to saves.
internal sealed class ExtraDeckReservation(
    string serializedEntry,
    int index,
    int materials,
    int ownedEntryIndex,
    long combatEntryId,
    ICombatState combat,
    CyberDevourCombatSnapshot devourSnapshot,
    FusionMaterialVisualSnapshot materialVisuals = null)
{
    internal string SerializedEntry { get; } = serializedEntry;
    internal int Index { get; } = Math.Max(0, index);
    internal int Materials { get; } = Math.Max(1, materials);
    internal int OwnedEntryIndex { get; } = ownedEntryIndex;
    internal long CombatEntryId { get; } = combatEntryId;
    internal ICombatState Combat { get; } = combat;
    internal CyberDevourCombatSnapshot DevourSnapshot { get; } = devourSnapshot?.DeepClone();
    internal FusionMaterialVisualSnapshot MaterialVisuals { get; } = materialVisuals?.DeepClone();

    internal ExtraDeckReservation DeepClone() =>
        new(SerializedEntry, Index, Materials, OwnedEntryIndex, CombatEntryId, Combat, DevourSnapshot, MaterialVisuals);
}
