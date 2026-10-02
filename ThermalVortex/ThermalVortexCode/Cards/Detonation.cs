using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class Detonation : XyzMonsterCard
{
    public const int RequiredMaterials = 2;
    public const int BaseMaxHp = 8;
    private const int BaseDamage = 12;
    private const int UpgradedDamage = 16;
    public override string CustomPortraitPath => "detonation.png".BigCardImagePath();
    public override string PortraitPath => "detonation.png".CardImagePath();
    public override string BetaPortraitPath => "detonation.png".CardImagePath();
    public override int MinimumMaterials => RequiredMaterials;
    public override int MaximumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => BaseMaxHp;

    public Detonation() : base(CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
    {
        WithUpgradeVar("FixedDamage", BaseDamage, UpgradedDamage);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCombatVfx.PlaySafeAsync(
            nameof(DetonationVfx),
            () => DetonationVfx.PlayAsync(play.Target));
        await ThermalVortexCombatVfx.CardAttack(this, play.Target, UpgradeVarValue("FixedDamage"), 1).Execute(ctx);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
