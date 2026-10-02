using BaseLib.Abstracts;
using BaseLib.Extensions;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using System.Text;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Powers;

/// <summary>
/// This is the base class for your mod's powers, which is set up to load the power's images from your mod's resources.
/// When creating a power, right click the Powers folder and create a new file with the Custom Power template.
/// This will generate a class that extends this one.
/// You can also just create the class manually; just make sure to inherit from this class.
/// </summary>
public abstract class ThermalVortexPower : CustomPowerModel
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        base.CanonicalVars.Concat(PowerDescriptionValues.CreateVariables(this));

    protected override string SmartDescriptionLocKey =>
        PowerDescriptionValues.SelectDescriptionKey(this, base.SmartDescriptionLocKey);

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        base.ExtraHoverTips.Concat(PowerExplanationBindings.Create(this));

    public override string CustomPackedIconPath => PowerIconFileName.PowerImagePath();
    public override string CustomBigIconPath => PowerIconFileName.BigPowerImagePath();

    private string PowerIconFileName => $"{ToSnakeCase(RemovePowerSuffix(GetType().Name))}.png";

    private static string RemovePowerSuffix(string typeName) =>
        typeName.EndsWith("Power", StringComparison.Ordinal)
            ? typeName[..^"Power".Length]
            : typeName;

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsUpper(current) && i > 0)
            {
                var previous = value[i - 1];
                var nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || nextIsLower)
                    builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether this power is a buff or debuff.
    /// </summary>
    public abstract override PowerType Type { get; }

    /// <summary>
    /// How this power stacks if reapplied. Counter is the most common type, where applying the power again just
    /// adds to the amount. Single means the power does not stack, like Barricade. None functions identically to
    /// Single, but you're suggested to use Single as it is more explicit about how it will work.
    /// </summary>
    public abstract override PowerStackType StackType { get; }
}
