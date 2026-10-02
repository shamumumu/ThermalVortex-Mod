using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class GrenMajuDaEiza : MonsterCard
{
    public const int BaseMaxHp = 6;
    private const int DamagePerExhaustedCard = 2;
    private const int UpgradedDamagePerExhaustedCard = 3;
    public override string CustomPortraitPath => "gren_maju_da_eiza.png".BigCardImagePath();
    public override string PortraitPath => "gren_maju_da_eiza.png".CardImagePath();
    public override string BetaPortraitPath => "gren_maju_da_eiza.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public GrenMajuDaEiza() : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        WithFormulaDamage(
            DamagePerExhaustedCard,
            UpgradedDamagePerExhaustedCard,
            (card, _) => ((GrenMajuDaEiza)card).GetExhaustedCardCount());
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);

        var hitCount = GetExhaustedCardCount();
        if (play.Target is not null && hitCount > 0)
        {
            await ThermalVortexCombatVfx.CardAttack(
                    this,
                    play.Target,
                    FormulaDamageUnit,
                    hitCount)
                .Execute(ctx);
        }
    }

    protected override void OnUpgrade() => ConstructedUpgrade();

    internal int GetExhaustedCardCount()
    {
        var owner = TryGetOwner();
        return owner?.PlayerCombatState is null ? 0 : CardPile.GetCards(owner, PileType.Exhaust).Count();
    }
}
