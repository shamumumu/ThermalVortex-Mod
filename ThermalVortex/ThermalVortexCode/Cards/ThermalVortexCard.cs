using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils;
using ThermalVortex.ThermalVortexCode.Character;
using ThermalVortex.ThermalVortexCode.Extensions;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Patches;
using ThermalVortex.ThermalVortexCode.RewardPools;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public abstract class ThermalVortexCard(int cost, CardType type, CardRarity rarity, TargetType target) :
    ConstructedCardModel(cost, type, rarity, target)
{
    private readonly List<CardExplanationBinding> _explanationBindings = [];
    private bool _explanationTipsAdded;
    private static readonly IReadOnlyDictionary<string, string[]> ExplanationDependencies =
        new Dictionary<string, string[]>
        {
            ["THERMALVORTEX-XYZ"] =
            ["THERMALVORTEX-MATERIAL", "THERMALVORTEX-EXTRA_DECK", "THERMALVORTEX-FUSION_MONSTER"],
            ["THERMALVORTEX-FUSION_MATERIAL"] =
            ["THERMALVORTEX-XYZ", "THERMALVORTEX-MATERIAL"]
        };
    private CyberDevourDisplaySnapshot _cyberDevourDisplaySnapshot;

    // Retain the legacy bool for old saves. An old true value represents one
    // spent attempt when the new integer property is absent.
    [SavedProperty]
    public bool AliceConsumedThisTurn { get; set; }

    [SavedProperty]
    public int AliceSummonsUsedThisTurn { get; set; }

    [SavedProperty]
    public int AliceQuotaTurnNumber { get; set; } = -1;

    private PlayerCombatState _aliceQuotaCombatState;

    internal bool TryConsumeAliceSpecialSummonQuota(int limit)
    {
        var combatState = Owner?.PlayerCombatState;
        if (combatState is null)
            return false;

        // The first binding preserves the legacy saved counters. After that,
        // the owner's actual turn number also distinguishes extra turns.
        if ((_aliceQuotaCombatState is not null
                && !ReferenceEquals(_aliceQuotaCombatState, combatState))
            || (AliceQuotaTurnNumber >= 0 && AliceQuotaTurnNumber != combatState.TurnNumber))
        {
            ResetAliceSpecialSummonQuota();
        }
        _aliceQuotaCombatState = combatState;
        AliceQuotaTurnNumber = combatState.TurnNumber;

        var used = Math.Max(AliceSummonsUsedThisTurn, AliceConsumedThisTurn ? 1 : 0);
        if (used >= Math.Max(0, limit))
            return false;

        AliceSummonsUsedThisTurn = used + 1;
        AliceConsumedThisTurn = true;
        return true;
    }

    internal void ResetAliceSpecialSummonQuota()
    {
        AliceSummonsUsedThisTurn = 0;
        AliceConsumedThisTurn = false;
        _aliceQuotaCombatState = null;
        AliceQuotaTurnNumber = -1;
    }

    // Use the template art for the playable prototype until individual portraits exist.
    public override string CustomPortraitPath => "card.png".BigCardImagePath();
    public override string PortraitPath => "card.png".CardImagePath();
    public override string BetaPortraitPath => "card.png".CardImagePath();

    // Generic combat generation uses the same eligible cards as ordinary rewards.
    // Effects that explicitly create a named token do not consult this property.
    public override bool CanBeGeneratedInCombat =>
        RewardPoolCatalog.IsLegalMainRewardCandidate(this);

    public override CardPoolModel VisualCardPool
    {
        get
        {
            if (this is XyzMonsterCard)
                return ModelDb.CardPool<ExtraDeckCardPool>();

            if (ThermalVortexGeneratedCards.UsesGeneratedColorlessVisual(this))
                return ModelDb.CardPool<MillenniumColorlessCardPool>();

            return base.VisualCardPool;
        }
    }

    protected Player TryGetOwner()
    {
        try
        {
            return Owner;
        }
        catch
        {
            // Card library renders canonical cards before they have mutable combat ownership.
            return null;
        }
    }

    internal IReadOnlyList<CardExplanationBinding> ExplanationBindings => _explanationBindings;

    internal CyberDevourDisplaySnapshot CyberDevourDisplaySnapshot => _cyberDevourDisplaySnapshot;

    internal void AttachCyberDevourDisplaySnapshot(CyberDevourDisplaySnapshot snapshot) =>
        _cyberDevourDisplaySnapshot = snapshot;

    private protected void WithExplanations(params CardExplanationBinding[] bindings)
    {
        if (bindings is null || bindings.Length == 0)
            return;

        _explanationBindings.AddRange(bindings);
        if (!_explanationTipsAdded)
        {
            _explanationTipsAdded = true;
            WithTips(card => IHoverTip.RemoveDupes(
                ((ThermalVortexCard)card).CreateExplanationTips(card)
                    .Concat(CyberDevourState.CreateHoverTips(card))));
        }
    }

    private IEnumerable<IHoverTip> CreateExplanationTips(CardModel card)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in _explanationBindings)
        {
            if (!binding.AppliesTo(card) || !visited.Add(binding.Id))
                continue;

            yield return binding.CreateTip(card);
            foreach (var tip in CreateDependencyTips(binding.Id, visited))
                yield return tip;
        }
    }

    internal static IEnumerable<IHoverTip> CreateDependencyTips(string key, HashSet<string> visited)
    {
        if (!ExplanationDependencies.TryGetValue(key, out var dependencies))
            yield break;

        foreach (var dependency in dependencies)
        {
            if (!visited.Add(dependency))
                continue;

            yield return KeywordTip(dependency);
            foreach (var tip in CreateDependencyTips(dependency, visited))
                yield return tip;
        }
    }

    private protected static CardExplanationBinding KeywordExplanation(
        string key,
        Func<CardModel, bool> appliesTo = null) =>
        new(
            key,
            card => KeywordTip(key, (card as ThermalVortexCard)?.TryGetOwner()),
            _ => new LocString("card_keywords", $"{key}.title").GetFormattedText(),
            appliesTo);

    // These bindings explain words in the card text without granting keywords,
    // powers, block, or energy. Keep the native tips unchanged so the final
    // CardModel.HoverTips distinct pass also removes native automatic duplicates.
    private protected static CardExplanationBinding NativeKeywordExplanation(
        CardKeyword keyword,
        Func<CardModel, bool> appliesTo = null) =>
        new(
            $"NATIVE_KEYWORD:{keyword}",
            _ => HoverTipFactory.FromKeyword(keyword),
            _ => ((HoverTip)HoverTipFactory.FromKeyword(keyword)).Title,
            card => (appliesTo?.Invoke(card) ?? true)
                && !card.Keywords.Contains(keyword)
                && (keyword != CardKeyword.Exhaust || !card.Keywords.Contains(CardKeyword.Ethereal)));

    private protected static CardExplanationBinding BlockExplanation() =>
        new(
            "NATIVE_STATIC:BLOCK",
            _ => HoverTipFactory.Static(StaticHoverTip.Block, []),
            _ => new LocString("static_hover_tips", "BLOCK.title").GetFormattedText(),
            card => !card.GainsBlock);

    private protected static CardExplanationBinding PowerExplanation<T>() where T : PowerModel =>
        new(
            $"NATIVE_POWER:{typeof(T).FullName}",
            _ => HoverTipFactory.FromPower<T>(null),
            _ => ((HoverTip)HoverTipFactory.FromPower<T>(null)).Title);

    private protected static CardExplanationBinding EnergyExplanation() =>
        new(
            "NATIVE_STATIC:ENERGY",
            card => HoverTipFactory.ForEnergy(card),
            _ => new LocString("static_hover_tips", "ENERGY.title").GetFormattedText());

    private protected static CardExplanationBinding ExtraDeckExplanation() =>
        KeywordExplanation("THERMALVORTEX-EXTRA_DECK");

    private protected static CardExplanationBinding CardPreviewExplanation<T>(
        bool matchSourceUpgrade = true) where T : CardModel =>
        new(
            $"CARD_PREVIEW:{typeof(T).FullName}",
            source => CardPreviewTip<T>(source, matchSourceUpgrade),
            source => CardPreviewTitle<T>(source, matchSourceUpgrade));

    protected static IEnumerable<IHoverTip> CardPreviewTips(params CardModel[] cards)
    {
        foreach (var preview in cards)
        {
            if (preview is not null)
                yield return new CardHoverTip(preview);
        }
    }

    protected static IHoverTip CardPreviewTip<T>() where T : CardModel =>
        new CardHoverTip(ModelDb.Card<T>());

    protected static IHoverTip CardPreviewTip<T>(CardModel source) where T : CardModel
        => CardPreviewTip<T>(source, matchSourceUpgrade: true);

    private static IHoverTip CardPreviewTip<T>(
        CardModel source,
        bool matchSourceUpgrade) where T : CardModel
    {
        var preview = ModelDb.Card<T>()?.ToMutable() as T;
        if (matchSourceUpgrade)
            ThermalVortexGeneratedCards.MatchUpgrade(preview, source);
        return new CardHoverTip(preview ?? ModelDb.Card<T>());
    }

    private static string CardPreviewTitle<T>(
        CardModel source,
        bool matchSourceUpgrade) where T : CardModel
    {
        var preview = ModelDb.Card<T>()?.ToMutable() as T;
        if (matchSourceUpgrade)
            ThermalVortexGeneratedCards.MatchUpgrade(preview, source);
        return (preview ?? ModelDb.Card<T>())?.Title ?? typeof(T).Name;
    }

    internal static IHoverTip KeywordTip(string key, Player owner = null)
    {
        var description = new LocString("card_keywords", $"{key}.description");
        if (key == "THERMALVORTEX-MILLENNIUM_COUNT")
        {
            // Related-card and library previews may be ownerless. Use the local
            // player in that case, and always show the live total, not an action's
            // prepared count or a summoned card's saved resolution value.
            owner = ResolveMillenniumCountOwner(owner);
            var hasCombatCount = owner?.PlayerCombatState is not null
                && owner.Creature?.CombatState is not null;
            description.Add("HasCurrentMillenniumCount", hasCombatCount);
            description.Add("CurrentMillenniumCount",
                hasCombatCount ? MillenniumSeries.CountTotal(owner) : 0);
        }

        return new HoverTip(
            new LocString("card_keywords", $"{key}.title"),
            description) { Id = key };
    }

    private static Player ResolveMillenniumCountOwner(Player owner)
    {
        if (owner is not null)
            return owner;

        try
        {
            return RunManager.Instance?.DebugOnlyGetState()?.Players
                .FirstOrDefault(player => LocalContext.IsMe(player));
        }
        catch
        {
            // Canonical previews can be rendered before a run or local player exists.
            return null;
        }
    }

    protected void WithUpgradeVar(string name, int baseValue, int upgradedValue)
    {
        var upgradeBy = upgradedValue - baseValue;
        WithVar(new UpgradePreviewDynamicVar(name, baseValue, upgradeBy).WithUpgrade(upgradeBy));
    }

    protected void WithUpgradeVar(
        string name,
        int baseValue,
        int upgradedValue,
        Func<CardModel, int> upgradedPreviewValue)
    {
        var upgradeBy = upgradedValue - baseValue;
        WithVar(new UpgradePreviewDynamicVar(
                name,
                baseValue,
                upgradeBy,
                upgradedPreviewValue)
            .WithUpgrade(upgradeBy));
    }

    protected int UpgradeVarValue(string name) =>
        DynamicVars[name].IntValue;

    protected void WithFormulaDamage(
        int baseUnitDamage,
        int upgradedUnitDamage,
        Func<CardModel, Creature, decimal> multiplier,
        ValueProp props = ValueProp.Move)
    {
        WithCalculatedDamage(
            0,
            baseUnitDamage,
            multiplier,
            props,
            0,
            upgradedUnitDamage - baseUnitDamage);
    }

    protected CalculatedDamageVar FormulaDamage => DynamicVars.CalculatedDamage;

    protected int FormulaDamageUnit => DynamicVars.ExtraDamage.IntValue;

    protected void WithMonsterHpUpgrade(int baseHp, int upgradedHp) =>
        WithUpgradeVar("MonsterHp", baseHp, upgradedHp);

    protected void AddMonsterHpArg(LocString locString, int monsterHp)
    {
        if (!DynamicVars.ContainsKey("MonsterHp"))
            locString.Add("MonsterHp", monsterHp);
    }

    protected override void AddExtraArgsToDescription(LocString locString)
    {
        base.AddExtraArgsToDescription(locString);
        CardEffectPreviewValues.AddArgs(this, locString);
    }

}

