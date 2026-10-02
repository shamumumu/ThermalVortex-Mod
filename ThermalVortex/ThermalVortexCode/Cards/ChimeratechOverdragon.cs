using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class ChimeratechOverdragon : XyzMonsterCard, ICyberMonster, ICyberDevourStats
{
    public const int RequiredMaterials = 2;
    internal const int MaxHpPerMaterial = 4;
    private const int DamagePerMaterial = 7;
    private const int UpgradedDamagePerMaterial = 11;
    public override string CustomPortraitPath => "chimeratech_overdragon.png".BigCardImagePath();
    public override string PortraitPath => "chimeratech_overdragon.png".CardImagePath();
    public override string BetaPortraitPath => "chimeratech_overdragon.png".CardImagePath();
    public override int MinimumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => MaxHpPerMaterial * Math.Max(MinimumMaterials, Materials);
    public int CyberAttackContribution => FormulaDamageUnit * Math.Max(MinimumMaterials, Materials);

    public ChimeratechOverdragon() : base(CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
        WithFormulaDamage(
            DamagePerMaterial,
            UpgradedDamagePerMaterial,
            (card, _) => Math.Max(
                ((ChimeratechOverdragon)card).MinimumMaterials,
                ((ChimeratechOverdragon)card).Materials));
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            CardPreviewExplanation<CyberDragon>(matchSourceUpgrade: false));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        if (play.Target is not null)
        {
            var damage = (int)FormulaDamage.Calculate(play.Target)
                + CyberDevourState.GetInheritedAttackContribution(this);
            await ThermalVortexCombatVfx.CardAttack(this, play.Target, damage, 1).Execute(ctx);
        }
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
