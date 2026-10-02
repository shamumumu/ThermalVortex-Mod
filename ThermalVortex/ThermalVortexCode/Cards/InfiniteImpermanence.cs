using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class InfiniteImpermanence : MainDeckCard
{
    public override string CustomPortraitPath => "infinite_impermanence.png".BigCardImagePath();
    public override string PortraitPath => "infinite_impermanence.png".CardImagePath();
    public override string BetaPortraitPath => "infinite_impermanence.png".CardImagePath();

    public InfiniteImpermanence() : base(0, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy)
    {
        WithExplanations(KeywordExplanation("THERMALVORTEX-NEGATE"));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (CurrentUpgradeLevel > 0 || IsFirstCardPlayedThisTurn());

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        if (play.Target is not null)
            await ThermalVortexCommandCompat.ApplyPower<InfiniteImpermanencePower>(ctx, play.Target, 1, Owner.Creature, this, false);

        EnergyCost.AddThisCombat(1, false);
    }

    private bool IsFirstCardPlayedThisTurn()
    {
        if (Owner is null)
            return true;

        var tracker = Owner.Creature.GetPower<TurnCardPlayTrackerPower>();
        return tracker is null || tracker.CardsPlayedThisTurn == 0;
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