internal sealed class CardExplanationBinding(
    string id,
    Func<CardModel, IHoverTip> createTip,
    Func<CardModel, string> getExpectedTitle,
    Func<CardModel, bool> appliesTo = null)
{
    internal string Id { get; } = id;

    internal IHoverTip CreateTip(CardModel card) => createTip(card);

    internal string GetExpectedTitle(CardModel card) => getExpectedTitle(card);

    internal bool AppliesTo(CardModel card) => appliesTo?.Invoke(card) ?? true;
}

internal sealed class UpgradePreviewDynamicVar(
    string name,
    int baseValue,
    int upgradeBy,
    Func<CardModel, int> upgradedPreviewValue = null) :
    DynamicVar(name, baseValue)
{
    public override void UpdateCardPreview(
        CardModel card,
        CardPreviewMode mode,
        Creature target,
        bool isInHand)
    {
        // BaseValue already contains the applied upgrade on an upgraded mutable
        // card. Only add the registered delta while rendering the *next*
        // upgrade preview, otherwise an upgraded card is displayed as though it
        // had been upgraded twice.
        var showNextUpgrade = mode == CardPreviewMode.Upgrade && card.IsUpgradable;
        PreviewValue = showNextUpgrade
            ? upgradedPreviewValue?.Invoke(card) ?? BaseValue + upgradeBy
            : BaseValue;
    }
}

