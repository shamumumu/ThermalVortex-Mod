using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class UpstartGoblin : MainDeckCard
{
    public override string CustomPortraitPath => "upstart_goblin.png".BigCardImagePath();
    public override string PortraitPath => "upstart_goblin.png".CardImagePath();
    public override string BetaPortraitPath => "upstart_goblin.png".CardImagePath();

    public UpstartGoblin() : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithCards(2, 1);
        WithUpgradeVar("Heal", 20, 30);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var draw = DynamicVars.Cards.IntValue;
        var heal = UpgradeVarValue("Heal");
        await CardPileCmd.Draw(ctx, draw, Owner, false);
        foreach (var enemy in Owner.Creature.CombatState.HittableEnemies.ToList())
            await CreatureCmd.Heal(enemy, heal, false);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
