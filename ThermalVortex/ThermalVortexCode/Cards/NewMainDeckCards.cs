using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class OverheatedCoil : FirePoleCard
{
    public const int BaseMaxHp = 2;
    public override string CustomPortraitPath => "overheated_coil.png".BigCardImagePath();
    public override string PortraitPath => "overheated_coil.png".CardImagePath();
    public override string BetaPortraitPath => "overheated_coil.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;
    internal int DamageAmount => CurrentUpgradeLevel > 0 ? 7 : 5;

    public OverheatedCoil() : base(2, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
        WithDamage(5, 2);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var repeats = MonsterFieldService.HasFieldOrPendingMonster<ThunderPoleCard>(Owner) ? 2 : 1;
        if (play.Target is not null)
            await ThermalVortexCombatVfx.CardAttack(this, play.Target, DamageAmount, repeats).Execute(ctx);

        await ResolveMonsterSummon(play);
    }
}

public class StabilizedMagneticCoil : ThunderPoleCard
{
    public const int BaseMaxHp = 2;
    public override string CustomPortraitPath => "stabilized_magnetic_coil.png".BigCardImagePath();
    public override string PortraitPath => "stabilized_magnetic_coil.png".CardImagePath();
    public override string BetaPortraitPath => "stabilized_magnetic_coil.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;
    internal int BlockPerMonsterAmount => CurrentUpgradeLevel > 0 ? 7 : 5;

    public StabilizedMagneticCoil() : base(2, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithVar("BlockPerMonster", 5, 2);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            BlockExplanation());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var effectHost = ChaosPhantomCopyService.ResolveEffectHost(this);
        var monsterCount = MonsterFieldService.CountFieldOrPendingMonsters(Owner, effectHost);

        await ThermalVortexCommandCompat.GainBlockFromCardEffect(
            Owner.Creature,
            Math.Max(0, BlockPerMonsterAmount * monsterCount),
            ValueProp.Unpowered,
            play,
            false);

        await ResolveMonsterSummon(play);
    }
}

public class DarkDuel : MainDeckCard
{
    public override string CustomPortraitPath => "dark_duel.png".BigCardImagePath();
    public override string PortraitPath => "dark_duel.png".CardImagePath();
    public override string BetaPortraitPath => "dark_duel.png".CardImagePath();

    public DarkDuel() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCommandCompat.ApplyPower<DarkDuelPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
    }
}

public class SpareArmor : MainDeckCard
{
    public const int BaseHealthBonus = 3;
    public const int UpgradedHealthBonus = 5;
    public override string CustomPortraitPath => "spare_armor.png".BigCardImagePath();
    public override string PortraitPath => "spare_armor.png".CardImagePath();
    public override string BetaPortraitPath => "spare_armor.png".CardImagePath();
    private int HealthBonus => UpgradeVarValue("HealthBonus");

    public SpareArmor() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithBlock(6, 3);
        WithUpgradeVar("HealthBonus", BaseHealthBonus, UpgradedHealthBonus);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await CommonActions.CardBlock(this, play);

