using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils;
using ThermalVortex.ThermalVortexCode.Character;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Relics;

/// <summary>
/// This is the base class for your mod's relics, which is set up to load the relic's images from your mod's resources.
/// When creating a relic, right click the Relics folder and create a new file with the Custom Relic template.
/// This will generate a class that extends this one.
/// You can also just create the class manually; just make sure to inherit from this class.
///
/// The [Pool] annotation marks this relic as being tied to your specific character. Inheriting from this class means
/// that your relics don't need to invidually say which pool they should be in.
/// </summary>
[Pool(typeof(ThermalVortexRelicPool))]
public abstract class ThermalVortexRelic : CustomRelicModel
{
    protected virtual string RelicIconFileName => "relic.png";
    protected virtual string RelicIconOutlineFileName => "relic_outline.png";

    public string DiagnosticPackedIconOutlinePath => PackedIconOutlinePath;
    public string DiagnosticBigIconPath => BigIconPath;

    public override string PackedIconPath => RelicIconFileName.RelicImagePath();
    protected override string PackedIconOutlinePath => RelicIconOutlineFileName.RelicImagePath();
    protected override string BigIconPath => RelicIconFileName.BigRelicImagePath();
}
