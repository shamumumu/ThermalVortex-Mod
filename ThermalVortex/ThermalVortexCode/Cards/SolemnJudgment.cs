using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class SolemnJudgment : SolemnCard
{
    public override string CustomPortraitPath => "solemn_judgment.png".BigCardImagePath();
    public override string PortraitPath => "solemn_judgment.png".CardImagePath();
    public override string BetaPortraitPath => "solemn_judgment.png".CardImagePath();

    public SolemnJudgment() : base(0, CardRarity.Rare, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithKeyword(CardKeyword.Retain, UpgradeType.Add);
        WithExplanations(KeywordExplanation("THERMALVORTEX-NEGATE"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        if (!await PayLife(GetLifePayment()))
            return;

        var enemies = Owner.Creature.CombatState.Enemies.ToList();
        if (enemies.Count > 0)
            await ThermalVortexCommandCompat.ApplyPower<SolemnJudgmentPower>(ctx, enemies, 1, Owner.Creature, this, false);
    }

    protected override decimal GetLifePayment() =>
        CalculateLifePayment(Owner.Creature.CurrentHp);

    internal static int CalculateLifePayment(decimal currentHp) =>
        (int)Math.Max(0, Math.Floor(currentHp / 2m));
}
