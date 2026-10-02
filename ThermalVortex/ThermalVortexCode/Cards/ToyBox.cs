using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class ToyBox : MainDeckCard
{
    public override string CustomPortraitPath => "toy_box.png".BigCardImagePath();
    public override string PortraitPath => "toy_box.png".CardImagePath();
    public override string BetaPortraitPath => "toy_box.png".CardImagePath();

    public ToyBox() : base(3, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(
            CardPreviewExplanation<ToyBoxToken>(),
            KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCommandCompat.ApplyPower<ToyBoxPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
        Owner.Creature.GetPower<ToyBoxPower>()?.BindUpgradedToken(CurrentUpgradeLevel > 0);
    }

}

public class ToyBoxToken : MonsterCard
{
    public const int BaseMaxHp = 1;
    public override string CustomPortraitPath => "toy_box_token.png".BigCardImagePath();
    public override string PortraitPath => "toy_box_token.png".CardImagePath();
    public override string BetaPortraitPath => "toy_box_token.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public ToyBoxToken() : base(0, CardType.Skill, CardRarity.Token, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.Add);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