        var choices = MonsterFieldService.GetMonsters(Owner)
            .Where(card => MonsterFieldHealthService.PeekHealth(card).IsValid)
            .ToList();
        var target = await CardSelectionHelper.ChooseOne(
            ctx,
            Owner,
            choices,
            "THERMALVORTEX-SPARE_ARMOR.selectionPrompt",
            false);
        if (target is not null)
            MonsterFieldHealthService.IncreaseHealth(target, HealthBonus);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class HeroArrival : MainDeckCard
{
    public override string CustomPortraitPath => "hero_arrival.png".BigCardImagePath();
    public override string PortraitPath => "hero_arrival.png".CardImagePath();
    public override string BetaPortraitPath => "hero_arrival.png".CardImagePath();
    private int SearchCount => UpgradeVarValue("SearchCount");

    public HeroArrival() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithUpgradeVar("SearchCount", 3, 5);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var owner = Owner;
        var isCombatValid = MonsterFieldService.CaptureMaterialUseValidity(owner);
        if (!isCombatValid())
            return;

        await CardSelectionHelper.RefillDrawPileFromDiscardIfNeeded(
            ctx,
            owner,
            SearchCount,
            this,
            isCombatValid);
        if (!isCombatValid())
            return;

        var candidates = CardSelectionHelper.DrawPileCards(owner)
            .Take(SearchCount)
            .Where(MonsterFieldService.IsFieldMonster)
            .Where(MonsterFieldService.CanPlaceOnField)
            .Where(card => EffectTargeting.CanPlayImmediately(owner, card))
            .ToList();
        var selected = await CardSelectionHelper.ChooseOne(
            ctx,
            owner,
            candidates,
            "THERMALVORTEX-HERO_ARRIVAL.selectionPrompt",
            false);
        if (selected is null)
            return;

        await EffectTargeting.PlayImmediately(
            ctx,
            owner,
            selected,
            prepare: () =>
            {
                selected.SetToFreeThisTurn();
                return Task.CompletedTask;
            });
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class FusionPreparation : MainDeckCard
{
    public override string CustomPortraitPath => "fusion_preparation.png".BigCardImagePath();
    public override string PortraitPath => "fusion_preparation.png".CardImagePath();
    public override string BetaPortraitPath => "fusion_preparation.png".CardImagePath();
    private int EnergyAmount => CurrentUpgradeLevel > 0 ? 2 : 1;

    public FusionPreparation() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithEnergy(1, 1);
        // This delayed effect always creates the base Fusion card, even when
        // Fusion Preparation itself is upgraded.
        WithExplanations(CardPreviewExplanation<XyzSummon>(matchSourceUpgrade: false));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCommandCompat.ApplyPower<FusionPreparationPower>(ctx, Owner.Creature, EnergyAmount, Owner.Creature, this, false);
        Owner.Creature.GetPower<FusionPreparationPower>()?.Bind(EnergyAmount);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class OverloadDraw : MainDeckCard
{
    public override string CustomPortraitPath => "overload_draw.png".BigCardImagePath();
    public override string PortraitPath => "overload_draw.png".CardImagePath();
    public override string BetaPortraitPath => "overload_draw.png".CardImagePath();
    private int DrawAmount => CurrentUpgradeLevel > 0 ? 3 : 2;

    public OverloadDraw() : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithCards(2, 1);
        WithExplanations(CardPreviewExplanation<Slag>());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await CardPileCmd.Draw(ctx, DrawAmount, Owner, false);
        var slag = ThermalVortexGeneratedCards.CreateGeneratedCard<Slag>(this);
        if (slag is not null)
            await ThermalVortexCommandCompat.AddGeneratedCardToCombat(slag, PileType.Discard, Owner, CardPilePosition.Top);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class Slag : MainDeckCard
{
    public override string CustomPortraitPath => "slag.png".BigCardImagePath();
    public override string PortraitPath => "slag.png".CardImagePath();
    public override string BetaPortraitPath => "slag.png".CardImagePath();

    public Slag() : base(1, CardType.Status, CardRarity.Status, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
    }

    protected override Task OnPlay(PlayerChoiceContext ctx, CardPlay play) =>
        Task.CompletedTask;

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class MillenniumContractBook : MonsterCard, IMillenniumCard, IMillenniumMonster, ICyberCopyableEffectProvider, IMonsterFieldEnterResolvedListener
{
    public const int BaseMaxHp = 1;
    public override string CustomPortraitPath => "millennium_contract_book.png".BigCardImagePath();
    public override string PortraitPath => "millennium_contract_book.png".CardImagePath();
    public override string BetaPortraitPath => "millennium_contract_book.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;
    private int DrawReduction => UpgradeVarValue("DrawReduction");

    public MillenniumContractBook() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
        WithUpgradeVar("DrawReduction", 1, 0);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            EnergyExplanation());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);
    }

    public IEnumerable<ICyberCopyableEffect> CreateCyberCopyableEffects()
    {
        yield return new CyberContractBookEffect(DrawReduction);
    }

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        await ThermalVortexCommandCompat.ApplyPower<MillenniumContractBookPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
        Owner.Creature.GetPower<MillenniumContractBookPower>()?.BindSource(this, DrawReduction);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class SinkingLand : MainDeckCard
{
    public override string CustomPortraitPath => "sinking_land.png".BigCardImagePath();
    public override string PortraitPath => "sinking_land.png".CardImagePath();
    public override string BetaPortraitPath => "sinking_land.png".CardImagePath();
    private int EnergyAmount => CurrentUpgradeLevel > 0 ? 2 : 1;

    public SinkingLand() : base(0, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
        WithCards(2, 0);
        WithEnergy(1, 1);
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || HasRemovableSlot());

    private bool HasRemovableSlot() =>
        Owner?.PlayerCombatState is not null && MonsterFieldService.GetDisplayCapacity(Owner) > 0;

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        // Auto-play may bypass IsPlayable. Placement projections are not real
        // slots and cannot be spent for cards or energy.
        if (!HasRemovableSlot())
            return;

        await ThermalVortexCommandCompat.ApplyPower<SinkingLandPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
        await MonsterFieldHealthService.EnforceCapacity(ctx, Owner, this);
        await CardPileCmd.Draw(ctx, 2, Owner, false);
        await PlayerCmd.GainEnergy(EnergyAmount, Owner);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class DimensionalExpansion : MainDeckCard
{
    public override string CustomPortraitPath => "dimensional_expansion.png".BigCardImagePath();
    public override string PortraitPath => "dimensional_expansion.png".CardImagePath();
    public override string BetaPortraitPath => "dimensional_expansion.png".CardImagePath();

    public DimensionalExpansion() : base(2, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCommandCompat.ApplyPower<DimensionalExpansionPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
    }
}

public class OverloadedSummonSlot : MainDeckCard
{
    public override string CustomPortraitPath => "overloaded_summon_slot.png".BigCardImagePath();
    public override string PortraitPath => "overloaded_summon_slot.png".CardImagePath();
    public override string BetaPortraitPath => "overloaded_summon_slot.png".CardImagePath();
    private int Slots => UpgradeVarValue("Slots");

    public OverloadedSummonSlot() : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithUpgradeVar("Slots", 1, 2);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var slots = Slots;
        var power = await ThermalVortexCommandCompat.ApplyPower<OverloadedSummonSlotPower>(
            ctx, Owner.Creature, slots, Owner.Creature, this, false);
        power?.RegisterGrant(slots);
    }

    protected override void AddExtraArgsToDescription(LocString locString)
    {
        base.AddExtraArgsToDescription(locString);
        locString.Add("DestroySlots", OverloadedSummonSlotPower.SlotsToDestroyAtTurnEnd);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class ProtectCore : MainDeckCard
{
    public override string CustomPortraitPath => "protect_core.png".BigCardImagePath();
    public override string PortraitPath => "protect_core.png".CardImagePath();
    public override string BetaPortraitPath => "protect_core.png".CardImagePath();

    public ProtectCore() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || MonsterFieldService.Count(Owner) > 0);

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var selected = await CardSelectionHelper.ChooseOne(
            ctx,
            Owner,
            MonsterFieldService.GetMonsters(Owner).ToList(),
            "THERMALVORTEX-PROTECT_CORE.selectionPrompt",
            false);
        if (selected is not null)
            MonsterFieldService.MoveMonsterToLeftByCardEffect(selected);
    }
}

public class SharedFate : MainDeckCard
{
    public override string CustomPortraitPath => "shared_fate.png".BigCardImagePath();
    public override string PortraitPath => "shared_fate.png".CardImagePath();
    public override string BetaPortraitPath => "shared_fate.png".CardImagePath();

    public SharedFate() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCommandCompat.ApplyPower<SharedFatePower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
    }
}
