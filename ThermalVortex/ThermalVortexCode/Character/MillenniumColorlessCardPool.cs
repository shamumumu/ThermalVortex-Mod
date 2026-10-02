using BaseLib.Abstracts;
using Godot;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Character;

public class MillenniumColorlessCardPool : CustomCardPoolModel
{
    public override string Title => "ThermalVortexGeneratedColorless";
    public override bool IsShared => false;
    public override string BigEnergyIconPath => "charui/big_energy.png".ImagePath();
    public override string TextEnergyIconPath => "charui/text_energy.png".ImagePath();
    public override float H => 1f;
    public override float S => 0f;
    public override float V => 1f;
    public override Color DeckEntryCardColor => new("e8e8e8");
    public override bool IsColorless => true;
}
