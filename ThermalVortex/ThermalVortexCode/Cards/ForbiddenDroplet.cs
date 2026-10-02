using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Random;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class ForbiddenDroplet : MainDeckCard
{
    public override string CustomPortraitPath => "forbidden_droplet.png".BigCardImagePath();
    public override string PortraitPath => "forbidden_droplet.png".CardImagePath();
    public override string BetaPortraitPath => "forbidden_droplet.png".CardImagePath();

    public ForbiddenDroplet() : base(2, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        // BaseLib's WithPower helper still calls the removed parameterless
        // HoverTipFactory.FromPower overload in the current game build.
        // This card only needs the dynamic value; applying Strength remains
        // handled explicitly in OnPlay below.
        WithVar("StrengthLoss", 2, 1);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(PowerExplanation<StrengthPower>());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var candidates = CardPile.GetCards(Owner, PileType.Hand)
            .Where(card => card != this)
            .ToList();
        if (candidates.Count == 0)
            return;

        var selected = await ChooseCardsToExhaust(ctx, candidates);
        var strengthLoss = CurrentUpgradeLevel > 0 ? 3 : 2;
        foreach (var card in selected)
        {
            await CardCmd.Exhaust(ctx, card, false, false);

            var enemy = RandomEnemy();
            if (enemy is not null)
                await ThermalVortexCommandCompat.ApplyPower<StrengthPower>(ctx, enemy, -strengthLoss, Owner.Creature, this, false);
        }
    }

    private Task<IReadOnlyList<CardModel>> ChooseCardsToExhaust(
        PlayerChoiceContext ctx,
        IReadOnlyList<CardModel> candidates) =>
        CardSelectionHelper.ChooseMany(
            ctx,
            Owner,
            candidates,
            "THERMALVORTEX-FORBIDDEN_DROPLET.selectionPrompt",
            0,
            candidates.Count,
            cancelable: true,
            automatedSelectionCount: 1,
            sourceCard: this);

    private MegaCrit.Sts2.Core.Entities.Creatures.Creature RandomEnemy()
    {
        var enemies = Owner.Creature.CombatState.HittableEnemies.ToList();
        if (enemies.Count == 0)
            return null;

        var rng = Owner.RunState?.Rng?.CombatTargets ?? Rng.Chaotic;
        return rng.NextItem(enemies);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
