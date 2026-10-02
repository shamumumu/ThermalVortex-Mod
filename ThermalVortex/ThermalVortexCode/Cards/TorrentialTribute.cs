using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class TorrentialTribute : MainDeckCard
{
    public override string CustomPortraitPath => "torrential_tribute.png".BigCardImagePath();
    public override string PortraitPath => "torrential_tribute.png".CardImagePath();
    public override string BetaPortraitPath => "torrential_tribute.png".CardImagePath();

    public TorrentialTribute() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies)
    {
        WithDamage(12, 4);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCombatVfx.CardAttack(this, play, 1).Execute(ctx);

        await MonsterFieldHealthService.DamageMonsters(
            ctx,
            MonsterFieldService.GetMonsters(Owner),
            CurrentUpgradeLevel > 0 ? 16 : 12,
            this,
            MonsterFieldLeaveReason.BattleDestroyed);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
