using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class DarkMagician : MonsterCard
{
    public const int BaseMaxHp = 8;

    public override string CustomPortraitPath => "dark_magician.png".BigCardImagePath();
    public override string PortraitPath => "dark_magician.png".CardImagePath();
    public override string BetaPortraitPath => "dark_magician.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public DarkMagician() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithBlock(6, 4);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        TcgMonsterCutinVfx.StartPreload(this);
        await ResolveMonsterSummon(play);
        await TcgMonsterCutinVfx.PlaySummonAsync(this, play);
        await CommonActions.CardBlock(this, play);
    }
}
