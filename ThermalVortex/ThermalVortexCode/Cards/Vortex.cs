using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class Vortex : XyzMonsterCard
{
    public const int RequiredMaterials = 2;
    public const int BaseMaxHp = 8;
    public override string CustomPortraitPath => "vortex.png".BigCardImagePath();
    public override string PortraitPath => "vortex.png".CardImagePath();
    public override string BetaPortraitPath => "vortex.png".CardImagePath();
    public override int MinimumMaterials => RequiredMaterials;
    public override int MaximumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => BaseMaxHp;

    public Vortex() : base(CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithUpgradeVar("DrawMonsters", 1, 2);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var drawPileMonsters = CardPile.GetCards(Owner, PileType.Draw)
            .Where(ThermalVortex.ThermalVortexCode.MonsterField.MonsterFieldService.IsFieldMonster)
            .ToList();
        var count = Math.Min(UpgradeVarValue("DrawMonsters"), drawPileMonsters.Count);
        var chosen = await CardSelectionHelper.ChooseMany(
            ctx,
            Owner,
            drawPileMonsters,
            "THERMALVORTEX-VORTEX.selectionPrompt",
            count,
            count,
            false);

        foreach (var card in chosen)
            await MegaCrit.Sts2.Core.Commands.CardPileCmd.Add(card, PileType.Hand, CardPilePosition.Top, this, false);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
