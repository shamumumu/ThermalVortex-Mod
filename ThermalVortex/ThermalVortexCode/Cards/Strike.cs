using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class Strike : MainDeckCard
{
    public override string CustomPortraitPath => ModelDb.Card<StrikeIronclad>().PortraitPath;
    public override string PortraitPath => ModelDb.Card<StrikeIronclad>().PortraitPath;
    public override string BetaPortraitPath => ModelDb.Card<StrikeIronclad>().PortraitPath;

    public Strike() : base(1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
    {
        WithDamage(6, 3);
        WithTags(CardTag.Strike);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCombatVfx.CardAttack(this, play, 1).Execute(ctx);
    }
}
