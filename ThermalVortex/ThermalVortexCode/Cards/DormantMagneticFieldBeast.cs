using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Vfx;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class DormantMagneticFieldBeast : XyzMonsterCard
{
    public const int MinimumRequiredMaterials = 2;
    public const int MaximumRequiredMaterials = 3;
    public const int BaseMaxHp = 6;
    public override string CustomPortraitPath => "dormant_magnetic_field_beast.png".BigCardImagePath();
    public override string PortraitPath => "dormant_magnetic_field_beast.png".CardImagePath();
    public override string BetaPortraitPath => "dormant_magnetic_field_beast.png".CardImagePath();
    public override int MinimumMaterials => MinimumRequiredMaterials;
    public override int MaximumMaterials => MaximumRequiredMaterials;
    public override int MonsterMaxHp => BaseMaxHp;

    public DormantMagneticFieldBeast() : base(CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
        WithUpgradeVar(
            "Weak",
            1,
            MinimumRequiredMaterials,
            static card => card is DormantMagneticFieldBeast beast
                ? Math.Clamp(beast.Materials, beast.MinimumMaterials, beast.MaximumMaterials)
                : MinimumRequiredMaterials);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-WEAK"),
            KeywordExplanation("THERMALVORTEX-DORMANT_MAGNETIC_FIELD"),
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            BlockExplanation());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var materials = Math.Clamp(Materials, MinimumMaterials, MaximumMaterials);
        var enemies = Owner.Creature.CombatState.HittableEnemies.ToList();
        await ThermalVortexCombatVfx.PlaySafeAsync(
            nameof(DormantMagneticFieldBeastVfx),
            () => DormantMagneticFieldBeastVfx.PlayAsync(Owner.Creature, enemies, materials));

        await ThermalVortexCommandCompat.GainBlockFromCardEffect(
            Owner.Creature, 8 + (4 * materials), ValueProp.Unpowered, play, false);

        if (enemies.Count > 0)
            await ThermalVortexCommandCompat.ApplyPower<WeakPower>(ctx, enemies, CurrentUpgradeLevel > 0 ? materials : 1, Owner.Creature, this, false);

        await ThermalVortexCommandCompat.ApplyPower<DormantMagneticFieldPower>(ctx, Owner.Creature, materials, Owner.Creature, this, false);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
