using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MillenniumOffering : MainDeckCard, IMillenniumCard
{
    public const int RequiredMaterials = 2;
    public override string CustomPortraitPath => "millennium_offering.png".BigCardImagePath();
    public override string PortraitPath => "millennium_offering.png".CardImagePath();
    public override string BetaPortraitPath => "millennium_offering.png".CardImagePath();

    public MillenniumOffering() : base(2, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MATERIAL"),
            KeywordExplanation("THERMALVORTEX-SEALED_MILLENNIUM_PIECE"),
            KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || MonsterFieldService.GetMonsters(Owner).Count >= RequiredMaterials);

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var owner = Owner;
        var isCombatValid = MonsterFieldService.CaptureMaterialUseValidity(owner);
        if (!isCombatValid())
            return;

        var materials = await CardSelectionHelper.ChooseMany(
            ctx,
            owner,
            MonsterFieldService.GetMonsters(owner),
            "THERMALVORTEX-MATERIAL_MONSTER.selectionPrompt",
            RequiredMaterials,
            RequiredMaterials,
            false,
            sourceCard: this);
        if (!isCombatValid() || materials.Count < RequiredMaterials)
            return;

        foreach (var material in materials)
        {
            if (!await MonsterFieldService.TryUseFieldMaterialToDiscard(ctx, owner, material, this))
                return;
        }

        await MillenniumSeries.AddChosenSealedPieceToHand(ctx, owner, this);
    }

}
