using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MonsterSwap : MainDeckCard
{
    public override string CustomPortraitPath => "monster_swap.png".BigCardImagePath();
    public override string PortraitPath => "monster_swap.png".CardImagePath();
    public override string BetaPortraitPath => "monster_swap.png".CardImagePath();

    public MonsterSwap() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || MonsterFieldService.GetMonsters(Owner).Count >= 2);

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var monsters = MonsterFieldService.GetMonsters(Owner).ToList();
        if (monsters.Count < 2)
            return;

        var selected = await CardSelectionHelper.ChooseMany(
            ctx,
            Owner,
            monsters,
            "THERMALVORTEX-MONSTER_SWAP.selectionPrompt",
            2,
            2,
            false);
        if (selected.Count < 2)
            return;

        var left = monsters.IndexOf(selected[0]);
        var right = monsters.IndexOf(selected[1]);
        if (left >= 0 && right >= 0)
            MonsterFieldService.SwapMonstersByCardEffect(Owner, left, right);
    }

}
