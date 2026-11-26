using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Relics;

public class AncientThermalVortexCore : ThermalVortexCore
{
    public override RelicRarity Rarity => RelicRarity.Ancient;
    protected override string RelicIconFileName => "millennium_puzzle.png";
    protected override string RelicIconOutlineFileName => "millennium_puzzle_outline.png";
    internal override bool HandlesFusionInOpeningDraw => false;

    public override CardLocation ModifyCardPlayResultLocation(
        CardModel card,
        bool isAutoPlay,
        ResourceInfo resources,
        CardLocation location)
    {
        if (card is XyzSummon && ReferenceEquals(card.Owner, Owner)
            && location.pileType == PileType.Discard && ReferenceEquals(location.player, Owner))
            return new CardLocation(Owner, PileType.Hand, CardPilePosition.Bottom);

        return location;
    }

    public override RelicModel GetUpgradeReplacement() => null;
}
