using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Vfx;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class AshBlossom : MonsterCard, IMonsterFieldEnterResolvedListener
{
    public const int BaseMaxHp = 3;
    private Creature _pendingTarget;
    private bool _hasPendingTarget;

    public override string CustomPortraitPath => "ash_blossom.png".BigCardImagePath();
    public override string PortraitPath => "ash_blossom.png".CardImagePath();
    public override string BetaPortraitPath => "ash_blossom.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public AshBlossom() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithKeyword(CardKeyword.Retain, UpgradeType.Add);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-NEGATE"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var self = Owner.Creature;
        // Entry is resolved after OnPlay returns. Replay clones also need this
        // selection when ResolveMonsterSummon places their separate bodies.
        _hasPendingTarget = true;
        _pendingTarget = play.Target ?? await EffectTargeting.ResolveForAutoPlay(ctx, Owner, this);
        await ResolveMonsterSummon(play, showManualSummonVfx: false);
        ThermalVortexCombatVfx.PlaySafe(
            nameof(AshBlossomVfx),
            () => AshBlossomVfx.Play(self));
    }

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        var hadTarget = _hasPendingTarget;
        var target = _pendingTarget;
        _hasPendingTarget = false;
        _pendingTarget = null;

        var host = enterEvent.Card;
        if (!hadTarget)
            target = await EffectTargeting.ChooseEnemy(ctx, host.Owner, sourceCard: host);

        // A committed target that disappeared must never silently retarget.
        if (target?.IsEnemy != true || !target.IsAlive || !target.IsHittable
            || host.Owner?.Creature?.CombatState?.ContainsCreature(target) != true)
            return;

        await ThermalVortexCommandCompat.ApplyPower<AshBlossomPower>(
            ctx, target, 1, host.Owner.Creature, host, false);
    }

    protected override PileType GetResultPileTypeForCardPlay()
    {
        var result = base.GetResultPileTypeForCardPlay();
        if (result != MonsterFieldPile.FieldPileType)
        {
            _hasPendingTarget = false;
            _pendingTarget = null;
        }
        return result;
    }

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel source)
    {
        if (ReferenceEquals(card, this)
            && card.Pile?.Type is PileType.Draw or PileType.Hand or PileType.Discard or PileType.Exhaust)
        {
            _hasPendingTarget = false;
            _pendingTarget = null;
        }
        return Task.CompletedTask;
    }
}
