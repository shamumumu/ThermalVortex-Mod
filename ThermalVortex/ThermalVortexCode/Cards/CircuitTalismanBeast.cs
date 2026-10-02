using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CircuitTalismanBeast : XyzMonsterCard
{
    public override string CustomPortraitPath => "circuit_talisman_beast.png".BigCardImagePath();
    public override string PortraitPath => "circuit_talisman_beast.png".CardImagePath();
    public override string BetaPortraitPath => "circuit_talisman_beast.png".CardImagePath();

    public const int RequiredMaterials = 2;
    public const int BaseMaxHp = 5;
    public const int ConsumedBlock = 8;
    public const int UpgradedConsumedBlock = 12;
    public int CurrentConsumedBlock => UpgradeVarValue("ConsumedBlock");
    public override int MinimumMaterials => RequiredMaterials;
    public override int MaximumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => BaseMaxHp;

    public CircuitTalismanBeast() : base(CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
        WithUpgradeVar("Weak", 1, 2);
        WithUpgradeVar("ConsumedBlock", ConsumedBlock, UpgradedConsumedBlock);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-WEAK"),
            KeywordExplanation("THERMALVORTEX-CONSUME_TRIGGER"),
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            BlockExplanation());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var enemies = Owner.Creature.CombatState.HittableEnemies.ToList();
        if (enemies.Count > 0)
            await ThermalVortexCommandCompat.ApplyPower<WeakPower>(ctx, enemies, UpgradeVarValue("Weak"), Owner.Creature, this, false);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
