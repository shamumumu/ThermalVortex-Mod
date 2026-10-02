using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class ElectromagneticCircle : PoleMonsterCard
{
    private const int BaseCost = 3;
    private const int BaseDamage = 6;
    private const int BaseBlock = 6;
    private const int BaseMaxHp = 5;

    public override int MaxUpgradeLevel => 2;
    public override string CustomPortraitPath => "electromagnetic_circle.png".BigCardImagePath();
    public override string PortraitPath => "electromagnetic_circle.png".CardImagePath();
    public override string BetaPortraitPath => "electromagnetic_circle.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;
    internal override bool IsFirePole => true;
    internal override bool IsThunderPole => true;
    protected override bool SummonsFirePole => true;

    public ElectromagneticCircle() : base(BaseCost, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
        WithDamage(BaseDamage, 0);
        WithBlock(BaseBlock, 0);
        WithCostUpgradeBy(-1);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCombatVfx.CardAttack(this, play, 1).Execute(ctx);
        await CommonActions.CardBlock(this, play);
        await ResolveMonsterSummon(play);
        await TrySummonAnyMainDeckMonster(ctx, play);
    }

    private async Task TrySummonAnyMainDeckMonster(PlayerChoiceContext ctx, CardPlay play)
    {
        var rules = Owner.Creature.GetPower<SummonRulesPower>();
        var candidates = CardPile.GetCards(Owner, PileType.Hand, PileType.Draw)
            .Where(card => card != this)
            .Where(CanSpecialSummonCandidate)
            .Where(card => rules?.CanSummon(card) ?? MonsterFieldService.CanPlaceOnField(card))
            .Where(card => EffectTargeting.CanPlayImmediately(Owner, card))
            .OrderBy(card => card.Pile?.Type == PileType.Hand ? 0 : 1)
            .ToList();

        var cardToSummon = await ChooseOptionalCard(ctx, candidates);
        if (cardToSummon is null)
            return;

        await EffectTargeting.PlayImmediately(ctx, Owner, cardToSummon);
    }

    private static bool CanSpecialSummonCandidate(CardModel card) =>
        card is MonsterCard
        && card is not XyzMonsterCard
        && MonsterFieldService.IsFieldMonster(card);

}
