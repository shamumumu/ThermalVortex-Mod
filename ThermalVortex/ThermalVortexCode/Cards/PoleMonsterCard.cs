using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Cards;

public abstract class PoleMonsterCard(int cost, CardType type, CardRarity rarity, TargetType target) :
    MonsterCard(cost, type, rarity, target)
{
    protected abstract bool SummonsFirePole { get; }
    internal virtual bool IsFirePole => false;
    internal virtual bool IsThunderPole => false;

    protected async Task ResolveSummon(
        PlayerChoiceContext ctx,
        CardPlay play)
    {
        await ResolveSummonAndGetLinkedMonster(ctx, play);
    }

    private protected async Task<CardModel> ResolveSummonAndGetLinkedMonster(
        PlayerChoiceContext ctx,
        CardPlay play)
    {
        await ResolveMonsterSummon(play);
        return await ResolveLinkedSummon(ctx);
    }

    protected async Task<CardModel> ResolveLinkedSummon(PlayerChoiceContext ctx)
    {
        var cardToSummon = await ChooseLinkedSummonCandidate(ctx);
        return cardToSummon is null
            ? null
            : await ExecuteLinkedSummonCandidate(ctx, cardToSummon);
    }

    protected async Task<PoleMonsterCard> ChooseLinkedSummonCandidate(PlayerChoiceContext ctx)
    {
        var rules = Owner.Creature.GetPower<SummonRulesPower>();
        if (rules is null)
            return null;

        var candidates = CardPile.GetCards(Owner, PileType.Hand, PileType.Draw)
            .OfType<PoleMonsterCard>()
            .Where(card => card != this)
            .Where(CanSummonLinkedCandidate)
            .Where(rules.CanSummon)
            .Where(card => EffectTargeting.CanPlayImmediately(Owner, card))
            .OrderByDescending(card => card is ExternalCombustion or Electrode)
            .ThenBy(card => card.Pile?.Type == PileType.Hand ? 0 : 1)
            .ToList();

        return await ChooseOptionalCard(ctx, candidates);
    }

    protected async Task<CardModel> ExecuteLinkedSummonCandidate(
        PlayerChoiceContext ctx,
        PoleMonsterCard cardToSummon)
    {
        // The linked card is a new play and gets its own mandatory target. The
        // outer card's target must not silently become the linked attack target.
        await EffectTargeting.PlayImmediately(ctx, Owner, cardToSummon);

        return MonsterFieldService.IsOnField(cardToSummon) ? cardToSummon : null;
    }

    protected virtual bool CanSummonLinkedCandidate(PoleMonsterCard card) =>
        SummonsFirePole ? card.IsFirePole : card.IsThunderPole;

    protected async Task<T> ChooseOptionalCard<T>(PlayerChoiceContext ctx, IReadOnlyList<T> candidates)
        where T : CardModel
    {
        if (candidates.Count == 0)
            return null;

        return await CardSelectionHelper.ChooseOptionalOne(
            ctx,
            Owner,
            candidates,
            "THERMALVORTEX-LINKED_SUMMON.selectionPrompt") as T;
    }

}

public abstract class FirePoleCard(int cost, CardType type, CardRarity rarity, TargetType target) :
    PoleMonsterCard(cost, type, rarity, target)
{
    internal override bool IsFirePole => true;
    protected override bool SummonsFirePole => false;
}

public abstract class ThunderPoleCard(int cost, CardType type, CardRarity rarity, TargetType target) :
    PoleMonsterCard(cost, type, rarity, target)
{
    internal override bool IsThunderPole => true;
    protected override bool SummonsFirePole => true;
}
