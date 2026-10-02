using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Commands;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MillenniumCross : MainDeckCard, IMillenniumCard
{
    public const int RequiredCount = 5;
    public override string CustomPortraitPath => "millennium_cross.png".BigCardImagePath();
    public override string PortraitPath => "millennium_cross.png".CardImagePath();
    public override string BetaPortraitPath => "millennium_cross.png".CardImagePath();

    public MillenniumCross() : base(1, CardType.Skill, CardRarity.Rare, TargetType.AllEnemies)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.Remove);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_CARD"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_POWER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_COUNT"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_CROSS_RULE"),
            CardPreviewExplanation<PhantomSummoningGodExodia>());
    }

    protected override bool IsPlayable =>
        base.IsPlayable
        && (TryGetOwner() is null || CanSummonExodia());

    internal bool CanSummonExodia()
    {
        var owner = TryGetOwner();
        return owner is not null
            && MillenniumResolution.GetCount(this) >= RequiredCount
            && MonsterFieldService.CanPlaceOnFieldAfterRemoving(owner, []);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        // Auto-play and queued effects may reach resolution after the field
        // changes. Recheck before creating a card or resolving its damage.
        if (!CanSummonExodia())
            return;

        var count = MillenniumResolution.GetCount(this);
        var god = ThermalVortexGeneratedCards.CreateGeneratedCard<PhantomSummoningGodExodia>(this);
        if (god is null)
            return;

        god.MillenniumCrossCount = count;
        await ThermalVortexCommandCompat.AddGeneratedCardToCombat(god, PileType.Hand, Owner, CardPilePosition.Top);
        var played = await EffectTargeting.PlayImmediately(ctx, Owner, god);
        if (!played)
            await EffectTargeting.RemoveUncommittedTransientCard(god);
    }

    protected override void AddExtraArgsToDescription(MegaCrit.Sts2.Core.Localization.LocString locString)
    {
        base.AddExtraArgsToDescription(locString);
        locString.Add("RequiredCount", RequiredCount);
        var owner = TryGetOwner();
        locString.Add("Count", owner is null ? RequiredCount : MillenniumResolution.GetCount(this));
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class PhantomSummoningGodExodia : MonsterCard
{
    public const int BaseMaxHp = 1;
    public override string CustomPortraitPath => "phantom_summoning_god_exodia.png".BigCardImagePath();
    public override string PortraitPath => "phantom_summoning_god_exodia.png".CardImagePath();
    public override string BetaPortraitPath => "phantom_summoning_god_exodia.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;
    public int? MillenniumCrossCount { get; set; }
    private bool MillenniumCrossDamageResolved { get; set; }

    public PhantomSummoningGodExodia() : base(0, CardType.Skill, CardRarity.Token, TargetType.AllEnemies)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithFormulaDamage(
            1,
            1,
            (card, _) => ((PhantomSummoningGodExodia)card).MillenniumCrossCount ?? 0,
            ValueProp.Unpowered);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_COUNT"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_CROSS_RULE"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        TcgMonsterCutinVfx.StartPreload(this);
        await ResolveMonsterSummon(play);
        var host = ChaosPhantomCopyService.ResolveEffectHost(this);
        if (!MonsterFieldService.CanPlaceOnField(host))
        {
            MonsterFieldService.UnmarkPending(host);
            return;
        }
        await TcgMonsterCutinVfx.PlaySummonAsync(this, play);
        await ResolveMillenniumCrossDamage(ctx);
    }

    internal void ResetExecutionForCompleteCopy() => MillenniumCrossDamageResolved = false;

    internal async Task ResolveMillenniumCrossDamage(PlayerChoiceContext ctx)
    {
        if (MillenniumCrossDamageResolved)
            return;

        var count = MillenniumCrossCount ?? 0;
        var owner = TryGetOwner();
        if (count <= 0 || owner?.Creature?.CombatState is null)
            return;

        MillenniumCrossDamageResolved = true;
        await ThermalVortexCombatVfx.CardAttackAllEnemies(this, FormulaDamage, count).Execute(ctx);
    }

    protected override void AddExtraArgsToDescription(MegaCrit.Sts2.Core.Localization.LocString locString)
    {
        base.AddExtraArgsToDescription(locString);
        locString.Add("Count", MillenniumCrossCount?.ToString() ?? "千年计数");
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
