using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Patches;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class TrapHole : MainDeckCard
{
    public override string CustomPortraitPath => "trap_hole.png".BigCardImagePath();
    public override string PortraitPath => "trap_hole.png".CardImagePath();
    public override string BetaPortraitPath => "trap_hole.png".CardImagePath();

    public TrapHole() : base(1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
        // Keep the value and the compatible custom Weak hover tip separate.
        // BaseLib's WithPower helper targets an obsolete game API overload.
        WithVar("Weak", 2, 0);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.Remove);
        WithExplanations(KeywordExplanation("THERMALVORTEX-WEAK"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        if (play.Target is null)
            return;

        var action = EnemyActionClassifier.Classify(play.Target.Monster?.NextMove);
        if ((action.Kind & EnemyActionKind.Attack) != 0)
            await ThermalVortexCommandCompat.ApplyPower<WeakPower>(ctx, play.Target, 2, Owner.Creature, this, false);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
