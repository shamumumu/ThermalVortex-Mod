using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class DarkCyberWorld : MainDeckCard
{
    public override string CustomPortraitPath => "dark_cyber_world.png".BigCardImagePath();
    public override string PortraitPath => "dark_cyber_world.png".CardImagePath();
    public override string BetaPortraitPath => "dark_cyber_world.png".CardImagePath();

    public DarkCyberWorld() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-CYBER_MONSTER"),
            KeywordExplanation("THERMALVORTEX-DEVOUR"),
            KeywordExplanation("THERMALVORTEX-SANITY", card => card.CurrentUpgradeLevel > 0),
            CardPreviewExplanation<CyberLarva>(),
            NativeKeywordExplanation(CardKeyword.Exhaust, card => !card.IsUpgraded));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCommandCompat.ApplyPower<DarkCyberWorldPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
        if (CurrentUpgradeLevel > 0)
            Owner.Creature.GetPower<DarkCyberWorldPower>()?.EnableSanity();

        await CyberSeries.GenerateCyberLarvaToDiscard(Owner, this);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
