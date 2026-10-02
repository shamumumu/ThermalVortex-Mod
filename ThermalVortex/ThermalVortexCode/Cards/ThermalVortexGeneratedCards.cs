using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
namespace ThermalVortex.ThermalVortexCode.Cards;

internal static class ThermalVortexGeneratedCards
{
    internal static bool IsGeneratedOnly(CardModel card) =>
        CyberSeries.IsGeneratedOnly(card)
        || card is ExodiaLeftArm or ExodiaRightArm or ExodiaLeftLeg or ExodiaRightLeg or ExodiaTorso
        || card is Slag
        || card is WingedDragonOfRaPhoenix or WingedDragonOfRa;

    internal static bool IsRewardExcluded(CardModel card) =>
        IsGeneratedOnly(card)
        || card is ElectromagneticCircle or PrimalGodFara;

    internal static bool UsesGeneratedColorlessVisual(CardModel card) =>
        CyberSeries.IsGeneratedOnly(card)
        || card is ExodiaLeftArm or ExodiaRightArm or ExodiaLeftLeg or ExodiaRightLeg or ExodiaTorso
        || card is WingedDragonOfRaPhoenix or WingedDragonOfRa;

    internal static T CreateGeneratedCard<T>(CardModel source)
        where T : CardModel
    {
        try
        {
            var generated = source?.Owner?.Creature?.CombatState?.CreateCard<T>(source.Owner);
            return MatchUpgrade(generated, source);
        }
        catch
        {
            return null;
        }
    }

    internal static T CreateGeneratedCard<T>(MegaCrit.Sts2.Core.Entities.Players.Player owner)
        where T : CardModel
    {
        try
        {
            return owner?.Creature?.CombatState?.CreateCard<T>(owner);
        }
        catch
        {
            return null;
        }
    }

    internal static T MatchUpgrade<T>(T generated, AbstractModel source)
        where T : CardModel
    {
        var upgradeLevel = source is CardModel sourceCard
            ? sourceCard.CurrentUpgradeLevel
            : 0;
        return ApplyUpgradeLevel(generated, upgradeLevel);
    }

    internal static T ApplyUpgradeLevel<T>(T card, int upgradeLevel)
        where T : CardModel
    {
        if (card is null)
            return null;

        while (card.CurrentUpgradeLevel < upgradeLevel && card.IsUpgradable)
        {
            card.UpgradeInternal();
            card.FinalizeUpgradeInternal();
        }

        return card;
    }

}
