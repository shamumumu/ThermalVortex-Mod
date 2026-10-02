using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberDragonCore : MainDeckCard
{
    public override string CustomPortraitPath => "cyber_dragon_core.png".BigCardImagePath();
    public override string PortraitPath => "cyber_dragon_core.png".CardImagePath();
    public override string BetaPortraitPath => "cyber_dragon_core.png".CardImagePath();

    public CyberDragonCore() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MATERIAL"),
            CardPreviewExplanation<CyberDragon>(),
            CardPreviewExplanation<CyberLarva>(),
            KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override bool IsPlayable
    {
        get
        {
            var owner = TryGetOwner();
            return base.IsPlayable && (owner is null || GetValidMaterials(owner).Count > 0);
        }
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var validMaterials = GetValidMaterials(Owner);
        var material = await CardSelectionHelper.ChooseOne(
            ctx,
            Owner,
            validMaterials,
            "THERMALVORTEX-MATERIAL_MONSTER.selectionPrompt",
            false);
        if (material is null
            || !GetValidMaterials(Owner).Any(candidate => ReferenceEquals(candidate, material)))
            return;

        var dragon = CyberSeries.CreateGeneratedCard<CyberDragon>(Owner, this);
        var played = false;
        if (dragon is not null)
        {
            // Preflight against the field after using the selected material.
            using var replacementSlot = MonsterFieldService.PushTemporaryCapacityBonus(Owner, 1);
            try
            {
                played = await EffectTargeting.PlayImmediately(
                    ctx,
                    Owner,
                    dragon,
                    canPrepare: () => MonsterFieldService.IsOnField(material),
                    prepare: async () =>
                    {
                        replacementSlot.Dispose();
                        using (MonsterFieldService.ReserveCapacitySlots(Owner))
                            await MonsterFieldService.UseMaterialsToDiscard(ctx, [material], this);
                        dragon.SetToFreeThisTurn();
                        await ThermalVortexCommandCompat.AddGeneratedCardToCombat(
                            dragon,
                            PileType.Hand,
                            Owner,
                            CardPilePosition.Top);
                        dragon.RefreshSpecialSummonCost();
                    });
            }
            finally
            {
                if (!played)
                    await EffectTargeting.RemoveUncommittedTransientCard(dragon);
            }
        }

        if (!played)
            return;

        await CyberSeries.GenerateCyberLarvaToDiscard(Owner, this);
    }

    private IReadOnlyList<CardModel> GetValidMaterials(Player owner)
    {
        if (owner is null)
            return [];

        var dragon = ModelDb.Card<CyberDragon>()?.ToMutable() as CyberDragon;
        if (dragon is null)
            return [];

        dragon.Owner = owner;
        ThermalVortexGeneratedCards.MatchUpgrade(dragon, this);
        return MonsterFieldService.GetMonsters(owner)
            .Where(material => CanSummonAfterUsingMaterial(owner, dragon, material))
            .ToList();
    }

    private static bool CanSummonAfterUsingMaterial(
        Player owner,
        CardModel dragon,
        CardModel material)
    {
        if (!MonsterFieldService.CanPlaceOnFieldAfterRemoving(owner, [material]))
            return false;

        using var replacementSlot = MonsterFieldService.PushTemporaryCapacityBonus(owner, 1);
        return EffectTargeting.CanPlayImmediately(owner, dragon);
    }

}
