using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Commands;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class ExodiaSummonerPower : ThermalVortexPower, IMillenniumPower, IMonsterFieldLeaveResolvedListener
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    // Generate before the normal turn-start draw, so the new monster can be drawn this turn.
    public override async Task AfterPlayerTurnStartEarly(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner)
            return;

        var summoners = MonsterFieldService.GetMonsters(player).OfType<ExodiaSummoner>().ToArray();
        if (summoners.Length == 0)
        {
            await PowerCmd.Remove(this);
            return;
        }

        var isValid = MonsterFieldService.CaptureMaterialUseValidity(player);
        foreach (var summoner in summoners)
        {
            if (!isValid())
                return;
            if (!MonsterFieldService.IsOnField(summoner))
                continue;

            var card = MillenniumExtraDeckGeneration.CreateRandomMainRewardMillenniumMonster(player);
            if (card is null)
                continue;

            if (summoner.CurrentUpgradeLevel > 0 && card.EnergyCost.GetResolved() > 0)
                card.EnergyCost.AddThisCombat(-1, false);
            Flash();
            await ThermalVortexCommandCompat.AddGeneratedCardToCombat(card, PileType.Draw, player, CardPilePosition.Random);
        }
    }

    public async Task AfterMonsterLeftFieldResolved(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent)
    {
        if (!MonsterFieldService.GetMonsters(Owner.Player).OfType<ExodiaSummoner>().Any()
            && ReferenceEquals(Owner.GetPower<ExodiaSummonerPower>(), this))
            await PowerCmd.Remove(this);
    }
}
