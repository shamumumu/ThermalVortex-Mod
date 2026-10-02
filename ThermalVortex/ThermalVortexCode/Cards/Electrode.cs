using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class Electrode : ThunderPoleCard
{
    public const int BaseMaxHp = 2;

    public override string CustomPortraitPath => "electrode.png".BigCardImagePath();
    public override string PortraitPath => "electrode.png".CardImagePath();
    public override string BetaPortraitPath => "electrode.png".CardImagePath();
    public override int MonsterMaxHp => CurrentUpgradeLevel > 0 ? 5 : BaseMaxHp;

    public Electrode() : base(2, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithBlock(2, 3);
        WithMonsterHpUpgrade(BaseMaxHp, 5);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await CommonActions.CardBlock(this, play);
        await ResolveMonsterSummon(play);

        var powerCards = CardPile.GetCards(Owner, PileType.Hand, PileType.Draw)
            .Where(card => card.Type == CardType.Power)
            .Where(card => EffectTargeting.CanPlayImmediately(Owner, card))
            .ToList();

        var powerToPlay = powerCards.Count > 0
            ? await ChoosePowerCard(ctx, powerCards)
            : null;

        if (powerToPlay is not null)
            await EffectTargeting.PlayImmediately(ctx, Owner, powerToPlay);

        var linkedMonster = await ResolveLinkedSummon(ctx);
        if (linkedMonster is not null
            && Owner.Creature.GetPower<FurnaceStartupPower>() is { } furnaceStartup)
        {
            await furnaceStartup.TryTriggerForSummon(ctx, linkedMonster);
        }
    }

    private async Task<CardModel> ChoosePowerCard(PlayerChoiceContext ctx, IReadOnlyList<CardModel> powerCards)
    {
        return await CardSelectionHelper.ChooseOptionalOne(
            ctx,
            Owner,
            powerCards,
            "THERMALVORTEX-POWER_SEARCH.selectionPrompt");
    }
}
