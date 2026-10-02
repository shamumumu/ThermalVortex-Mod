using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class ExternalCombustion : FirePoleCard
{
    public const int BaseMaxHp = 2;

    public override string CustomPortraitPath => "external_combustion.png".BigCardImagePath();
    public override string PortraitPath => "external_combustion.png".CardImagePath();
    public override string BetaPortraitPath => "external_combustion.png".CardImagePath();
    public override int MonsterMaxHp => CurrentUpgradeLevel > 0 ? 5 : BaseMaxHp;

    public ExternalCombustion() : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        WithDamage(2, 3);
        WithMonsterHpUpgrade(BaseMaxHp, 5);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            ExtraDeckExplanation(),
            NativeKeywordExplanation(CardKeyword.Exhaust));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCombatVfx.CardAttack(this, play, 1).Execute(ctx);
        await ResolveMonsterSummon(play);

        var core = Owner.GetRelic<ThermalVortexCore>();
        if (core?.HasExtraDeckCard != true)
            return;

        while (true)
        {
            var linkedCandidates = GetLinkedSummonCandidates();
            if (linkedCandidates.Count == 0)
                return;

            var linkedChoice = await CardSelectionHelper.ChooseManyWithBack(
                ctx,
                Owner,
                linkedCandidates,
                "THERMALVORTEX-LINKED_SUMMON.selectionPrompt",
                0,
                1,
                allowBack: false,
                automatedSelectionCount: 1);
            var linkedMonster = linkedChoice.FirstOrDefault;
            if (!linkedChoice.Confirmed || linkedMonster is null)
                return;

            var extraDeckChoice = await CardSelectionHelper.ChooseOneWithBack(
                ctx,
                Owner,
                core.GetExtraDeckConsumeChoices(),
                "THERMALVORTEX-EXTRA_DECK_CONSUME.selectionPrompt",
                allowBack: linkedChoice.WasPresented);
            if (extraDeckChoice.WentBack)
            {
                await Task.Yield();
                continue;
            }

            var extraDeckMonster = extraDeckChoice.FirstOrDefault;
            if (!extraDeckChoice.Confirmed || extraDeckMonster is null)
                return;

            bool CanCommitLinkedSummon() =>
                CardPile.GetCards(Owner, PileType.Hand, PileType.Draw)
                    .Any(card => ReferenceEquals(card, linkedMonster))
                && EffectTargeting.CanPlayImmediately(Owner, linkedMonster);

            // Both choices may have yielded. Commit the Extra Deck cost only
            // after both selected cards have been revalidated.
            if (!CanCommitLinkedSummon()
                || !await core.ConsumeSelectedExtraDeckCard(
                    ctx,
                    extraDeckMonster,
                    CanCommitLinkedSummon))
            {
                return;
            }

            await ExecuteLinkedSummonCandidate(ctx, linkedMonster);
            return;
        }
    }

    private IReadOnlyList<PoleMonsterCard> GetLinkedSummonCandidates()
    {
        var rules = Owner.Creature.GetPower<SummonRulesPower>();
        if (rules is null)
            return [];

        return CardPile.GetCards(Owner, PileType.Hand, PileType.Draw)
            .OfType<PoleMonsterCard>()
            .Where(card => card != this)
            .Where(CanSummonLinkedCandidate)
            .Where(rules.CanSummon)
            .Where(card => EffectTargeting.CanPlayImmediately(Owner, card))
            .OrderByDescending(card => card is ExternalCombustion or Electrode)
            .ThenBy(card => card.Pile?.Type == PileType.Hand ? 0 : 1)
            .ToList();
    }
}
