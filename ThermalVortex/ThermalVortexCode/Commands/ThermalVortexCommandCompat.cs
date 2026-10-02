using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Commands;

internal static class ThermalVortexCommandCompat
{
    internal static Task<decimal> GainBlock(
        Creature target,
        decimal amount,
        ValueProp props,
        CardPlay play,
        bool playVfx) =>
        GainBlock(target, amount, props, play, playVfx, isCardEffect: false);

    internal static Task<decimal> GainBlockFromCardEffect(
        Creature target,
        decimal amount,
        ValueProp props,
        CardPlay play,
        bool playVfx) =>
        GainBlock(target, amount, props, play, playVfx, isCardEffect: true);

    private static Task<decimal> GainBlock(
        Creature target,
        decimal amount,
        ValueProp props,
        CardPlay play,
        bool playVfx,
        bool isCardEffect)
    {
        // Fixed card effects must honor native card-only No Block even when
        // Unpowered or detached from their original CardPlay. Brilliant's
        // stronger turn lock applies to every source through the command patch.
        var blocked = amount > 0
            && (BrilliantRebootBlockLock.IsActive(target)
                || (isCardEffect && target?.HasPower<NoBlockPower>() == true));
        return blocked
            ? Task.FromResult(0m)
            : CreatureCmd.GainBlock(target, amount, props, play, playVfx);
    }

    internal static Task<T> ApplyPower<T>(
        PlayerChoiceContext ctx,
        Creature target,
        decimal amount,
        Creature applier,
        CardModel source,
        bool silent)
        where T : PowerModel
    {
        return PowerCmd.Apply<T>(ctx, target, amount, applier, source, silent);
    }

    internal static Task<IReadOnlyList<T>> ApplyPower<T>(
        PlayerChoiceContext ctx,
        IEnumerable<Creature> targets,
        decimal amount,
        Creature applier,
        CardModel source,
        bool silent)
        where T : PowerModel
    {
        return PowerCmd.Apply<T>(ctx, targets, amount, applier, source, silent);
    }

    internal static Task<int> ModifyPowerAmount(
        PlayerChoiceContext ctx,
        PowerModel power,
        decimal amount,
        Creature applier,
        CardModel source,
        bool silent)
    {
        return PowerCmd.ModifyAmount(ctx, power, amount, applier, source, silent);
    }

    internal static Task<CardPileAddResult> AddGeneratedCardToCombat(
        CardModel card,
        PileType pile,
        Player owner,
        CardPilePosition position)
    {
        return CardPileCmd.AddGeneratedCardToCombat(card, pile, owner, position);
    }
}
