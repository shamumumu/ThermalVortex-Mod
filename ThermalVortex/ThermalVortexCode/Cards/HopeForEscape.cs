using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class HopeForEscape : MainDeckCard
{
    public override string CustomPortraitPath => "hope_for_escape.png".BigCardImagePath();
    public override string PortraitPath => "hope_for_escape.png".CardImagePath();
    public override string BetaPortraitPath => "hope_for_escape.png".CardImagePath();

    public HopeForEscape() : base(2, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var creature = Owner?.Creature;
        if (creature is null)
            return;

        var maxHp = Math.Max(1, creature.MaxHp);
        var draw = CalculateDrawCount(creature.CurrentHp, maxHp);
        if (draw > 0)
            await CardPileCmd.Draw(ctx, draw, Owner, false);

        var lifeLoss = CalculateLifeLoss(maxHp);
        if (lifeLoss > 0)
            await CreatureCmd.SetCurrentHp(creature, Math.Max(0, creature.CurrentHp - lifeLoss));
    }

    internal static int CalculateDrawCount(decimal currentHp, decimal maxHp) =>
        (int)Math.Floor(Math.Max(0, maxHp - currentHp) * 10m / Math.Max(1, maxHp));

    internal static int CalculateLifeLoss(decimal maxHp) =>
        (int)Math.Max(0, Math.Floor(maxHp / 10m));

    protected override void AddExtraArgsToDescription(LocString locString)
    {
        base.AddExtraArgsToDescription(locString);
        locString.Add("HpPercent", 10);
    }

}