[Pool(typeof(ThermalVortexCardPool))]
public abstract class MainDeckCard(int cost, CardType type, CardRarity rarity, TargetType target) :
    ThermalVortexCard(cost, type, rarity, target);

public abstract class MonsterCard(int cost, CardType type, CardRarity rarity, TargetType target) :
    MainDeckCard(cost, type, rarity, target)
{
    public abstract int MonsterMaxHp { get; }

    protected override bool IsPlayable =>
        base.IsPlayable && (EffectTargeting.IsImmediatePlayEvaluation
            ? MonsterFieldService.CanPlaceOnField(this)
            : MonsterFieldService.CanNormalSummonOnField(this));

    protected override void AddExtraArgsToDescription(LocString locString)
    {
        base.AddExtraArgsToDescription(locString);
        AddMonsterHpArg(locString, MonsterMaxHp);
    }

    protected Task ResolveMonsterSummon(CardPlay play) =>
        ResolveMonsterSummon(play, showManualSummonVfx: true);

    protected async Task ResolveMonsterSummon(CardPlay play, bool showManualSummonVfx)
    {
        // Reserve the real Phantom at the copied monster's own summon point,
        // before its linked summons look for existing monsters or free slots.
        if (await ChaosPhantomCopyService.TryResolveCopiedMonsterSummon(this, play, showManualSummonVfx))
            return;

        if (play.IsFirstInSeries)
        {
            MonsterFieldService.MarkPending(this);
            return;
        }

        // Replay effects execute the same CardModel more than once, but a card
        // object can only occupy one pile slot. Materialize one combat clone for
        // every additional CardPlay so Duplication Potion produces a real second
        // monster body with the same upgrades and card state.
        var combatState = Owner?.Creature?.CombatState;
        var duplicate = combatState?.CloneCard(this);
        if (duplicate is null)
            return;

        if (duplicate is ThermalVortexCard duplicateMonster)
            duplicateMonster.ResetAliceSpecialSummonQuota();

        // CombatState.CloneCard calls ClonePreservingMutability directly, so
        // it does not pass through CardModel.CreateClone's postfix.
        CyberDevourState.CopyForCombatClone(this, duplicate);

        // CloneCard registers the clone with combat state and preserves its
        // owner. Assigning Owner again throws "already has an owner" and leaves
        // the duplicated CardPlay action stuck in its exception path.
        MonsterFieldUpgradeLockService.CopyForReplay(this, duplicate);
        MonsterFieldDampenCompatibilityPatch.CopyRestoreLevelForReplay(this, duplicate);
        var entered = false;
        var summoned = false;
        void MarkEntered(MonsterFieldEnterEvent enterEvent)
        {
            if (ReferenceEquals(enterEvent.Card, duplicate))
                entered = true;
        }

        MonsterFieldEventService.MonsterEnteredVisual += MarkEntered;
        try
        {
            summoned = await MonsterFieldService.SpecialSummon(null, duplicate, this);
        }
        finally
        {
            MonsterFieldEventService.MonsterEnteredVisual -= MarkEntered;
            // Immediate leave/exhaust effects belong to a committed monster.
            // Only remove a clone that never entered the field at all.
            if (!summoned && !entered)
                await EffectTargeting.RemoveUncommittedTransientCard(duplicate);
        }
    }

    protected override PileType GetResultPileTypeForCardPlay()
    {
        var canPlaceOnField = EffectTargeting.IsImmediatePlayEvaluation
            ? MonsterFieldService.CanPlaceOnField(this)
            : MonsterFieldService.CanNormalSummonOnField(this);
        MonsterFieldService.UnmarkPending(this, keepFieldOrderReservation: canPlaceOnField);
        if (canPlaceOnField)
            return MonsterFieldPile.FieldPileType;

        var baseResultPile = base.GetResultPileTypeForCardPlay();
        if (baseResultPile != PileType.None)
            return baseResultPile;

        // Base power cards leave combat instead of entering a result pile. A
        // power-type monster that lost its field slot during resolution is
        // still a monster card, so preserve the normal discard/exhaust result.
        if (ExhaustOnNextPlay || Keywords.Contains(CardKeyword.Exhaust))
        {
            ExhaustOnNextPlay = false;
            return PileType.Exhaust;
        }

        return PileType.Discard;
    }
}
