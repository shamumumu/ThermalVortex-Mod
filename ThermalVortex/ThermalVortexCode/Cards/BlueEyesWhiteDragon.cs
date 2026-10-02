using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class BlueEyesWhiteDragon : MonsterCard
{
    public const int BaseMaxHp = 6;

    public override string CustomPortraitPath => "blue_eyes_white_dragon.png".BigCardImagePath();
    public override string PortraitPath => "blue_eyes_white_dragon.png".CardImagePath();
    public override string BetaPortraitPath => "blue_eyes_white_dragon.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public BlueEyesWhiteDragon() : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        WithDamage(8, 6);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        TcgMonsterCutinVfx.StartPreload(this);
        await ResolveMonsterSummon(play);
        await TcgMonsterCutinVfx.PlaySummonAsync(this, play);
        await ThermalVortexCombatVfx.CardAttack(this, play, 1).Execute(ctx);
    }
}
