using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace ThermalVortex.ThermalVortexCode.CardInspection;

internal sealed record CardInspectionDisplayContext(
    PileType Pile = PileType.None,
    CardPreviewMode Mode = CardPreviewMode.Normal,
    Creature PreviewTarget = null,
    bool ForceUnpowered = false,
    bool PretendPlayable = false);

internal sealed record CardInspectionEntry(
    CardModel Model,
    CardInspectionDisplayContext Display = null);

internal sealed record CardInspectionRequest(
    Node Owner,
    IReadOnlyList<CardInspectionEntry> Entries,
    int Index,
    Control ReturnFocus = null,
    bool ViewUpgraded = false,
    bool ReplaceOpenContent = false);
