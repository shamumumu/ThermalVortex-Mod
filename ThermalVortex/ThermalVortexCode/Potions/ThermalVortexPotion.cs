using BaseLib.Abstracts;
using BaseLib.Utils;
using ThermalVortex.ThermalVortexCode.Character;

namespace ThermalVortex.ThermalVortexCode.Potions;

[Pool(typeof(ThermalVortexPotionPool))]
public abstract class ThermalVortexPotion : CustomPotionModel;