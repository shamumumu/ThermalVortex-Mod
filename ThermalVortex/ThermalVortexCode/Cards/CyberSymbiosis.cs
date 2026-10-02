using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberSymbiosis : MainDeckCard
{
    public override string CustomPortraitPath => "cyber_symbiosis.png".BigCardImagePath();
    public override string PortraitPath => "cyber_symbiosis.png".CardImagePath();
    public override string BetaPortraitPath => "cyber_symbiosis.png".CardImagePath();

    public CyberSymbiosis() : base(3, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.Remove);
        WithExplanations(
            CardPreviewExplanation<CyberDragon>(),
            CardPreviewExplanation<CyberLarva>());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var repetitions = GeneratedCyberDragonPairs;
        for (var i = 0; i < repetitions; i++)
        {
            await CyberSeries.GenerateCyberDragon(Owner, PileType.Hand, this, CardPilePosition.Top);
        }
        await CyberSeries.GenerateCyberLarvaToDiscard(Owner, this);
    }

    protected override void AddExtraArgsToDescription(LocString locString)
    {
        base.AddExtraArgsToDescription(locString);
        locString.Add("GeneratedPairs", GeneratedCyberDragonPairs);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();

    private int GeneratedCyberDragonPairs
    {
        get
        {
            return CyberSeries.GetCyberLarvaeGeneratedThisCombat(TryGetOwner());
        }
    }
}
