using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class SectionPole : ThunderPoleCard
{
    public const int BaseMaxHp = 2;

    public override string CustomPortraitPath => "section_pole.png".BigCardImagePath();
    public override string PortraitPath => "section_pole.png".CardImagePath();
    public override string BetaPortraitPath => "section_pole.png".CardImagePath();
    public override int MonsterMaxHp => CurrentUpgradeLevel > 0 ? 5 : BaseMaxHp;

    public SectionPole() : base(2, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithBlock(3, 3);
        WithMonsterHpUpgrade(BaseMaxHp, 5);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await CommonActions.CardBlock(this, play);
        await ResolveSummon(ctx, play);
    }
}
