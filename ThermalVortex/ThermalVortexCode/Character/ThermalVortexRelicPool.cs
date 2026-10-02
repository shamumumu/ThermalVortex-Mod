using BaseLib.Abstracts;
using ThermalVortex.ThermalVortexCode.Extensions;
using Godot;

namespace ThermalVortex.ThermalVortexCode.Character;

public class ThermalVortexRelicPool : CustomRelicPoolModel
{
    public override Color LabOutlineColor => ThermalVortex.Color;

    public override string BigEnergyIconPath => "charui/big_energy.png".ImagePath();
    public override string TextEnergyIconPath => "charui/text_energy.png".ImagePath();
}