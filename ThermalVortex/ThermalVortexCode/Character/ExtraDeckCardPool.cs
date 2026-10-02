using BaseLib.Abstracts;
using Godot;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Character;

public class ExtraDeckCardPool : CustomCardPoolModel
{
    public static readonly Color Color = new("c8f2ff");
    public const float FrameHue = 0.56f;
    public const float FrameSaturation = 0.34f;
    public const float FrameValue = 1.0f;

    public override string Title => "ThermalVortexExtraDeck";
    public override bool IsShared => false;
    public override string BigEnergyIconPath => "charui/extra_deck_material_big.png".ImagePath();
    public override string TextEnergyIconPath => "charui/extra_deck_material_text.png".ImagePath();
    public override float H => FrameHue;
    public override float S => FrameSaturation;
    public override float V => FrameValue;
    public override Color DeckEntryCardColor => Color;
    public override bool IsColorless => false;
}
