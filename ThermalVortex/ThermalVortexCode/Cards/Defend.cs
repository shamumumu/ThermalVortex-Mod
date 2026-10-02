using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class Defend : MainDeckCard
{
    public override string CustomPortraitPath => ModelDb.Card<DefendIronclad>().PortraitPath;
    public override string PortraitPath => ModelDb.Card<DefendIronclad>().PortraitPath;
    public override string BetaPortraitPath => ModelDb.Card<DefendIronclad>().PortraitPath;

    public Defend() : base(1, CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
        WithBlock(5, 3);
        WithTags(CardTag.Defend);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await CommonActions.CardBlock(this, play);
    }
}
