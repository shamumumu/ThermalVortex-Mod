using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class ExodiaLeftLeg : SealedMillenniumPieceCard
{
    public const int BaseMaxHp = 1;

    public override string CustomPortraitPath => "exodia_left_leg.png".BigCardImagePath();
    public override string PortraitPath => "exodia_left_leg.png".CardImagePath();
    public override string BetaPortraitPath => "exodia_left_leg.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;
    public override MillenniumPieceSummonEffectKind SummonEffectKind => MillenniumPieceSummonEffectKind.Energy;

    public ExodiaLeftLeg() : base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_UNIQUE_HAND"),
            EnergyExplanation());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);
    }
}
