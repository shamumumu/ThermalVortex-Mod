using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberEndDragon : XyzMonsterCard, ICyberMonster, ICyberDevourStats
{
    public const int RequiredMaterials = 2;
    public const int HpPerMaterial = 8;
    private const int HpLossPerMaterial = 15;
    private const int UpgradedHpLossPerMaterial = 20;

    public override string CustomPortraitPath => "cyber_end_dragon.png".BigCardImagePath();
    public override string PortraitPath => "cyber_end_dragon.png".CardImagePath();
    public override string BetaPortraitPath => "cyber_end_dragon.png".CardImagePath();
    public override int MinimumMaterials => RequiredMaterials;
    public override int MaximumMaterials => int.MaxValue;
    public override int MonsterMaxHp =>
        HpPerMaterial * Math.Max(MinimumMaterials, Materials);
    public int CyberAttackContribution =>
        (CurrentUpgradeLevel > 0 ? UpgradedHpLossPerMaterial : HpLossPerMaterial)
        * Math.Max(MinimumMaterials, Materials);

    public CyberEndDragon() : base(CardType.Skill, CardRarity.Rare, TargetType.AllEnemies)
    {
        WithUpgradeVar("HpLossPerMaterial", HpLossPerMaterial, UpgradedHpLossPerMaterial);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-CYBER_MONSTER"),
            BlockExplanation());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        if (play.IsFirstInSeries
            && IsResolvingAuthorizedExtraDeckSummon
            && Owner?.Creature?.CombatState is not null)
        {
            await TcgMonsterCutinVfx.PlaySummonAsync(this, play);
        }

        var hpLossPerMaterial = UpgradeVarValue("HpLossPerMaterial");
        var hpLoss = hpLossPerMaterial * Math.Max(MinimumMaterials, Materials);
        if (hpLoss <= 0)
            return;

        var enemies = Owner.Creature.CombatState.HittableEnemies.Where(enemy => !enemy.IsDead).ToList();
        using var output = CyberWeldingPower.BeginLifeLossOutput(this, hpLoss, enemies);
        var modifiedHpLoss = CyberWeldingPower.ScaleLifeLoss(this, hpLoss);
        foreach (var enemy in enemies)
        {
            ThermalVortexCombatVfx.PlayImpact(enemy);
            await CreatureCmd.SetCurrentHp(enemy, Math.Max(0, enemy.CurrentHp - modifiedHpLoss));
        }
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
