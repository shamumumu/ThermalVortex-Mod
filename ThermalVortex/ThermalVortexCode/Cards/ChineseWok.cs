using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class ChineseWok : MainDeckCard
{
    public override string CustomPortraitPath => "chinese_wok.png".BigCardImagePath();
    public override string PortraitPath => "chinese_wok.png".CardImagePath();
    public override string BetaPortraitPath => "chinese_wok.png".CardImagePath();

    public ChineseWok() : base(3, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MATERIAL"),
            KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || MonsterFieldService.GetMonsters(Owner).Any());

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var owner = Owner;
        var isCombatValid = MonsterFieldService.CaptureMaterialUseValidity(owner);
        if (!isCombatValid())
            return;

        var material = await CardSelectionHelper.ChooseOne(
            ctx,
            owner,
            MonsterFieldService.GetMonsters(owner).ToList(),
            "THERMALVORTEX-MATERIAL_MONSTER.selectionPrompt",
            false,
            sourceCard: this);
        if (!isCombatValid())
            return;

        var fieldPile = MonsterFieldService.GetPile(owner);
        if (material is null
            || fieldPile is null
            || !ReferenceEquals(material.Owner, owner)
            || !ReferenceEquals(material.Pile, fieldPile)
            || !fieldPile.Cards.Any(card => ReferenceEquals(card, material)))
            return;

        var health = MonsterFieldHealthService.PeekHealth(material);
        if (await MonsterFieldService.TryUseFieldMaterialToDiscard(ctx, owner, material, this)
            && health.CurrentHp > 0)
            await CreatureCmd.Heal(owner.Creature, health.CurrentHp, false);
    }

}
