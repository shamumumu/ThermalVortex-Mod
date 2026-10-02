using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class InternalCombustion : FirePoleCard
{
    public const int BaseMaxHp = 2;

    public override string CustomPortraitPath => "internal_combustion.png".BigCardImagePath();
    public override string PortraitPath => "internal_combustion.png".CardImagePath();
    public override string BetaPortraitPath => "internal_combustion.png".CardImagePath();
    public override int MonsterMaxHp => CurrentUpgradeLevel > 0 ? 5 : BaseMaxHp;

    public InternalCombustion() : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        WithDamage(4, 2);
        WithMonsterHpUpgrade(BaseMaxHp, 5);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCombatVfx.CardAttack(this, play, 1).Execute(ctx);
        await ResolveSummon(ctx, play);
    }
}
