using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class YugiCardActionVfx
{
    private enum CardAction
    {
        None,
        Single,
        Grand,
        ThrowCard
    }

    internal static async Task BeforeCardPlayed(Creature actor, CardPlay play)
    {
        var card = play?.Card;
        // Reborn chooses a discard-pile target before starting its gesture.
        if (card is MonsterReborn)
            return;

        var action = ResolveAction(actor, card, play);
        if (action == CardAction.ThrowCard)
        {
            await ThermalVortexCombatVfx.PlaySafeAsync(
                nameof(MonsterPlayActionVfx),
                () => MonsterPlayActionVfx.PlayAsync(actor));
            return;
        }

        await PlayCast(card, play, action);
    }

    internal static Task<bool> PlaySelectedCast(CardModel card, CardPlay play) =>
        PlayCast(card, play, ResolveAction(card?.Owner?.Creature, card, play));

    private static CardAction ResolveAction(Creature actor, CardModel card, CardPlay play)
    {
        if (actor is null
            || card is not ThermalVortexCard
            || !ReferenceEquals(card.Owner?.Creature, actor)
            || play is null
            || !ReferenceEquals(play.Card, card)
            || !play.IsFirstInSeries
            || play.IsAutoPlay
            || card is ExtraDeckCard)
        {
            return CardAction.None;
        }

        // Extra summons use their own presentation. Normal played cards may
        // already be in Play when this hook runs, so classify by the play and
        // card type rather than the current pile.
        return card switch
        {
            FurnaceStartup or GoldSarcophagus or HopeForEscape
                or InfiniteImpermanence or MagicalHats or MonsterReborn
                or PotOfDesires or PotOfExtravagance or PotOfGreed
                or SolemnStrike or SolemnWarning or AliceInWonderland
                or CyberSymbiosis or FusionGate or LimiterRemoval
                or MacroCosmos or MillenniumCross or MirrorForce
                or SolemnJudgment => CardAction.Grand,
            MonsterCard => CardAction.Single,
            Strike => CardAction.ThrowCard,
            _ => CardAction.None
        };
    }

    private static Task<bool> PlayCast(CardModel card, CardPlay play, CardAction action)
    {
        var sequence = action switch
        {
            CardAction.Single => "single",
            CardAction.Grand => "grand",
            _ => null
        };
        if (sequence is null)
            return Task.FromResult(false);

        try
        {
            // Strike unlocks its effect before its throw recovery ends. Release
            // that sprite lease so a quick following cast is not skipped.
            var sprite = card.Owner?.Creature?.GetCreatureNode()?.Visuals?
                .FindChild("YugiSprite", true, false) as Sprite2D;
            if (sprite is not null)
                MonsterPlayActionVfx.Interrupt(sprite);
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Yugi throw handoff unavailable: " + exception.Message);
        }

        return YugiNativeActionController.PlayAsync(card, play, sequence);
    }
}
