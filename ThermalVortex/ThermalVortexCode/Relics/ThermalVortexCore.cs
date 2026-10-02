using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Patches;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.RewardPools;
using ThermalVortex.ThermalVortexCode.Vfx;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Relics;

internal enum RewardPoolDefinitionLoadState
{
    RuntimeDefinition,
    ValidSavedSnapshot,
    MissingSavedSnapshot,
    InvalidSavedSnapshot
}

public class ThermalVortexCore : ThermalVortexRelic
{
    private const string SaveSeparator = ";";
    private const string UpgradeSeparator = ":";
    private const string EnchantmentJsonPrefix = "json1|";
    private const string EmptyExtraDeckSave = "empty1";
    private static readonly UTF8Encoding EnchantmentEncoding = new(false, true);
    private const string VortexSaveId = "Vortex";
    private const string DetonationSaveId = "Detonation";
    private const string CircuitTalismanBeastSaveId = "CircuitTalismanBeast";
    private const string FullArmorThunderLanceSaveId = "FullArmorThunderLance";
    private const string DormantMagneticFieldBeastSaveId = "DormantMagneticFieldBeast";
    private const string BrilliantRebootKnightSaveId = "BrilliantRebootKnight";
    private const string DivineArsenalFurnaceGodSaveId = "DivineArsenalFurnaceGod";
    private const string ChimeratechOverdragonSaveId = "ChimeratechOverdragon";
    private const string RelinquishedSaveId = "Relinquished";
    private const string CyberEndDragonSaveId = "CyberEndDragon";
    private const string CyberDragonInfinitySaveId = "CyberDragonInfinity";
    private const string WingedDragonOfRaSphereModeSaveId = "WingedDragonOfRaSphereMode";
    private const string MillenniumGrandThiefSaveId = "MillenniumGrandThief";
    private const string ExodiaSummonerSaveId = "ExodiaSummoner";
    private const string MillenniumMasterKeySaveId = "MillenniumMasterKey";
    private const string EvilExodiaSaveId = "EvilExodia";
    private const string ExodiaGuardianSaveId = "ExodiaGuardian";
    public const int MaxOwnedExtraDeckCards = 10;
    private const int ExtraDeckRewardOptionCount = 3;

    private List<ExtraDeckEntry> _ownedExtraDeck = [new(typeof(Detonation), 0)];
    private string _ownedExtraDeckLoadFailure;
    private List<ExtraDeckEntry> _extraDeck = [];
    private Dictionary<XyzMonsterCard, ExtraDeckReturnState> _trackedExtraDeckSummons = new(new ReferenceComparer<XyzMonsterCard>());
    private Dictionary<XyzMonsterCard, ExtraDeckIdentity> _physicalExtraDeckIdentities = new(new ReferenceComparer<XyzMonsterCard>());
    private ConditionalWeakTable<XyzMonsterCard, ExtraDeckIdentity> _previewExtraDeckIdentities = new();
    private ICombatState _extraDeckCombat;
    private long _nextCombatEntryId;
    private static readonly List<WeakReference<ThermalVortexCore>> CombatCores = [];
    private RewardPoolDefinition _rewardPoolDefinition = RewardPoolDefinition.Standard.Clone();
    private RewardPoolDefinitionLoadState _rewardPoolDefinitionLoadState = RewardPoolDefinitionLoadState.RuntimeDefinition;
    private string _unreadableRewardPoolSnapshot;
    private int _rewardPoolOrbSlotContribution;
    private bool _hasRewardPoolOrbSlotContributionMetadata;
    private bool _loggedInvalidRewardPoolSnapshot;
    private int _selfDamageTakenThisRun;
    private int _damageReceivedThisCombat;
    private Dictionary<uint, int> _enemyAttackDamageThisCombat = [];
    private int _raReviveCount;
    private static readonly System.Reflection.FieldInfo CardRewardSynchronizerField =
        typeof(CardReward).GetField("_synchronizer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private enum ExtraDeckSummonMode
    {
        FieldOnly,
        FieldAndHand,
        CyberloadFusion,
        MiracleFusion,
        FusionGate,
        FutureFusion
    }

    private enum ExtraDeckSelectionStage
    {
        Monster,
        RequiredFieldMaterials,
        RemainingMaterials
    }

    private enum ExtraDeckPrecommitOutcome
    {
        Completed,
        PlanInvalidated,
        SelectionInvalidated
    }

    private sealed class ExtraDeckEntry(
        Type monsterType,
        int upgradeLevel,
        SerializableEnchantment enchantment = null,
        int ownedEntryIndex = -1,
        long combatEntryId = 0,
        ICombatState combat = null,
        CyberDevourCombatSnapshot devourSnapshot = null)
    {
        public Type MonsterType { get; } = monsterType;
        public int UpgradeLevel { get; set; } = Math.Max(0, upgradeLevel);
        public SerializableEnchantment Enchantment { get; set; } = CloneSerializableEnchantment(enchantment);
        // Combat-only identity within the owned list, independent of removals
        // and insertions in the remaining deck. Never added to the save format.
        public int OwnedEntryIndex { get; set; } = ownedEntryIndex;
        public long CombatEntryId { get; set; } = combatEntryId;
        public ICombatState Combat { get; set; } = combat;
        public CyberDevourCombatSnapshot DevourSnapshot { get; set; } = devourSnapshot?.DeepClone();

        public ExtraDeckEntry Clone(int? ownedEntryIndex = null) =>
            new(MonsterType, UpgradeLevel, Enchantment, ownedEntryIndex ?? OwnedEntryIndex,
                CombatEntryId, Combat, DevourSnapshot);

        public ExtraDeckEntry CloneOwned() => new(MonsterType, UpgradeLevel, Enchantment);
    }

    private sealed record ExtraDeckIdentity(ICombatState Combat, long EntryId);

    private sealed class ExtraDeckReturnState(ExtraDeckEntry entry, int index)
    {
        public ExtraDeckEntry Entry { get; } = entry.Clone();
        public int Index { get; } = Math.Max(0, index);
    }

    private sealed class MaterialSelectionPlan
    {
        public MaterialSelectionPlan(
            IReadOnlyList<CardModel> candidates,
            IReadOnlyList<CardModel> fixedMaterials,
            int minimumMaterials,
            int maximumMaterials,
            int requiredAdditionalFieldMaterials,
            bool fixedSelection,
            IReadOnlyList<CardModel> automaticSelection)
        {
            Candidates = candidates;
            FixedMaterials = fixedMaterials;
            MinimumMaterials = minimumMaterials;
            MaximumMaterials = maximumMaterials;
            RequiredAdditionalFieldMaterials = requiredAdditionalFieldMaterials;
            FixedSelection = fixedSelection;
            AutomaticSelection = automaticSelection;
        }

        public IReadOnlyList<CardModel> Candidates { get; }
        public IReadOnlyList<CardModel> FixedMaterials { get; }
        public int MinimumMaterials { get; }
        public int MaximumMaterials { get; }
        public int RequiredAdditionalFieldMaterials { get; }
        public bool FixedSelection { get; }
        public IReadOnlyList<CardModel> AutomaticSelection { get; }
    }

    private sealed class ExtraDeckSummonSelection(
        XyzMonsterCard monster,
        IReadOnlyList<CardModel> materials,
        int entryIndex,
        ExtraDeckEntry entry)
    {
        public XyzMonsterCard Monster { get; } = monster;
        public IReadOnlyList<CardModel> Materials { get; } = materials;
        public int EntryIndex { get; } = entryIndex;
        public ExtraDeckEntry Entry { get; } = entry;
    }

    private readonly record struct ExtraDeckPrecommitResult(
        ExtraDeckPrecommitOutcome Outcome,
        ExtraDeckSummonSelection Selection)
    {
        public bool Completed => Outcome == ExtraDeckPrecommitOutcome.Completed;
    }

    public bool HasExtraDeckCard => _extraDeck.Count > 0;
    public int RemainingExtraDeckCardCount => _extraDeck.Count;
    public bool CanXyzSummon => CanExtraDeckSummon(ExtraDeckSummonMode.FieldOnly);
    public bool CanUpgradedXyzSummon => CanExtraDeckSummon(ExtraDeckSummonMode.FieldAndHand);
    public bool CanLoadFusionSummon => CanExtraDeckSummon(ExtraDeckSummonMode.CyberloadFusion);
    public bool CanMiracleFusionSummon => CanExtraDeckSummon(ExtraDeckSummonMode.MiracleFusion);
    public bool CanFusionGateSummon => CanExtraDeckSummon(ExtraDeckSummonMode.FusionGate);
    public bool CanPrepareFutureFusion(int maximumMaterials) => CanFutureFusionSummon(maximumMaterials);
    public IReadOnlyList<CardModel> OwnedExtraDeckPreviewCards => CreatePreviewCards(_ownedExtraDeck);
    public IReadOnlyList<CardModel> RemainingExtraDeckPreviewCards => CreatePreviewCards(_extraDeck);
    internal IReadOnlyList<CardModel> OwnedExtraDeckViewCards => CreateOwnedExtraDeckViewCards();
    internal int OwnedExtraDeckCardCount => _ownedExtraDeck.Count;
    internal int DisplayRemainingExtraDeckCardCount => IsCombatExtraDeckPrepared()
        ? _extraDeck.Count
        : _ownedExtraDeck.Count;
    internal bool IsCombatExtraDeckPreparedForDisplay => IsCombatExtraDeckPrepared();
    internal bool CanAcceptExtraDeckReward => _ownedExtraDeck.Count < MaxOwnedExtraDeckCards;
    internal bool IsConstructedRewardPoolEnabled => _rewardPoolDefinition?.IsEnabled == true;
    internal RewardPoolDefinition CurrentRewardPoolDefinition =>
        (_rewardPoolDefinition ?? RewardPoolDefinition.Standard).Clone();
    internal RewardPoolDefinitionLoadState RewardPoolDefinitionLoadState => _rewardPoolDefinitionLoadState;
    internal int RewardPoolOrbSlotContribution => Math.Max(0, _rewardPoolOrbSlotContribution);
    internal bool HasRewardPoolOrbSlotContributionMetadata => _hasRewardPoolOrbSlotContributionMetadata;
    public int SelfDamageTakenThisRun => Math.Max(0, _selfDamageTakenThisRun);
    public int DamageReceivedThisCombat => Math.Max(0, _damageReceivedThisCombat);

    public override RelicRarity Rarity => RelicRarity.Starter;
    protected override string RelicIconFileName => "unfinished_millennium_puzzle.png";
    protected override string RelicIconOutlineFileName => "unfinished_millennium_puzzle_outline.png";

    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get
        {
            yield return new HoverTip(
                new LocString("card_keywords", "THERMALVORTEX-EXTRA_DECK.title"),
                new LocString("card_keywords", "THERMALVORTEX-EXTRA_DECK.description"),
                Icon);
        }
    }

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();

        // RelicModel cloning is shallow unless mutable fields are explicitly
        // copied. Sharing these lists with the canonical starter relic made
        // extra-deck rewards from an abandoned run become the next run's
        // starting extra deck.
        _ownedExtraDeck = _ownedExtraDeck.Select(entry => entry.CloneOwned()).ToList();
        _extraDeck = _extraDeck.Select(entry => entry.Clone()).ToList();
        _rewardPoolDefinition = (_rewardPoolDefinition ?? RewardPoolDefinition.Standard).Clone();
        // The model clone also retains the immutable unreadable save string.
        // The load state, contribution, and metadata marker are value fields and
        // are already copied by the model clone. Normalize the persisted count
        // here so malformed values cannot propagate through a deep clone.
        _rewardPoolOrbSlotContribution = Math.Max(0, _rewardPoolOrbSlotContribution);

        var trackedSummons = new Dictionary<XyzMonsterCard, ExtraDeckReturnState>(
            new ReferenceComparer<XyzMonsterCard>());
        foreach (var (card, state) in _trackedExtraDeckSummons)
            trackedSummons[card] = new ExtraDeckReturnState(state.Entry, state.Index);
        _trackedExtraDeckSummons = trackedSummons;
        _physicalExtraDeckIdentities = new Dictionary<XyzMonsterCard, ExtraDeckIdentity>(
            _physicalExtraDeckIdentities, new ReferenceComparer<XyzMonsterCard>());
        _previewExtraDeckIdentities = new();
        _enemyAttackDamageThisCombat = new Dictionary<uint, int>(_enemyAttackDamageThisCombat);
    }

    internal void ResetForNewRun()
    {
        _ownedExtraDeck.Clear();
        _ownedExtraDeck.Add(new ExtraDeckEntry(typeof(Detonation), 0));
        _ownedExtraDeckLoadFailure = null;
        ClearCombatExtraDeck();
        _rewardPoolDefinition = RewardPoolDefinition.Standard.Clone();
        _rewardPoolDefinitionLoadState = RewardPoolDefinitionLoadState.RuntimeDefinition;
        _unreadableRewardPoolSnapshot = null;
        _rewardPoolOrbSlotContribution = 0;
        _hasRewardPoolOrbSlotContributionMetadata = false;
        _loggedInvalidRewardPoolSnapshot = false;
        _selfDamageTakenThisRun = 0;
        _damageReceivedThisCombat = 0;
        _enemyAttackDamageThisCombat.Clear();
        _raReviveCount = 0;
    }

    public override RelicModel GetUpgradeReplacement()
    {
        if (ModelDb.Relic<AncientThermalVortexCore>().ToMutable() is not AncientThermalVortexCore replacement)
            return null;

        replacement.CopyStateFrom(this);
        return replacement;
    }

    internal void CopyStateFrom(ThermalVortexCore source)
    {
        if (source is null || ReferenceEquals(source, this))
            return;

        OwnedExtraDeckSave = source.OwnedExtraDeckSave;
        ApplyRewardPoolDefinition(source._rewardPoolDefinition);
        _rewardPoolDefinitionLoadState = source._rewardPoolDefinitionLoadState;
        _unreadableRewardPoolSnapshot = source._unreadableRewardPoolSnapshot;
        _rewardPoolOrbSlotContribution = source.RewardPoolOrbSlotContribution;
        _hasRewardPoolOrbSlotContributionMetadata = source._hasRewardPoolOrbSlotContributionMetadata;
        SelfDamageTakenThisRunSave = source.SelfDamageTakenThisRunSave;
        RaReviveCountSave = source.RaReviveCountSave;
        _extraDeck.Clear();
        _extraDeck.AddRange(source._extraDeck.Select(entry => entry.Clone()));
        _extraDeckCombat = source._extraDeckCombat;
        _nextCombatEntryId = source._nextCombatEntryId;
        _trackedExtraDeckSummons.Clear();
        foreach (var (card, state) in source._trackedExtraDeckSummons)
            _trackedExtraDeckSummons[card] = new ExtraDeckReturnState(state.Entry, state.Index);
        _physicalExtraDeckIdentities = new Dictionary<XyzMonsterCard, ExtraDeckIdentity>(
            source._physicalExtraDeckIdentities, new ReferenceComparer<XyzMonsterCard>());
        _previewExtraDeckIdentities = new();
        if (_extraDeckCombat is not null)
            RegisterCombatCore();
    }

    [SavedProperty]
    public string OwnedExtraDeckSave
    {
        get
        {
            // Do not let a caller that caught a failed load save the default or
            // previously loaded deck over the unreadable original entries.
            if (_ownedExtraDeckLoadFailure is not null)
                throw new InvalidDataException(_ownedExtraDeckLoadFailure);
            return _ownedExtraDeck.Count == 0 ? EmptyExtraDeckSave : SerializeExtraDeck(_ownedExtraDeck);
        }
        set
        {
            try
            {
                var explicitlyEmpty = string.Equals(value, EmptyExtraDeckSave, StringComparison.Ordinal);
                ReplaceOwnedExtraDeck(explicitlyEmpty ? [] : DeserializeExtraDeck(value), explicitlyEmpty);
                _ownedExtraDeckLoadFailure = null;
            }
            catch (Exception ex)
            {
                _ownedExtraDeckLoadFailure =
                    "Extra Deck save could not be loaded safely. Loading and saving are stopped "
                    + "to preserve the original saved entries. " + ex.Message;
                MainFile.Logger.Info(_ownedExtraDeckLoadFailure);
                throw new InvalidDataException(_ownedExtraDeckLoadFailure, ex);
            }
        }
    }

    [SavedProperty]
    public string RewardPoolDefinitionSave
    {
        get => _unreadableRewardPoolSnapshot
            ?? (_rewardPoolDefinition ?? RewardPoolDefinition.Standard).Serialize();
        set => LoadRewardPoolDefinition(value);
    }

    [SavedProperty]
    public int RewardPoolOrbSlotContributionSave
    {
        get => RewardPoolOrbSlotContribution;
        set
        {
            _rewardPoolOrbSlotContribution = Math.Max(0, value);
            _hasRewardPoolOrbSlotContributionMetadata = true;
        }
    }

    [SavedProperty]
    public int SelfDamageTakenThisRunSave
    {
        get => _selfDamageTakenThisRun;
        set => _selfDamageTakenThisRun = Math.Max(0, value);
    }

    [SavedProperty]
    public int RaReviveCountSave
    {
        get => _raReviveCount;
        set => _raReviveCount = Math.Max(0, value);
    }

    internal void ApplyRewardPoolDefinition(RewardPoolDefinition definition)
    {
        _rewardPoolDefinitionLoadState = RewardPoolDefinitionLoadState.RuntimeDefinition;
        if (definition is null || !definition.IsEnabled)
        {
            _rewardPoolDefinition = RewardPoolDefinition.Standard.Clone();
            _unreadableRewardPoolSnapshot = null;
            _loggedInvalidRewardPoolSnapshot = false;
            return;
        }

        var validation = definition.Validate();
        if (!validation.IsValid)
        {
            FallBackToStandardRewardPool("invalid_definition", validation);
            return;
        }

        _rewardPoolDefinition = definition.Clone();
        _unreadableRewardPoolSnapshot = null;
        _loggedInvalidRewardPoolSnapshot = false;
    }

    internal void SetRewardPoolOrbSlotContribution(int contribution)
    {
        _rewardPoolOrbSlotContribution = Math.Max(0, contribution);
        _hasRewardPoolOrbSlotContributionMetadata = true;
    }

    internal bool IsMainDeckRewardAllowed(CardModel card) =>
        (_rewardPoolDefinition ?? RewardPoolDefinition.Standard).ContainsMainReward(card);

    internal bool IsExtraDeckRewardAllowed(CardModel card) =>
        (_rewardPoolDefinition ?? RewardPoolDefinition.Standard).ContainsExtraReward(card);

    private void LoadRewardPoolDefinition(string serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            _rewardPoolDefinition = RewardPoolDefinition.Standard.Clone();
            _rewardPoolDefinitionLoadState = RewardPoolDefinitionLoadState.MissingSavedSnapshot;
            _unreadableRewardPoolSnapshot = null;
            _loggedInvalidRewardPoolSnapshot = false;
            return;
        }

        if (RewardPoolDefinition.TryDeserialize(serialized, out var definition, out var validation)
            && validation.IsValid)
        {
            ApplyRewardPoolDefinition(definition);
            _rewardPoolDefinitionLoadState = RewardPoolDefinitionLoadState.ValidSavedSnapshot;
            return;
        }

        // Keep the rejected bytes for the next save while gameplay uses Standard.
        _unreadableRewardPoolSnapshot = serialized;
        FallBackToStandardRewardPool("invalid_saved_snapshot", validation);
        _rewardPoolDefinitionLoadState = RewardPoolDefinitionLoadState.InvalidSavedSnapshot;
    }

    private void FallBackToStandardRewardPool(string reason, object validation)
    {
        _rewardPoolDefinition = RewardPoolDefinition.Standard.Clone();
        if (_loggedInvalidRewardPoolSnapshot)
            return;

        _loggedInvalidRewardPoolSnapshot = true;
        MainFile.Logger.Info(
            $"Reward pool snapshot rejected reason={reason} validation={validation ?? "unknown"}; using standard pool");
    }

    public void RecordSelfDamage(int amount)
    {
        if (amount > 0)
            _selfDamageTakenThisRun += amount;
    }

    public void RecordDamageReceivedThisCombat(Creature enemy, int amount)
    {
        if (amount <= 0)
            return;

        _damageReceivedThisCombat += amount;
        if (enemy?.IsEnemy != true || enemy.CombatId is not { } enemyId)
            return;

        _enemyAttackDamageThisCombat.TryGetValue(enemyId, out var recorded);
        _enemyAttackDamageThisCombat[enemyId] = recorded + amount;
    }

    public int GetEnemyAttackDamageThisCombat(Creature enemy) =>
        enemy?.CombatId is { } enemyId && _enemyAttackDamageThisCombat.TryGetValue(enemyId, out var damage)
            ? Math.Max(0, damage)
            : 0;

    public int ConsumeRaReviveHp()
    {
        var sequence = _raReviveCount + 3;
        _raReviveCount++;
        return sequence * sequence;
    }

    public override async Task BeforeCombatStart()
    {
        MonsterFieldUpgradeLockService.ResetCombat();
        _damageReceivedThisCombat = 0;
        _enemyAttackDamageThisCombat.Clear();
        ClearCombatExtraDeck();
        _extraDeckCombat = Owner.Creature.CombatState;
        for (var index = 0; index < _ownedExtraDeck.Count; index++)
        {
            var entry = _ownedExtraDeck[index].CloneOwned();
            entry.OwnedEntryIndex = index;
            EnsureCombatEntryIdentity(entry);
            _extraDeck.Add(entry);
        }
        RegisterCombatCore();
        CyberSeries.ResetCombat(Owner);
        await ThermalVortexCommandCompat.ApplyPower<SummonRulesPower>(null, Owner.Creature, 1, Owner.Creature, null, false);
        await ThermalVortexCommandCompat.ApplyPower<TurnCardPlayTrackerPower>(null, Owner.Creature, 1, Owner.Creature, null, false);
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        ClearCombatExtraDeck();
        CyberSeries.ResetCombat(Owner);
        return Task.CompletedTask;
    }

    internal static void ClearAllCombatState()
    {
        foreach (var weak in CombatCores)
            if (weak.TryGetTarget(out var core))
                core.ClearCombatExtraDeck();
        CombatCores.Clear();
    }

    private void RegisterCombatCore()
    {
        CombatCores.RemoveAll(weak => !weak.TryGetTarget(out _));
        if (!CombatCores.Any(weak => weak.TryGetTarget(out var core) && ReferenceEquals(core, this)))
            CombatCores.Add(new WeakReference<ThermalVortexCore>(this));
    }

    private void ClearCombatExtraDeck()
    {
        _extraDeck.Clear();
        _trackedExtraDeckSummons.Clear();
        _physicalExtraDeckIdentities.Clear();
        _previewExtraDeckIdentities.Clear();
        _extraDeckCombat = null;
        _nextCombatEntryId = 0;
    }

    private void EnsureCombatEntryIdentity(ExtraDeckEntry entry)
    {
        if (entry is null || entry.CombatEntryId > 0 || _extraDeckCombat is null)
            return;
        entry.Combat = _extraDeckCombat;
        entry.CombatEntryId = ++_nextCombatEntryId;
    }

    private bool IsCurrentCombatEntry(ExtraDeckEntry entry) =>
        entry is { CombatEntryId: > 0 }
        && _extraDeckCombat is not null
        && ReferenceEquals(entry.Combat, _extraDeckCombat)
        && ReferenceEquals(Owner?.Creature?.CombatState, _extraDeckCombat);

    public override async Task AfterCardExhausted(PlayerChoiceContext ctx, CardModel card, bool causedByEthereal)
    {
        if (card is null)
            return;

        if (await TryRestoreTrackedExtraDeckSummon(ctx, card))
            return;

        if (card.Owner == Owner
            && card is XyzMonsterCard { CurrentUpgradeLevel: > 0 } returningMonster
            && card is EvilExodia or ExodiaGuardian)
        {
            await ReturnDepartedMonsterToExtraDeck(returningMonster);
            return;
        }

        if (card.Owner != Owner || GetXyzMonsterType(card) != typeof(CircuitTalismanBeast))
            return;

        var consumedBlock = card is CircuitTalismanBeast talisman
            ? talisman.CurrentConsumedBlock
            : card.CurrentUpgradeLevel > 0
                ? CircuitTalismanBeast.UpgradedConsumedBlock
                : CircuitTalismanBeast.ConsumedBlock;
        await GainCircuitTalismanConsumedBlock(consumedBlock);
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel source)
    {
        if (card is not XyzMonsterCard summon || card.Owner != Owner)
            return;

        if (!_trackedExtraDeckSummons.ContainsKey(summon))
        {
            // Native discard shuffles and other draw-pile returns must also
            // restore committed Extra Deck monsters before the next draw.
            // Hand returns and summon rollback retain their existing rules.
            if (summon.Pile?.Type == PileType.Draw && !summon.IsExtraDeckSummonPlayAuthorized)
                await ReturnDepartedMonsterToExtraDeck(summon);
            return;
        }

        var destinationPileType = summon.Pile?.Type;
        if (destinationPileType == MonsterFieldPile.FieldPileType)
        {
            ClearTrackedExtraDeckSummon(summon);
            return;
        }

        if (summon.IsExtraDeckSummonPlayAuthorized)
            return;

        if (destinationPileType is not null && IsExtraDeckReturnPile(destinationPileType.Value))
            await TryRestoreTrackedExtraDeckSummon(null, summon);
    }

    public Task<bool> XyzSummon(PlayerChoiceContext ctx, bool includeHandMaterials = false) =>
        SummonExtraDeck(
            ctx,
            includeHandMaterials
                ? ExtraDeckSummonMode.FieldAndHand
                : ExtraDeckSummonMode.FieldOnly);

    public Task<bool> LoadFusionSummon(PlayerChoiceContext ctx) =>
        SummonExtraDeck(ctx, ExtraDeckSummonMode.CyberloadFusion);

    public Task<bool> MiracleFusionSummon(PlayerChoiceContext ctx) =>
        SummonExtraDeck(ctx, ExtraDeckSummonMode.MiracleFusion);

    public Task<bool> FusionGateSummon(PlayerChoiceContext ctx) =>
        SummonExtraDeck(ctx, ExtraDeckSummonMode.FusionGate);

    internal Task<bool> MillenniumFusionSummon(PlayerChoiceContext ctx) =>
        SummonExtraDeck(ctx, ExtraDeckSummonMode.FieldAndHand);

    public Task<bool> PrepareFutureFusion(PlayerChoiceContext ctx, int maximumMaterials) =>
        FutureFusionSummon(ctx, maximumMaterials);

    private async Task<bool> SummonExtraDeck(
        PlayerChoiceContext ctx,
        ExtraDeckSummonMode mode)
    {
        var isMaterialUseValid = MonsterFieldService.CaptureMaterialUseValidity(Owner);
        if (!isMaterialUseValid())
            return false;

        var selection = await ChooseExtraDeckSummonBeforeCommit(ctx, mode);
        if (selection is null || !isMaterialUseValid())
            return false;

        var summon = CreateXyzMonster(selection.Entry);
        if (summon is null)
            return false;

        summon.Materials = selection.Materials.Count;
        if (summon is WingedDragonOfRaSphereMode sphereMode)
            sphereMode.CaptureMaterialHealth(selection.Materials);
        PrepareSummonedXyz(summon);
        if (!RemoveExtraDeckEntry(selection.EntryIndex, selection.Entry))
        {
            await EffectTargeting.RemoveUncommittedTransientCard(summon);
            return false;
        }

        TrackExtraDeckSummon(summon, selection.Entry, selection.EntryIndex);

        var summoned = false;
        try
        {
            MillenniumResolution.PrepareSummonCount(summon);
            var materialVisuals = FusionMaterialVisualSnapshot.Capture(selection.Materials);
            using (MonsterFieldService.ReserveCapacitySlots(Owner))
                await MoveMaterials(ctx, selection.Materials, mode, isMaterialUseValid);

            if (!isMaterialUseValid() || !CanPlaySummonedXyz(summon))
                return false;

            var summonTarget = ResolveXyzTargetForPlay(ctx, summon);
            await ThermalVortexCombatVfx.PlaySafeAsync(
                nameof(FusionSummonVfx),
                () => FusionSummonVfx.PlayAsync(summon, materialVisuals, summonTarget));

            if (!isMaterialUseValid())
                return false;

            // Every fusion belongs to the current effect queue. Synchronize its
            // target and wait for field entry without using the local hand UI.
            summoned = await PlayReservedSummonedXyz(ctx, summon, selection.Entry, summonTarget);

            if (summoned && summon is WingedDragonOfRaSphereMode)
                MonsterFieldService.MoveMonsterToLeftByCardEffect(summon);

            return summoned;
        }
        finally
        {
            if (!summoned)
            {
                MillenniumResolution.ClearPreparedCount(summon);
                await TryRestoreTrackedExtraDeckSummon(ctx, summon);
            }
        }
    }

    public async Task<bool> SummonReservedExtraDeck(
        PlayerChoiceContext ctx,
        Type monsterType,
        int upgradeLevel,
        int materials) =>
        await SummonReservedExtraDeck(
            ctx,
            new ExtraDeckEntry(monsterType, upgradeLevel),
            _extraDeck.Count,
            materials,
            restoreEntryOnPreTrackFailure: true);

    public Task<bool> SummonReservedExtraDeck(
        PlayerChoiceContext ctx,
        string serializedEntry,
        int index,
        int materials) =>
        SummonReservedExtraDeck(ctx, serializedEntry, index, materials, ownedEntryIndex: -1);

    internal async Task<bool> SummonReservedExtraDeck(
        PlayerChoiceContext ctx,
        string serializedEntry,
        int index,
        int materials,
        int ownedEntryIndex)
    {
        var entry = DeserializeExtraDeckEntry(serializedEntry);
        if (entry?.MonsterType is null)
            return false;

        entry.OwnedEntryIndex = ownedEntryIndex;

        return await SummonReservedExtraDeck(
            ctx,
            entry,
            index,
            materials,
            restoreEntryOnPreTrackFailure: true);
    }

    internal Task<bool> SummonReservedExtraDeck(PlayerChoiceContext ctx, ExtraDeckReservation reservation)
    {
        var entry = CreateEntryFromReservation(reservation);
        return entry is null
            ? Task.FromResult(false)
            : SummonReservedExtraDeck(ctx, entry, reservation.Index, reservation.Materials,
                restoreEntryOnPreTrackFailure: true, materialVisuals: reservation.MaterialVisuals);
    }

    internal bool IsCurrentReservation(ExtraDeckReservation reservation) =>
        reservation is { CombatEntryId: > 0 }
        && _extraDeckCombat is not null
        && ReferenceEquals(reservation.Combat, _extraDeckCombat)
        && ReferenceEquals(Owner?.Creature?.CombatState, _extraDeckCombat);

    internal void RestoreReservedExtraDeck(ExtraDeckReservation reservation)
    {
        var entry = CreateEntryFromReservation(reservation);
        if (entry is not null)
            RestoreExtraDeckEntry(reservation.Index, entry);
    }

    private ExtraDeckEntry CreateEntryFromReservation(ExtraDeckReservation reservation)
    {
        if (!IsCurrentReservation(reservation))
            return null;
        var entry = DeserializeExtraDeckEntry(reservation.SerializedEntry);
        if (entry is null)
            return null;
        entry.OwnedEntryIndex = reservation.OwnedEntryIndex;
        entry.CombatEntryId = reservation.CombatEntryId;
        entry.Combat = reservation.Combat;
        entry.DevourSnapshot = reservation.DevourSnapshot?.DeepClone();
        return entry;
    }

    private async Task<bool> SummonReservedExtraDeck(
        PlayerChoiceContext ctx,
        ExtraDeckEntry entry,
        int index,
        int materials,
        bool restoreEntryOnPreTrackFailure,
        FusionMaterialVisualSnapshot materialVisuals = null)
    {
        EnsureCombatEntryIdentity(entry);
        if (!IsCurrentCombatEntry(entry))
            return false;

        XyzMonsterCard summon = null;
        var tracked = false;
        var played = false;
        try
        {
            if (entry.MonsterType == typeof(Relinquished) || !CanChooseXyzType(entry.MonsterType))
                return false;

            summon = CreateXyzMonster(entry);
            if (summon is null)
                return false;

            var reservedMaterialCount = Math.Max(summon.MinimumMaterials, materials);
            if (reservedMaterialCount > summon.MaximumMaterials)
                return false;

            var canPlay = false;
            try
            {
                summon.Materials = reservedMaterialCount;
                PrepareSummonedXyz(summon);
                canPlay = CanPlaySummonedXyz(summon);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info(
                    $"Reserved ExtraDeck summon preflight failed card={summon.GetType().Name} error={ex}");
            }

            if (!canPlay)
                return false;

            MillenniumResolution.PrepareSummonCount(summon);
            TrackExtraDeckSummon(summon, entry, index);
            tracked = true;
            var summonTarget = ResolveXyzTargetForPlay(ctx, summon);
            await ThermalVortexCombatVfx.PlaySafeAsync(
                nameof(FusionSummonVfx),
                () => FusionSummonVfx.PlayAsync(summon, materialVisuals, summonTarget));

            if (!IsCurrentCombatEntry(entry))
                return false;
            played = await PlayReservedSummonedXyz(ctx, summon, entry, summonTarget);
            if (played && summon is WingedDragonOfRaSphereMode)
                MonsterFieldService.MoveMonsterToLeftByCardEffect(summon);
            return played;
        }
        finally
        {
            if (!played)
                MillenniumResolution.ClearPreparedCount(summon);
            if (tracked && !played)
                await TryRestoreTrackedExtraDeckSummon(ctx, summon);
            else if (!tracked && summon is not null)
                await FailReservedExtraDeckSummon(summon, entry, index, restoreEntryOnPreTrackFailure);
            else if (!tracked)
                FailReservedExtraDeckSummon(entry, index, restoreEntryOnPreTrackFailure);
        }
    }

    private bool FailReservedExtraDeckSummon(
        ExtraDeckEntry entry,
        int index,
        bool restoreEntry)
    {
        if (restoreEntry)
            RestoreExtraDeckEntry(index, entry);

        return false;
    }

    private async Task<bool> FailReservedExtraDeckSummon(
        XyzMonsterCard summon,
        ExtraDeckEntry entry,
        int index,
        bool restoreEntry)
    {
        try
        {
            await EffectTargeting.RemoveUncommittedTransientCard(summon);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info(
                $"Reserved ExtraDeck summon cleanup failed card={summon?.GetType().Name ?? "null"} error={ex}");
        }

        if (summon.HasBeenRemovedFromState)
        {
            _physicalExtraDeckIdentities.Remove(summon);
            CyberDevourState.ReleaseCombatState(summon);
            FailReservedExtraDeckSummon(entry, index, restoreEntry);
        }
        else if (restoreEntry && IsCurrentCombatEntry(entry))
        {
            // Keep the live entity's return ticket if host cleanup failed;
            // restoring a virtual entry too would duplicate the same monster.
            TrackExtraDeckSummon(summon, entry, index);
        }

        return false;
    }

    public Task<bool> ConsumeExtraDeckCard() =>
        ConsumeExtraDeckCard(new BlockingPlayerChoiceContext());

    public async Task<bool> ConsumeExtraDeckCard(PlayerChoiceContext ctx)
    {
        var target = _extraDeck.FirstOrDefault();
        if (target is null)
            return false;

        return await ConsumeExtraDeckEntry(ctx, _extraDeck.IndexOf(target), target);
    }

    public Task<bool> ChooseAndConsumeExtraDeckCard(PlayerChoiceContext ctx) =>
        ChooseAndConsumeExtraDeckCard(ctx, null);

    internal IReadOnlyList<XyzMonsterCard> GetExtraDeckConsumeChoices() =>
        BuildExtraDeckConsumeChoices();

    internal async Task<bool> ConsumeSelectedExtraDeckCard(
        PlayerChoiceContext ctx,
        XyzMonsterCard selected,
        Func<bool> canCommit)
    {
        if (selected is null
            || !TryGetCombatExtraDeckEntry(selected, out var index, out var entry)
            || (canCommit is not null && !canCommit()))
        {
            return false;
        }

        return await ConsumeExtraDeckEntry(ctx, index, entry, canCommit);
    }

    internal async Task<bool> ChooseAndConsumeExtraDeckCard(
        PlayerChoiceContext ctx,
        Func<bool> canCommit)
    {
        var choices = BuildExtraDeckConsumeChoices();
        if (choices.Count == 0)
            return false;

        var chosen = choices.Count == 1 || IsAutomatedChoiceContext(ctx)
            ? choices[0]
            : await ChooseExtraDeckCardToConsume(ctx, choices);

        return await ConsumeSelectedExtraDeckCard(ctx, chosen, canCommit);
    }

    public Task<int> ConsumeRandomExtraDeckCards(int count) =>
        ConsumeRandomExtraDeckCards(new BlockingPlayerChoiceContext(), count);

    public async Task<int> ConsumeRandomExtraDeckCards(PlayerChoiceContext ctx, int count)
    {
        if (count <= 0 || _extraDeck.Count < count)
            return 0;

        var rng = Owner?.RunState?.Rng?.CombatCardSelection;
        if (rng is null)
            return 0;

        var consumed = 0;
        var consumedEntryIds = new HashSet<long>();
        for (var i = 0; i < count; i++)
        {
            // An exhaust effect may immediately return this same entry. A
            // multi-card payment still consumes distinct combat identities.
            var candidates = _extraDeck
                .Where(entry => !consumedEntryIds.Contains(entry.CombatEntryId))
                .ToList();
            if (candidates.Count == 0)
                break;

            var target = rng.NextItem(candidates);
            if (target is null
                || !await ConsumeExtraDeckEntry(ctx, _extraDeck.IndexOf(target), target))
            {
                break;
            }

            consumedEntryIds.Add(target.CombatEntryId);
            consumed++;
        }

        return consumed;
    }

    public async Task<int> ChooseAndConsumeExtraDeckMonsterAboveHp(
        PlayerChoiceContext ctx,
        int minimumHp,
        Func<int, bool> canConsume = null)
    {
        var choices = BuildExtraDeckChoicesByHp(minimumHp);
        if (choices.Count == 0)
            return 0;

        var chosen = choices.Count == 1 || IsAutomatedChoiceContext(ctx)
            ? choices[0]
            : await ChooseExtraDeckCardToConsume(ctx, choices);

        if (chosen is null || !TryGetCombatExtraDeckEntry(chosen, out var index, out var entry))
            return 0;

        var hp = MonsterFieldHealthService.GetInitialMaxHp(chosen);
        if (hp <= minimumHp || canConsume?.Invoke(hp) == false)
            return 0;

        if (!await ConsumeExtraDeckEntry(ctx, index, entry, () => canConsume?.Invoke(hp) != false))
            return 0;

        return hp;
    }

    public bool HasExtraDeckMonsterAboveHp(int minimumHp) =>
        BuildExtraDeckChoicesByHp(minimumHp).Count > 0;

    private async Task<bool> FutureFusionSummon(PlayerChoiceContext ctx, int maximumMaterials)
    {
        var isMaterialUseValid = MonsterFieldService.CaptureMaterialUseValidity(Owner);
        if (!isMaterialUseValid())
            return false;

        var boundedMaximum = Math.Max(1, maximumMaterials);
        var selection = await ChooseExtraDeckSummonBeforeCommit(
            ctx,
            ExtraDeckSummonMode.FutureFusion,
            boundedMaximum);
        if (selection is null || !isMaterialUseValid())
            return false;

        var serializedEntry = SerializeExtraDeckEntry(selection.Entry);
        if (string.IsNullOrWhiteSpace(serializedEntry)
            || !RemoveExtraDeckEntry(selection.EntryIndex, selection.Entry))
            return false;

        var reservation = new ExtraDeckReservation(
            serializedEntry,
            selection.EntryIndex,
            selection.Materials.Count,
            selection.Entry.OwnedEntryIndex,
            selection.Entry.CombatEntryId,
            selection.Entry.Combat,
            selection.Entry.DevourSnapshot,
            FusionMaterialVisualSnapshot.Capture(selection.Materials));
        var reservationBound = false;
        try
        {
            await MonsterFieldService.SendToExhaust(
                ctx, selection.Materials, this, MonsterFieldLeaveReason.FusionMaterial);

            if (!isMaterialUseValid())
                return false;

            await ThermalVortexCommandCompat.ApplyPower<FutureFusionPower>(ctx, Owner.Creature, 1, Owner.Creature, null, false);
            if (!isMaterialUseValid())
                return false;

            var power = Owner.Creature.GetPower<FutureFusionPower>();
            reservationBound = power?.Bind(reservation) == true;
            return reservationBound;
        }
        finally
        {
            // A new combat rebuilds the Extra Deck; an old interrupted payment
            // must not insert its entry into that new deck a second time.
            if (!reservationBound)
                RestoreReservedExtraDeck(reservation);
        }
    }

    private bool CanFutureFusionSummon(int maximumMaterials)
    {
        return BuildXyzChoices(
            ExtraDeckSummonMode.FutureFusion,
            Math.Max(1, maximumMaterials)).Count > 0;
    }

    public override bool TryModifyCardRewardOptions(
        Player player,
        List<CardCreationResult> options,
        CardCreationOptions creationOptions)
    {
        return false;
    }

    public override bool TryModifyRewards(Player player, List<Reward> rewards, AbstractRoom room)
    {
        if (player != Owner
            || room is not CombatRoom
            || !CanAcceptExtraDeckReward)
            return false;

        if (player.RunState is not RunState runState)
            return false;

        if (rewards.OfType<CardReward>().Any(IsExtraDeckReward))
            return false;

        var sourceReward = rewards.OfType<CardReward>().FirstOrDefault();
        var synchronizer = TryGetSynchronizer(sourceReward);
        if (synchronizer is null)
            return false;

        var creationOptions = CardCreationOptions.ForRoom(player, room.RoomType);
        var extraDeckCards = CreateXyzRewardCards(runState, player, creationOptions, ExtraDeckRewardOptionCount);
        if (extraDeckCards.Count < ExtraDeckRewardOptionCount)
            return false;

        var reward = new CardReward(extraDeckCards, CardCreationSource.Encounter, player, creationOptions, synchronizer)
        {
            CanReroll = false
        };

        rewards.Add(reward);
        return true;
    }

    private static bool IsExtraDeckReward(CardReward reward)
    {
        var cards = reward.Cards?.ToList();
        return cards is { Count: > 0 } && cards.All(IsExtraDeckDisplayCard);
    }

    private static PlayerChoiceSynchronizer TryGetSynchronizer(CardReward reward) =>
        reward is null ? null : CardRewardSynchronizerField?.GetValue(reward) as PlayerChoiceSynchronizer;

    private IReadOnlyList<CardModel> CreateXyzRewardCards(
        RunState runState,
        Player player,
        CardCreationOptions creationOptions,
        int count)
    {
        var cards = new List<CardModel>();
        var candidates = RewardPoolCatalog
            .GetExtraCandidates()
            .Where(IsExtraDeckRewardAllowed)
            .ToList();
        var rng = creationOptions.RngOverride ?? player.PlayerRng.Rewards;

        while (cards.Count < count && candidates.Count > 0)
        {
            var selected = rng?.NextItem(candidates) ?? candidates[0];
            var card = runState.CreateCard(selected, player);
            candidates.Remove(selected);
            if (card is not null)
                cards.Add(card);
        }

        return cards;
    }

    public override bool ShouldAddToDeck(CardModel card)
    {
        if (card is null || !ReferenceEquals(card.Owner, Owner))
            return true;

        var cardType = GetXyzMonsterType(card);
        if (cardType is null)
            return true;

        if (CanAcceptExtraDeckReward)
            _ownedExtraDeck.Add(CreateExtraDeckEntryFromCard(cardType, card));

        return false;
    }

    public static bool IsExtraDeckDisplayCard(CardModel card) =>
        GetXyzMonsterType(card) is not null;

    internal static bool IsXyzMonsterDisplayCard(CardModel card) =>
        GetXyzMonsterType(card) is not null;

    public static bool IsXyzSummonCommand(CardModel card) =>
        card is XyzSummon || card.Id.Entry == ModelDb.Card<XyzSummon>().Id.Entry;

    private static bool CanPlaySummonedXyz(XyzMonsterCard summon)
    {
        if (summon is null)
            return false;

        var wasAuthorized = summon.IsExtraDeckSummonPlayAuthorized;
        if (!wasAuthorized)
            summon.AuthorizeExtraDeckSummonPlay();

        try
        {
            return CanPlayAuthorizedSummonedXyz(summon);
        }
        finally
        {
            if (!wasAuthorized)
                summon.ClearExtraDeckSummonPlayAuthorization();
        }
    }

    private static bool CanPlayAuthorizedSummonedXyz(XyzMonsterCard summon) =>
        summon is { IsExtraDeckSummonPlayAuthorized: true }
        && MonsterFieldService.CanPlaceOnField(summon)
        && EffectTargeting.CanPlayImmediately(
            summon.Owner,
            summon,
            excludeFromCardPlayCount: true);

    private async Task<bool> PlayReservedSummonedXyz(
        PlayerChoiceContext ctx,
        XyzMonsterCard summon,
        ExtraDeckEntry entry,
        Creature preferredTarget)
    {
        var enteredField = false;
        bool IsOperationValid() =>
            IsCurrentCombatEntry(entry)
            && Owner?.Creature is { IsAlive: true }
            && CombatManager.Instance?.IsOverOrEnding != true
            && ReferenceEquals(summon.Owner, Owner)
            && !summon.HasBeenRemovedFromState
            && _trackedExtraDeckSummons.TryGetValue(summon, out var state)
            && state.Entry.CombatEntryId == entry.CombatEntryId
            && ReferenceEquals(state.Entry.Combat, entry.Combat);

        void MarkFieldEntry(MonsterFieldEnterEvent enterEvent)
        {
            if (!ReferenceEquals(enterEvent.Card, summon) || !IsCurrentCombatEntry(entry))
                return;
            // Entry commits the reservation even if an entry listener immediately
            // removes this monster. Its old return ticket must no longer apply.
            enteredField = true;
            ClearTrackedExtraDeckSummon(summon);
        }

        summon.AuthorizeExtraDeckSummonPlay();
        MonsterFieldEventService.MonsterEnteredVisual += MarkFieldEntry;
        try
        {
            await EffectTargeting.PlayImmediately(
                ctx,
                Owner,
                summon,
                preferredTarget,
                prepare: async () =>
                {
                    // The hand may already be full after material hooks or at
                    // turn start. Select a synchronized target first, then stage
                    // exactly once in Play without requiring a local hand holder.
                    await ThermalVortexCommandCompat.AddGeneratedCardToCombat(
                        summon, PileType.Play, Owner, CardPilePosition.Top);
                },
                canPrepare: () => CanPlayAuthorizedSummonedXyz(summon),
                excludeFromCardPlayCount: true,
                canExecute: () => summon.Pile?.Type == PileType.Play
                    && CanPlayAuthorizedSummonedXyz(summon),
                isOperationValid: IsOperationValid);
            return enteredField;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"ExtraDeck summon play failed card={summon.GetType().Name} enteredField={enteredField} error={ex}");
            return enteredField;
        }
        finally
        {
            MonsterFieldEventService.MonsterEnteredVisual -= MarkFieldEntry;
            summon.ClearExtraDeckSummonPlayAuthorization();
            // The caller owns failure cleanup and restores the same entry only
            // after its transient entity has actually been removed.
        }
    }

    private static void PrepareSummonedXyz(XyzMonsterCard summon)
    {
        summon.PrepareForExtraDeckPlayCost();
        summon.WasExtraSummonedThisTurn = true;
    }

    private XyzMonsterCard CreateXyzMonster(ExtraDeckEntry entry)
    {
        EnsureCombatEntryIdentity(entry);
        if (!IsCurrentCombatEntry(entry))
            return null;
        var card = CreateXyzMonster(entry.MonsterType);
        if (card is not null)
        {
            try
            {
                ApplyEntryState(card, entry);
                CyberDevourState.RestoreCombatSnapshot(card, entry.DevourSnapshot);
                _physicalExtraDeckIdentities[card] = new ExtraDeckIdentity(entry.Combat, entry.CombatEntryId);
            }
            catch
            {
                // Initialization has not exposed the card to a pile yet.
                // Let the caller restore its entry without leaving a second
                // physical entity behind if applying its state failed.
                card.RemoveFromState();
                CyberDevourState.ReleaseCombatState(card);
                throw;
            }
        }

        return card;
    }

    private XyzMonsterCard CreateXyzMonster(Type type)
    {
        if (type == typeof(Vortex))
            return Owner.Creature.CombatState.CreateCard<Vortex>(Owner);

        if (type == typeof(Detonation))
            return Owner.Creature.CombatState.CreateCard<Detonation>(Owner);

        if (type == typeof(CircuitTalismanBeast))
            return Owner.Creature.CombatState.CreateCard<CircuitTalismanBeast>(Owner);

        if (type == typeof(FullArmorThunderLance))
            return Owner.Creature.CombatState.CreateCard<FullArmorThunderLance>(Owner);

        if (type == typeof(DormantMagneticFieldBeast))
            return Owner.Creature.CombatState.CreateCard<DormantMagneticFieldBeast>(Owner);

        if (type == typeof(BrilliantRebootKnight))
            return Owner.Creature.CombatState.CreateCard<BrilliantRebootKnight>(Owner);

        if (type == typeof(DivineArsenalFurnaceGod))
            return Owner.Creature.CombatState.CreateCard<DivineArsenalFurnaceGod>(Owner);

        if (type == typeof(ChimeratechOverdragon))
            return Owner.Creature.CombatState.CreateCard<ChimeratechOverdragon>(Owner);

        if (type == typeof(Relinquished))
            return Owner.Creature.CombatState.CreateCard<Relinquished>(Owner);

        if (type == typeof(CyberEndDragon))
            return Owner.Creature.CombatState.CreateCard<CyberEndDragon>(Owner);

        if (type == typeof(CyberDragonInfinity))
            return Owner.Creature.CombatState.CreateCard<CyberDragonInfinity>(Owner);

        if (type == typeof(WingedDragonOfRaSphereMode))
            return Owner.Creature.CombatState.CreateCard<WingedDragonOfRaSphereMode>(Owner);

        if (type == typeof(MillenniumGrandThief))
            return Owner.Creature.CombatState.CreateCard<MillenniumGrandThief>(Owner);

        if (type == typeof(ExodiaSummoner))
            return Owner.Creature.CombatState.CreateCard<ExodiaSummoner>(Owner);

        if (type == typeof(MillenniumMasterKey))
            return Owner.Creature.CombatState.CreateCard<MillenniumMasterKey>(Owner);

        if (type == typeof(EvilExodia))
            return Owner.Creature.CombatState.CreateCard<EvilExodia>(Owner);

        if (type == typeof(ExodiaGuardian))
            return Owner.Creature.CombatState.CreateCard<ExodiaGuardian>(Owner);

        return null;
    }

    private static Type GetXyzMonsterType(CardModel card)
    {
        if (card is Vortex)
            return typeof(Vortex);

        if (card is Detonation)
            return typeof(Detonation);

        if (card is CircuitTalismanBeast)
            return typeof(CircuitTalismanBeast);

        if (card is FullArmorThunderLance)
            return typeof(FullArmorThunderLance);

        if (card is DormantMagneticFieldBeast)
            return typeof(DormantMagneticFieldBeast);

        if (card is BrilliantRebootKnight)
            return typeof(BrilliantRebootKnight);

        if (card is DivineArsenalFurnaceGod)
            return typeof(DivineArsenalFurnaceGod);

        if (card is ChimeratechOverdragon)
            return typeof(ChimeratechOverdragon);

        if (card is Relinquished)
            return typeof(Relinquished);

        if (card is CyberEndDragon)
            return typeof(CyberEndDragon);

        if (card is CyberDragonInfinity)
            return typeof(CyberDragonInfinity);

        if (card is WingedDragonOfRaSphereMode)
            return typeof(WingedDragonOfRaSphereMode);

        if (card is MillenniumGrandThief)
            return typeof(MillenniumGrandThief);

        if (card is ExodiaSummoner)
            return typeof(ExodiaSummoner);

        if (card is MillenniumMasterKey)
            return typeof(MillenniumMasterKey);

        if (card is EvilExodia)
            return typeof(EvilExodia);

        if (card is ExodiaGuardian)
            return typeof(ExodiaGuardian);

        if (card.Id.Entry == ModelDb.Card<Vortex>().Id.Entry)
            return typeof(Vortex);

        if (card.Id.Entry == ModelDb.Card<Detonation>().Id.Entry)
            return typeof(Detonation);

        if (card.Id.Entry == ModelDb.Card<CircuitTalismanBeast>().Id.Entry)
            return typeof(CircuitTalismanBeast);

        if (card.Id.Entry == ModelDb.Card<FullArmorThunderLance>().Id.Entry)
            return typeof(FullArmorThunderLance);

        if (card.Id.Entry == ModelDb.Card<DormantMagneticFieldBeast>().Id.Entry)
            return typeof(DormantMagneticFieldBeast);

        if (card.Id.Entry == ModelDb.Card<BrilliantRebootKnight>().Id.Entry)
            return typeof(BrilliantRebootKnight);

        if (card.Id.Entry == ModelDb.Card<DivineArsenalFurnaceGod>().Id.Entry)
            return typeof(DivineArsenalFurnaceGod);

        if (card.Id.Entry == ModelDb.Card<ChimeratechOverdragon>().Id.Entry)
            return typeof(ChimeratechOverdragon);

        if (card.Id.Entry == ModelDb.Card<Relinquished>().Id.Entry)
            return typeof(Relinquished);

        if (card.Id.Entry == ModelDb.Card<CyberEndDragon>().Id.Entry)
            return typeof(CyberEndDragon);

        if (card.Id.Entry == ModelDb.Card<CyberDragonInfinity>().Id.Entry)
            return typeof(CyberDragonInfinity);

        if (card.Id.Entry == ModelDb.Card<WingedDragonOfRaSphereMode>().Id.Entry)
            return typeof(WingedDragonOfRaSphereMode);

        if (card.Id.Entry == ModelDb.Card<MillenniumGrandThief>().Id.Entry)
            return typeof(MillenniumGrandThief);

        if (card.Id.Entry == ModelDb.Card<ExodiaSummoner>().Id.Entry)
            return typeof(ExodiaSummoner);

        if (card.Id.Entry == ModelDb.Card<MillenniumMasterKey>().Id.Entry)
            return typeof(MillenniumMasterKey);

        if (card.Id.Entry == ModelDb.Card<EvilExodia>().Id.Entry)
            return typeof(EvilExodia);

        if (card.Id.Entry == ModelDb.Card<ExodiaGuardian>().Id.Entry)
            return typeof(ExodiaGuardian);

        return null;
    }

    private static int MinimumMaterials(Type type) =>
        type switch
        {
            _ when type == typeof(Vortex) => Vortex.RequiredMaterials,
            _ when type == typeof(Detonation) => Detonation.RequiredMaterials,
            _ when type == typeof(CircuitTalismanBeast) => CircuitTalismanBeast.RequiredMaterials,
            _ when type == typeof(FullArmorThunderLance) => FullArmorThunderLance.RequiredMaterials,
            _ when type == typeof(DormantMagneticFieldBeast) => DormantMagneticFieldBeast.MinimumRequiredMaterials,
            _ when type == typeof(BrilliantRebootKnight) => BrilliantRebootKnight.RequiredMaterials,
            _ when type == typeof(DivineArsenalFurnaceGod) => DivineArsenalFurnaceGod.RequiredMaterials,
            _ when type == typeof(ChimeratechOverdragon) => ChimeratechOverdragon.RequiredMaterials,
            _ when type == typeof(Relinquished) => Relinquished.RequiredMaterials,
            _ when type == typeof(CyberEndDragon) => CyberEndDragon.RequiredMaterials,
            _ when type == typeof(CyberDragonInfinity) => CyberDragonInfinity.RequiredMaterials,
            _ when type == typeof(WingedDragonOfRaSphereMode) => 1,
            _ when type == typeof(MillenniumGrandThief) => 2,
            _ when type == typeof(ExodiaSummoner) => 2,
            _ when type == typeof(MillenniumMasterKey) => 2,
            _ when type == typeof(EvilExodia) => 2,
            _ when type == typeof(ExodiaGuardian) => 2,
            _ => int.MaxValue
        };

    private IEnumerable<ExtraDeckEntry> GetDisplayRemainingExtraDeck() =>
        IsCombatExtraDeckPrepared() ? _extraDeck : _ownedExtraDeck;

    internal bool IsOwnedExtraDeckEntryAvailableForDisplay(int index) =>
        index >= 0
        && index < _ownedExtraDeck.Count
        && (!IsCombatExtraDeckPrepared() || _extraDeck.Any(entry => entry.OwnedEntryIndex == index));

    private bool IsCombatExtraDeckPrepared()
    {
        try
        {
            var combatManager = CombatManager.Instance;
            return Owner?.Creature?.CombatState is not null
                && combatManager is not null
                && (combatManager.IsInProgress || combatManager.IsStarting)
                && !combatManager.IsOverOrEnding;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<Type, int> CountExtraDeck(IEnumerable<ExtraDeckEntry> deck) =>
        deck.GroupBy(entry => entry.MonsterType).ToDictionary(group => group.Key, group => group.Count());

    private static int RemainingExtraDeckCount(Type type, int ownedCount, IReadOnlyDictionary<Type, int> remainingCounts)
    {
        var remainingCount = remainingCounts.TryGetValue(type, out var count) ? count : 0;
        return Math.Clamp(remainingCount, 0, ownedCount);
    }

    private static string SerializeExtraDeck(IEnumerable<ExtraDeckEntry> deck) =>
        string.Join(SaveSeparator, deck.Select(SerializeExtraDeckEntry).Where(entry => entry is not null));

    private static string SerializeExtraDeckEntry(ExtraDeckEntry entry)
    {
        var saveId = GetXyzMonsterSaveId(entry.MonsterType);
        if (saveId is null)
        {
            if (HasSerializableEnchantment(entry.Enchantment))
                throw new InvalidDataException($"Cannot save unknown enchanted Extra Deck monster type '{entry.MonsterType}'.");
            return null;
        }

        var upgrade = Math.Max(0, entry.UpgradeLevel);
        if (!HasSerializableEnchantment(entry.Enchantment))
        {
            return upgrade > 0
                ? $"{saveId}{UpgradeSeparator}{upgrade}"
                : saveId;
        }

        var serializedEnchantment = SerializeEnchantment(entry.Enchantment);
        return $"{saveId}{UpgradeSeparator}{upgrade}{UpgradeSeparator}{serializedEnchantment}";
    }

    private static List<ExtraDeckEntry> DeserializeExtraDeck(string save)
    {
        var entries = new List<ExtraDeckEntry>();
        if (string.IsNullOrWhiteSpace(save))
            return entries;

        foreach (var savedEntry in save.Split(SaveSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var entry = DeserializeExtraDeckEntry(savedEntry);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries;
    }

    private static ExtraDeckEntry DeserializeExtraDeckEntry(string savedEntry)
    {
        if (string.IsNullOrWhiteSpace(savedEntry))
            return null;

        var parts = savedEntry.Split(UpgradeSeparator, 3, StringSplitOptions.TrimEntries);
        // Preserve the old compatibility rules only for unenchanted entries.
        // Decode first so skipping an unknown card cannot hide an unreadable payload.
        var enchantment = DeserializeEnchantment(parts);
        var type = GetXyzMonsterTypeFromSaveId(parts[0]);
        if (type is null)
        {
            if (HasSerializableEnchantment(enchantment))
                throw new InvalidDataException($"The Extra Deck save contains unknown enchanted monster '{parts[0]}'.");
            return null;
        }
        return new ExtraDeckEntry(type, ParseUpgradeLevel(parts, HasSerializableEnchantment(enchantment)), enchantment);
    }

    private static int ParseUpgradeLevel(IReadOnlyList<string> parts, bool hasEnchantment)
    {
        if (parts.Count < 2)
            return 0;
        if (!int.TryParse(parts[1], out var upgradeLevel) || upgradeLevel < 0)
        {
            if (hasEnchantment)
                throw new InvalidDataException("The enchanted Extra Deck entry contains an invalid upgrade level.");
            return 0;
        }
        return upgradeLevel;
    }

    private static SerializableEnchantment DeserializeEnchantment(IReadOnlyList<string> parts)
    {
        if (parts.Count < 3 || string.IsNullOrWhiteSpace(parts[2]))
            return null;

        return DeserializeEnchantment(parts[2]);
    }

    private static string SerializeEnchantment(SerializableEnchantment enchantment)
    {
        if (!HasSerializableEnchantment(enchantment))
            return null;

        ValidateSerializableEnchantment(enchantment);
        // Native JSON stores stable model IDs and every SavedProperties field,
        // including nested cards. Network packet IDs depend on the loaded model
        // table and must never be used for a save that outlives this process.
        var json = JsonSerializer.Serialize(
            enchantment, JsonSerializationUtility.GetTypeInfo<SerializableEnchantment>());
        return EnchantmentJsonPrefix + Convert.ToBase64String(EnchantmentEncoding.GetBytes(json));
    }

    private static SerializableEnchantment DeserializeEnchantment(string serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
            return null;

        if (!serialized.StartsWith(EnchantmentJsonPrefix, StringComparison.Ordinal))
            throw new InvalidDataException(
                "The Extra Deck contains a legacy or unsupported enchantment format. "
                + "Legacy network IDs cannot be migrated without their original model mapping; "
                + "the saved enchantment has not been discarded or guessed.");

        var json = EnchantmentEncoding.GetString(
            Convert.FromBase64String(serialized[EnchantmentJsonPrefix.Length..]));
        var enchantment = JsonSerializer.Deserialize(
            json, JsonSerializationUtility.GetTypeInfo<SerializableEnchantment>());
        ValidateSerializableEnchantment(enchantment);
        return enchantment;
    }

    private static void ValidateSerializableEnchantment(SerializableEnchantment enchantment)
    {
        if (enchantment?.Id is null
            || !string.Equals(enchantment.Id.Category, ModelId.SlugifyCategory<EnchantmentModel>(), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(enchantment.Id.Entry))
        {
            throw new InvalidDataException("The Extra Deck enchantment is missing a valid stable model ID.");
        }
    }

    private static SerializableEnchantment CloneSerializableEnchantment(SerializableEnchantment enchantment)
    {
        var serialized = SerializeEnchantment(enchantment);
        return string.IsNullOrWhiteSpace(serialized)
            ? null
            : DeserializeEnchantment(serialized);
    }

    private static bool HasSerializableEnchantment(SerializableEnchantment enchantment) =>
        enchantment is not null;

    private static string GetEnchantmentKey(SerializableEnchantment enchantment) =>
        SerializeEnchantment(enchantment) ?? string.Empty;

    private void ReplaceOwnedExtraDeck(IEnumerable<ExtraDeckEntry> deck, bool allowEmpty = false)
    {
        // Validate every payload before applying the old limit for unenchanted
        // entries. An enchanted overflow must never be silently truncated.
        var parsedDeck = deck.Select(entry => entry.CloneOwned()).ToList();
        if (parsedDeck.Skip(MaxOwnedExtraDeckCards).Any(entry => HasSerializableEnchantment(entry.Enchantment)))
            throw new InvalidDataException("The Extra Deck save has enchanted entries beyond the owned card limit; no entries were discarded.");
        var loadedDeck = parsedDeck.Take(MaxOwnedExtraDeckCards).ToList();
        if (loadedDeck.Count == 0 && !allowEmpty)
            loadedDeck.Add(new ExtraDeckEntry(typeof(Detonation), 0));

        _ownedExtraDeck = loadedDeck;
    }

    private static string GetXyzMonsterSaveId(Type type) =>
        type switch
        {
            _ when type == typeof(Vortex) => VortexSaveId,
            _ when type == typeof(Detonation) => DetonationSaveId,
            _ when type == typeof(CircuitTalismanBeast) => CircuitTalismanBeastSaveId,
            _ when type == typeof(FullArmorThunderLance) => FullArmorThunderLanceSaveId,
            _ when type == typeof(DormantMagneticFieldBeast) => DormantMagneticFieldBeastSaveId,
            _ when type == typeof(BrilliantRebootKnight) => BrilliantRebootKnightSaveId,
            _ when type == typeof(DivineArsenalFurnaceGod) => DivineArsenalFurnaceGodSaveId,
            _ when type == typeof(ChimeratechOverdragon) => ChimeratechOverdragonSaveId,
            _ when type == typeof(Relinquished) => RelinquishedSaveId,
            _ when type == typeof(CyberEndDragon) => CyberEndDragonSaveId,
            _ when type == typeof(CyberDragonInfinity) => CyberDragonInfinitySaveId,
            _ when type == typeof(WingedDragonOfRaSphereMode) => WingedDragonOfRaSphereModeSaveId,
            _ when type == typeof(MillenniumGrandThief) => MillenniumGrandThiefSaveId,
            _ when type == typeof(ExodiaSummoner) => ExodiaSummonerSaveId,
            _ when type == typeof(MillenniumMasterKey) => MillenniumMasterKeySaveId,
            _ when type == typeof(EvilExodia) => EvilExodiaSaveId,
            _ when type == typeof(ExodiaGuardian) => ExodiaGuardianSaveId,
            _ => null
        };

    private static Type GetXyzMonsterTypeFromSaveId(string saveId) =>
        saveId switch
        {
            VortexSaveId => typeof(Vortex),
            DetonationSaveId => typeof(Detonation),
            CircuitTalismanBeastSaveId => typeof(CircuitTalismanBeast),
            FullArmorThunderLanceSaveId => typeof(FullArmorThunderLance),
            DormantMagneticFieldBeastSaveId => typeof(DormantMagneticFieldBeast),
            BrilliantRebootKnightSaveId => typeof(BrilliantRebootKnight),
            DivineArsenalFurnaceGodSaveId => typeof(DivineArsenalFurnaceGod),
            ChimeratechOverdragonSaveId => typeof(ChimeratechOverdragon),
            RelinquishedSaveId => typeof(Relinquished),
            CyberEndDragonSaveId => typeof(CyberEndDragon),
            CyberDragonInfinitySaveId => typeof(CyberDragonInfinity),
            WingedDragonOfRaSphereModeSaveId => typeof(WingedDragonOfRaSphereMode),
            MillenniumGrandThiefSaveId => typeof(MillenniumGrandThief),
            ExodiaSummonerSaveId => typeof(ExodiaSummoner),
            MillenniumMasterKeySaveId => typeof(MillenniumMasterKey),
            EvilExodiaSaveId => typeof(EvilExodia),
            ExodiaGuardianSaveId => typeof(ExodiaGuardian),
            _ => null
        };

    private static CardModel CreateXyzRewardCard(RunState runState, Player player, Type type) =>
        type switch
        {
            _ when type == typeof(Vortex) => runState.CreateCard<Vortex>(player),
            _ when type == typeof(Detonation) => runState.CreateCard<Detonation>(player),
            _ when type == typeof(CircuitTalismanBeast) => runState.CreateCard<CircuitTalismanBeast>(player),
            _ when type == typeof(FullArmorThunderLance) => runState.CreateCard<FullArmorThunderLance>(player),
            _ when type == typeof(DormantMagneticFieldBeast) => runState.CreateCard<DormantMagneticFieldBeast>(player),
            _ when type == typeof(BrilliantRebootKnight) => runState.CreateCard<BrilliantRebootKnight>(player),
            _ when type == typeof(DivineArsenalFurnaceGod) => runState.CreateCard<DivineArsenalFurnaceGod>(player),
            _ when type == typeof(ChimeratechOverdragon) => runState.CreateCard<ChimeratechOverdragon>(player),
            _ when type == typeof(Relinquished) => runState.CreateCard<Relinquished>(player),
            _ when type == typeof(CyberEndDragon) => runState.CreateCard<CyberEndDragon>(player),
            _ when type == typeof(CyberDragonInfinity) => runState.CreateCard<CyberDragonInfinity>(player),
            _ when type == typeof(WingedDragonOfRaSphereMode) => runState.CreateCard<WingedDragonOfRaSphereMode>(player),
            _ when type == typeof(MillenniumGrandThief) => runState.CreateCard<MillenniumGrandThief>(player),
            _ when type == typeof(ExodiaSummoner) => runState.CreateCard<ExodiaSummoner>(player),
            _ when type == typeof(MillenniumMasterKey) => runState.CreateCard<MillenniumMasterKey>(player),
            _ when type == typeof(EvilExodia) => runState.CreateCard<EvilExodia>(player),
            _ when type == typeof(ExodiaGuardian) => runState.CreateCard<ExodiaGuardian>(player),
            _ => null
        };

    internal bool HasUpgradeableExtraDeckCard =>
        _ownedExtraDeck.Any(entry => CreateXyzMonsterPreview(entry)?.IsUpgradable == true);

    internal object GetOwnedExtraDeckEntryIdentity(int index) =>
        index >= 0 && index < _ownedExtraDeck.Count ? _ownedExtraDeck[index] : null;

    internal int GetOwnedExtraDeckEntryIndex(object identity) =>
        identity is ExtraDeckEntry entry ? _ownedExtraDeck.IndexOf(entry) : -1;

    internal bool TryRemoveOwnedExtraDeckEntry(object identity)
    {
        var index = GetOwnedExtraDeckEntryIndex(identity);
        if (index < 0)
            return false;

        _ownedExtraDeck.RemoveAt(index);

        // As with native deck removal, existing combat copies live until combat
        // ends. Only their permanent source indices change when an entry is removed.
        foreach (var entry in _extraDeck
                     .Concat(_trackedExtraDeckSummons.Values.Select(state => state.Entry))
                     .Distinct(new ReferenceComparer<ExtraDeckEntry>()))
            entry.OwnedEntryIndex = ReindexOwnedEntryAfterRemoval(entry.OwnedEntryIndex, index);
        foreach (var card in _physicalExtraDeckIdentities.Keys)
            card.ExtraDeckOwnedEntryIndex = ReindexOwnedEntryAfterRemoval(card.ExtraDeckOwnedEntryIndex, index);
        return true;
    }

    private static int ReindexOwnedEntryAfterRemoval(int ownedIndex, int removedIndex) =>
        ownedIndex == removedIndex ? -1 : ownedIndex > removedIndex ? ownedIndex - 1 : ownedIndex;

    internal IReadOnlyList<CardModel> CreateSelectionProxyCards(RunState runState)
    {
        var cards = new List<CardModel>();
        for (var index = 0; index < _ownedExtraDeck.Count; index++)
        {
            var card = CreateXyzMonsterProxy(runState, _ownedExtraDeck[index]);
            if (card is null)
                continue;

            card.ExtraDeckUpgradeIndex = index;
            card.ExtraDeckEnchantmentIndex = index;
            card.IsExtraDeckUpgradeProxy = true;
            card.IsExtraDeckEnchantmentProxy = true;
            cards.Add(card);
        }

        return cards;
    }

    internal bool TryUpdateExtraDeckUpgrade(int index, int upgradeLevel)
    {
        if (index < 0 || index >= _ownedExtraDeck.Count)
            return false;

        var entry = _ownedExtraDeck[index];
        var oldUpgradeLevel = entry.UpgradeLevel;
        var enchantmentKey = GetEnchantmentKey(entry.Enchantment);
        entry.UpgradeLevel = Math.Max(entry.UpgradeLevel, upgradeLevel);
        SyncCombatExtraDeckUpgrade(entry, oldUpgradeLevel, enchantmentKey);
        return entry.UpgradeLevel > oldUpgradeLevel;
    }

    private void SyncCombatExtraDeckUpgrade(ExtraDeckEntry upgradedEntry, int oldUpgradeLevel, string enchantmentKey)
    {
        var ownedIndex = _ownedExtraDeck.IndexOf(upgradedEntry);
        var combatEntry = _extraDeck.FirstOrDefault(entry =>
            entry.OwnedEntryIndex == ownedIndex
            && entry.MonsterType == upgradedEntry.MonsterType
            && entry.UpgradeLevel == oldUpgradeLevel
            && GetEnchantmentKey(entry.Enchantment) == enchantmentKey);
        if (combatEntry is not null)
            combatEntry.UpgradeLevel = upgradedEntry.UpgradeLevel;
    }

    internal bool HasEnchantableExtraDeckCard(
        EnchantmentModel enchantment,
        Func<CardModel, bool> predicate)
    {
        if (enchantment is null)
            return false;

        foreach (var entry in _ownedExtraDeck)
        {
            var card = CreateXyzMonsterPreview(entry, Owner);
            if (CanEnchantExtraDeckProxy(card, enchantment, predicate))
                return true;
        }

        return false;
    }

    internal bool TryUpdateExtraDeckEnchantment(int index, SerializableEnchantment enchantment)
    {
        if (index < 0 || index >= _ownedExtraDeck.Count)
            return false;

        var entry = _ownedExtraDeck[index];
        var oldEnchantmentKey = GetEnchantmentKey(entry.Enchantment);
        entry.Enchantment = CloneSerializableEnchantment(enchantment);
        SyncCombatExtraDeckEnchantment(entry, oldEnchantmentKey);
        return true;
    }

    internal bool TryClearExtraDeckEnchantment(int index) =>
        TryUpdateExtraDeckEnchantment(index, null);

    private void SyncCombatExtraDeckEnchantment(ExtraDeckEntry updatedEntry, string oldEnchantmentKey)
    {
        var ownedIndex = _ownedExtraDeck.IndexOf(updatedEntry);
        var combatEntry = _extraDeck.FirstOrDefault(entry =>
            entry.OwnedEntryIndex == ownedIndex
            && entry.MonsterType == updatedEntry.MonsterType
            && entry.UpgradeLevel == updatedEntry.UpgradeLevel
            && GetEnchantmentKey(entry.Enchantment) == oldEnchantmentKey);
        if (combatEntry is not null)
            combatEntry.Enchantment = CloneSerializableEnchantment(updatedEntry.Enchantment);
    }

    private static bool CanEnchantExtraDeckProxy(
        CardModel card,
        EnchantmentModel enchantment,
        Func<CardModel, bool> predicate)
    {
        if (card is null || enchantment is null)
            return false;

        try
        {
            return (predicate?.Invoke(card) ?? true) && enchantment.CanEnchant(card);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"ExtraDeck enchantment filter failed card={card.GetType().Name} error={ex}");
            return false;
        }
    }

    private bool TryGetCombatExtraDeckEntry(
        XyzMonsterCard card,
        out int index,
        out ExtraDeckEntry entry)
    {
        index = card.ExtraDeckEntryIndex;
        entry = null;

        if (!_previewExtraDeckIdentities.TryGetValue(card, out var identity)
            || !ReferenceEquals(identity.Combat, _extraDeckCombat))
            return false;

        if (index >= 0
            && index < _extraDeck.Count
            && _extraDeck[index].CombatEntryId == identity.EntryId
            && IsCurrentCombatEntry(_extraDeck[index]))
        {
            entry = _extraDeck[index];
            return true;
        }

        index = _extraDeck.FindIndex(extraDeckEntry =>
            extraDeckEntry.CombatEntryId == identity.EntryId && IsCurrentCombatEntry(extraDeckEntry));
        if (index < 0)
            return false;

        entry = _extraDeck[index];
        return true;
    }

    private bool RemoveExtraDeckEntry(int index, ExtraDeckEntry entry)
    {
        if (index < 0 || index >= _extraDeck.Count || _extraDeck[index] != entry)
            index = _extraDeck.IndexOf(entry);

        if (index < 0)
            return false;

        _extraDeck.RemoveAt(index);
        return true;
    }

    private async Task<bool> ConsumeExtraDeckEntry(
        PlayerChoiceContext ctx,
        int index,
        ExtraDeckEntry entry,
        Func<bool> canCommit = null)
    {
        if (entry is null || (canCommit is not null && !canCommit()))
            return false;

        var card = CreateXyzMonster(entry);
        if (card is null)
            return false;

        // Extra Deck entries are already-owned virtual cards, not generated
        // combat cards. Stage the materialized model silently in a non-Hand,
        // non-Play combat pile. Native exhaust will then reveal it, wait using
        // the player's game speed setting, and play the standard exhaust VFX.
        // Staging in Play skips that reveal and is especially easy to miss
        // because a newly materialized virtual card has no existing NCard.
        CardPileAddResult staged;
        try
        {
            staged = await CardPileCmd.Add(
                card,
                PileType.Discard,
                CardPilePosition.Top,
                null,
                true);
        }
        catch (Exception ex)
        {
            await CleanupUncommittedExtraDeckCard(card);
            MainFile.Logger.Info(
                $"ExtraDeck consume failed card={card.GetType().Name} reason=stage_exception error={ex}");
            return false;
        }

        if (!staged.success
            || !ReferenceEquals(staged.cardAdded, card)
            || !IsActuallyInPile(card, PileType.Discard))
        {
            var resultingPile = card.Pile?.Type.ToString() ?? "null";
            await CleanupUncommittedExtraDeckCard(card);
            MainFile.Logger.Info(
                $"ExtraDeck consume failed card={card.GetType().Name} reason=stage_rejected "
                + $"success={staged.success} sameCard={ReferenceEquals(staged.cardAdded, card)} pile={resultingPile}");
            return false;
        }

        var combatState = card.CombatState ?? card.Owner?.Creature?.CombatState;
        var combatActive = CombatManager.Instance is { IsInProgress: true, IsOverOrEnding: false };
        var removed = card.HasBeenRemovedFromState;
        var ownerAlive = card.Owner?.Creature is { IsDead: false };
        var registered = combatState?.ContainsCard(card) == true;
        var stagedForExhaust = IsActuallyInPile(card, PileType.Discard);
        var canExhaust = combatActive && !removed && ownerAlive && registered && stagedForExhaust;
        if (!canExhaust)
        {
            await CleanupUncommittedExtraDeckCard(card);
            MainFile.Logger.Info(
                $"ExtraDeck consume failed card={card.GetType().Name} reason=precommit_rejected "
                + $"combatActive={combatActive} removed={removed} ownerAlive={ownerAlive} "
                + $"registered={registered} stagedForExhaust={stagedForExhaust}");
            return false;
        }

        if (canCommit is not null && !canCommit())
        {
            await CleanupUncommittedExtraDeckCard(card);
            MainFile.Logger.Info(
                $"ExtraDeck consume failed card={card.GetType().Name} reason=commit_guard_rejected");
            return false;
        }

        if (!RemoveExtraDeckEntry(index, entry))
        {
            await CleanupUncommittedExtraDeckCard(card);
            MainFile.Logger.Info(
                $"ExtraDeck consume failed card={card.GetType().Name} reason=entry_missing");
            return false;
        }

        // From this point the consume is committed. Do not restore the virtual
        // entry or fake a second pile move if the native hook chain fails or a
        // legitimate exhaust listener redirects the card.
        try
        {
            await CardCmd.Exhaust(
                ctx ?? new BlockingPlayerChoiceContext(),
                card,
                false,
                false);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info(
                $"ExtraDeck consume native exhaust failed card={card.GetType().Name} "
                + $"pile={card.Pile?.Type.ToString() ?? "null"} error={ex}");
            throw;
        }

        var confirmed = IsActuallyInPile(card, PileType.Exhaust);
        MainFile.Logger.Info(
            $"ExtraDeck consume committed card={card.GetType().Name} "
            + $"pile={card.Pile?.Type.ToString() ?? "null"} confirmed={confirmed} remaining={_extraDeck.Count}");
        return true;
    }

    private bool IsActuallyInPile(CardModel card, PileType pileType) =>
        card?.Pile?.Type == pileType
        && CardPile.GetCards(Owner, pileType)
            .Any(candidate => ReferenceEquals(candidate, card));

    private static async Task CleanupUncommittedExtraDeckCard(CardModel card)
    {
        if (card is null || card.HasBeenRemovedFromState)
            return;

        try
        {
            if (card.Pile?.IsCombatPile == true)
                await CardPileCmd.RemoveFromCombat(card, true);
            else
                card.RemoveFromState();
        }
        catch (Exception ex)
        {
            var pile = card.Pile?.Type.ToString() ?? "null";
            var removed = card.HasBeenRemovedFromState;
            if (!removed)
            {
                try
                {
                    card.RemoveFromState();
                    removed = card.HasBeenRemovedFromState;
                }
                catch (Exception fallbackEx)
                {
                    MainFile.Logger.Info(
                        $"ExtraDeck consume cleanup fallback failed card={card.GetType().Name} "
                        + $"pile={pile} error={fallbackEx}");
                }
            }

            MainFile.Logger.Info(
                $"ExtraDeck consume cleanup failed card={card.GetType().Name} "
                + $"pile={pile} removed={removed} error={ex}");
        }
    }

    private void TrackExtraDeckSummon(XyzMonsterCard summon, ExtraDeckEntry entry, int index)
    {
        if (summon is null || entry is null)
            return;

        summon.ExtraDeckReturnIndex = Math.Max(0, index);
        _trackedExtraDeckSummons[summon] = new ExtraDeckReturnState(entry, index);
    }

    private async Task<bool> TryRestoreTrackedExtraDeckSummon(PlayerChoiceContext ctx, CardModel card)
    {
        if (card is not XyzMonsterCard summon
            || !_trackedExtraDeckSummons.TryGetValue(summon, out var state))
        {
            return false;
        }

        ClearTrackedExtraDeckSummon(summon);
        if (!IsCurrentCombatEntry(state.Entry))
            return false;
        // Capture before removing the physical model. Real callbacks may have
        // changed its growth since the original virtual entry was extracted.
        state.Entry.DevourSnapshot = CyberDevourState.CaptureCombatSnapshot(summon);
        MonsterFieldUpgradeLockService.CancelAwaitingFieldEntry(summon);
        MonsterFieldDampenCompatibilityPatch.RemoveRestoreLevel(summon);
        MonsterFieldService.UnmarkPending(summon);

        try
        {
            if (summon.Pile is not null)
                await CardPileCmd.RemoveFromCombat(summon, false);
            else
                summon.RemoveFromState();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"ExtraDeck return remove transient card failed card={summon.GetType().Name} error={ex}");
        }

        if (summon.HasBeenRemovedFromState)
        {
            _physicalExtraDeckIdentities.Remove(summon);
            state.Entry.DevourSnapshot = CyberDevourState.CaptureCombatSnapshot(summon);
            CyberDevourState.ReleaseCombatState(summon);
            RestoreExtraDeckEntry(state.Index, state.Entry);
        }
        else
        {
            // A failed native removal leaves this same entity alive. Retain
            // its ticket only if the awaited removal did not outlive this combat.
            if (IsCurrentCombatEntry(state.Entry))
                TrackExtraDeckSummon(summon, state.Entry, state.Index);
            return false;
        }
        MainFile.Logger.Info(
            $"ExtraDeck summon returned card={summon.GetType().Name} index={state.Index} pile={summon.Pile?.Type.ToString() ?? "null"} ctx={ctx?.GetType().Name ?? "null"}");
        return true;
    }

    private void ClearTrackedExtraDeckSummon(XyzMonsterCard summon)
    {
        if (summon is null)
            return;

        summon.ExtraDeckReturnIndex = -1;
        MillenniumResolution.ClearPreparedCount(summon);
        _trackedExtraDeckSummons.Remove(summon);
    }

    private async Task StageCyberloadMaterialForReturn(XyzMonsterCard material)
    {
        if (material is null
            || material.Owner != Owner
            || material.HasBeenRemovedFromState
            || material.Pile?.IsCombatPile != true)
        {
            return;
        }

        // Leave the field while keeping the same live combat entity available
        // to resolved material effects, such as Fara summoning its host again.
        // This is only a pile move, never a new exhaust event or a rollback.
        ClearTrackedExtraDeckSummon(material);
        MonsterFieldService.UnmarkPending(material);
        await CardPileCmd.Add(material, PileType.Discard, CardPilePosition.Top, this, false);
    }

    internal static Task<bool> ReturnToExtraDeck(XyzMonsterCard card) =>
        TryGetCore(card?.Owner)?.ReturnDepartedMonsterToExtraDeck(card) ?? Task.FromResult(false);

    private async Task<bool> ReturnDepartedMonsterToExtraDeck(XyzMonsterCard material)
    {
        if (material is null
            || material.Owner != Owner
            || material.HasBeenRemovedFromState
            || MonsterFieldService.IsOnField(material)
            || material.Pile?.IsCombatPile != true)
        {
            return false;
        }

        // Material-return and exhaust-return effects share the same identity
        // transfer. A host already summoned back stays on the field; only a
        // still-departed entity becomes an Extra Deck entry, preserving its
        // final upgrade, enchantment and devour state.
        var entry = CreateCombatExtraDeckEntryFromCard(material);
        if (!IsCurrentCombatEntry(entry))
            return false;
        // This physical monster has already left its original virtual entry.
        // Returning keeps its combat entry identity and never creates a
        // permanent owned copy or rolls back an uncommitted summon.
        ClearTrackedExtraDeckSummon(material);
        MonsterFieldUpgradeLockService.CancelAwaitingFieldEntry(material);
        MonsterFieldDampenCompatibilityPatch.RemoveRestoreLevel(material);
        MonsterFieldService.UnmarkPending(material);
        try
        {
            await CardPileCmd.RemoveFromCombat(material, false);
        }
        finally
        {
            // Native removal hooks can throw after committing the removal.
            // Transfer the snapshot iff the physical entity really left.
            if (material.HasBeenRemovedFromState)
            {
                _physicalExtraDeckIdentities.Remove(material);
                entry.DevourSnapshot = CyberDevourState.CaptureCombatSnapshot(material);
                CyberDevourState.ReleaseCombatState(material);
                RestoreExtraDeckEntry(_extraDeck.Count, entry);
            }
        }

        return material.HasBeenRemovedFromState
            && IsCurrentCombatEntry(entry)
            && _extraDeck.Any(candidate => candidate.CombatEntryId == entry.CombatEntryId);
    }

    private void RestoreExtraDeckEntry(int index, ExtraDeckEntry entry)
    {
        if (!IsCurrentCombatEntry(entry)
            || _extraDeck.Any(candidate => candidate.CombatEntryId == entry.CombatEntryId))
            return;

        var insertIndex = Math.Clamp(index, 0, _extraDeck.Count);
        _extraDeck.Insert(insertIndex, entry.Clone());
    }

    private static bool IsExtraDeckReturnPile(PileType pileType) =>
        pileType is PileType.Hand
            or PileType.Draw
            or PileType.Discard
            or PileType.Exhaust
            or PileType.Deck;

    private static ThermalVortexCore TryGetCore(Player player)
    {
        try
        {
            return player?.GetRelic<ThermalVortexCore>();
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<CardModel> CreateOwnedExtraDeckViewCards()
    {
        var owner = TryGetOwner();
        var cards = new List<CardModel>();
        for (var index = 0; index < _ownedExtraDeck.Count; index++)
        {
            var card = CreateXyzMonsterPreview(_ownedExtraDeck[index], owner);
            if (card is null)
                continue;

            card.ExtraDeckViewOwnedEntryIndex = index;
            cards.Add(card);
        }

        return cards;
    }

    private IReadOnlyList<CardModel> CreatePreviewCards(
        IEnumerable<ExtraDeckEntry> deck,
        bool mergeEquivalentEntries = true,
        bool includeStatusLine = true)
    {
        var remainingCounts = CountExtraDeck(GetDisplayRemainingExtraDeck());
        var owner = TryGetOwner();
        var cards = new List<CardModel>();
        var displayEntries = mergeEquivalentEntries && !deck.Any(entry => entry.CombatEntryId > 0)
            ? deck
                .GroupBy(entry => (entry.MonsterType, entry.UpgradeLevel, Enchantment: GetEnchantmentKey(entry.Enchantment)))
                .Select(group => group.First())
            : deck;

        foreach (var entry in displayEntries)
        {
            var type = entry.MonsterType;
            var card = CreateXyzMonsterPreview(entry, owner);
            if (card is not null)
            {
                var ownedCount = _ownedExtraDeck.Count(ownedEntry => ownedEntry.MonsterType == type);
                if (includeStatusLine && ownedCount > 0)
                    card.ExtraDeckStatusLine = CreateExtraDeckStatusLine(
                        RemainingExtraDeckCount(type, ownedCount, remainingCounts),
                        ownedCount);

                cards.Add(card);
            }
        }

        return cards;
    }

    private static LocString CreateExtraDeckStatusLine(int remainingCount, int ownedCount)
    {
        var locString = new LocString("cards", "THERMALVORTEX-EXTRA_DECK_STATUS.preview");
        locString.Add("Remaining", remainingCount);
        locString.Add("Owned", ownedCount);
        return locString;
    }

    private XyzMonsterCard CreateXyzMonsterPreview(ExtraDeckEntry entry, Player owner = null)
    {
        var card = CreateXyzMonsterPreview(entry.MonsterType, owner);
        if (card is not null)
        {
            ApplyEntryState(card, entry);
            CyberDevourState.AttachCombatPreviewSnapshot(card, entry.DevourSnapshot);
            if (IsCurrentCombatEntry(entry))
                _previewExtraDeckIdentities.Add(card, new ExtraDeckIdentity(entry.Combat, entry.CombatEntryId));
        }

        return card;
    }

    private XyzMonsterCard CreateXyzMonsterProxy(RunState runState, ExtraDeckEntry entry)
    {
        var card = CreateXyzRewardCard(runState, Owner, entry.MonsterType) as XyzMonsterCard;
        if (card is not null)
            ApplyEntryState(card, entry);

        return card;
    }

    private XyzMonsterCard CreateXyzMonsterPreview(Type type, Player owner = null)
    {
        if (type == typeof(Vortex))
            return CreateChoice<Vortex>(owner);

        if (type == typeof(Detonation))
            return CreateChoice<Detonation>(owner);

        if (type == typeof(CircuitTalismanBeast))
            return CreateChoice<CircuitTalismanBeast>(owner);

        if (type == typeof(FullArmorThunderLance))
            return CreateChoice<FullArmorThunderLance>(owner);

        if (type == typeof(DormantMagneticFieldBeast))
            return CreateChoice<DormantMagneticFieldBeast>(owner);

        if (type == typeof(BrilliantRebootKnight))
            return CreateChoice<BrilliantRebootKnight>(owner);

        if (type == typeof(DivineArsenalFurnaceGod))
            return CreateChoice<DivineArsenalFurnaceGod>(owner);

        if (type == typeof(ChimeratechOverdragon))
            return CreateChoice<ChimeratechOverdragon>(owner);

        if (type == typeof(Relinquished))
            return CreateChoice<Relinquished>(owner);

        if (type == typeof(CyberEndDragon))
            return CreateChoice<CyberEndDragon>(owner);

        if (type == typeof(CyberDragonInfinity))
            return CreateChoice<CyberDragonInfinity>(owner);

        if (type == typeof(WingedDragonOfRaSphereMode))
            return CreateChoice<WingedDragonOfRaSphereMode>(owner);

        if (type == typeof(MillenniumGrandThief))
            return CreateChoice<MillenniumGrandThief>(owner);

        if (type == typeof(ExodiaSummoner))
            return CreateChoice<ExodiaSummoner>(owner);

        if (type == typeof(MillenniumMasterKey))
            return CreateChoice<MillenniumMasterKey>(owner);

        if (type == typeof(EvilExodia))
            return CreateChoice<EvilExodia>(owner);

        if (type == typeof(ExodiaGuardian))
            return CreateChoice<ExodiaGuardian>(owner);

        return null;
    }

    private bool CanChooseXyzType(Type type) =>
        (Owner?.Creature.HasPower<DormantMagneticFieldPower>() != true || !IsAttackExtraDeckType(type))
        && (type != typeof(BrilliantRebootKnight) || BrilliantRebootKnight.IsRebootConditionMet(Owner))
        && (type != typeof(Relinquished) || CanChooseRelinquished())
        && (type != typeof(WingedDragonOfRaSphereMode) || CanChooseRaSphereMode());

    internal static bool IsAttackExtraDeckType(Type type) =>
        type == typeof(Detonation)
        || type == typeof(BrilliantRebootKnight)
        || type == typeof(DivineArsenalFurnaceGod)
        || type == typeof(ChimeratechOverdragon)
        || type == typeof(CyberDragonInfinity)
        || type == typeof(EvilExodia);

    private Creature ResolveXyzTargetForPlay(PlayerChoiceContext ctx, XyzMonsterCard summon) =>
        !IsAutomatedChoiceContext(ctx)
        && summon.TargetType.IsSingleTarget()
        && summon.TargetType != TargetType.Self
        && summon.TargetType != TargetType.TargetedNoCreature
            ? null
            : ResolveXyzTarget(summon);

    private Creature ResolveXyzTarget(XyzMonsterCard summon)
    {
        if (summon.TargetType == TargetType.Self)
            return Owner.Creature;

        if (summon is Relinquished)
            return Owner.Creature.CombatState.HittableEnemies.FirstOrDefault(Relinquished.IsMinion);

        return Owner.Creature.CombatState.HittableEnemies.FirstOrDefault();
    }

    private bool CanExtraDeckSummon(ExtraDeckSummonMode mode)
    {
        return BuildXyzChoices(mode).Count > 0;
    }

    private List<XyzMonsterCard> BuildXyzChoices(
        ExtraDeckSummonMode mode,
        int? maximumMaterialsOverride = null)
    {
        var materialChoices = GetMaterialCandidates(mode);
        var choices = new List<XyzMonsterCard>();
        foreach (var representative in _extraDeck.Select((entry, index) => (Entry: entry, Index: index)))
        {
            var type = representative.Entry.MonsterType;
            if (!CanChooseXyzType(type)
                || (!AllowsRaSphere(mode) && type == typeof(WingedDragonOfRaSphereMode)))
            {
                continue;
            }

            var choice = CreateXyzMonsterPreview(representative.Entry, Owner);
            if (choice is null)
                continue;

            if (!TryCreateMaterialSelectionPlan(
                    choice,
                    mode,
                    materialChoices,
                    maximumMaterialsOverride,
                    out _))
            {
                continue;
            }

            choice.ExtraDeckEntryIndex = representative.Index;
            choice.Materials = choice.MinimumMaterials;
            choices.Add(choice);
        }

        return choices;
    }

    private List<XyzMonsterCard> BuildExtraDeckConsumeChoices(
        Func<XyzMonsterCard, bool> predicate = null)
    {
        var choices = new List<XyzMonsterCard>();
        foreach (var representative in _extraDeck.Select((entry, index) => (Entry: entry, Index: index)))
        {
            var choice = CreateXyzMonsterPreview(representative.Entry, Owner);
            if (choice is null)
                continue;

            if (predicate is not null && !predicate(choice))
                continue;

            choice.ExtraDeckEntryIndex = representative.Index;
            choices.Add(choice);
        }

        return choices;
    }

    private List<XyzMonsterCard> BuildExtraDeckChoicesByHp(int minimumHp) =>
        BuildExtraDeckConsumeChoices(
            choice => MonsterFieldHealthService.GetInitialMaxHp(choice) > minimumHp);

    private T CreateChoice<T>(Player owner = null) where T : XyzMonsterCard
    {
        var choice = (T)ModelDb.Card<T>().ToMutable();
        if (owner is not null)
            choice.Owner = owner;

        return choice;
    }

    private Player TryGetOwner()
    {
        try
        {
            return Owner;
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyUpgradeLevel(CardModel card, int upgradeLevel)
    {
        while (card.CurrentUpgradeLevel < upgradeLevel && card.IsUpgradable)
        {
            card.UpgradeInternal();
            card.FinalizeUpgradeInternal();
        }
    }

    private static ExtraDeckEntry CreateExtraDeckEntryFromCard(Type cardType, CardModel card) =>
        new(cardType, card.CurrentUpgradeLevel, card.Enchantment?.ToSerializable());

    private ExtraDeckEntry CreateCombatExtraDeckEntryFromCard(XyzMonsterCard card)
    {
        if (card is null || _extraDeckCombat is null
            || !ReferenceEquals(card.Owner?.Creature?.CombatState, _extraDeckCombat))
            return null;

        // A copied physical card has no reference binding and gets its own
        // identity even when it carries the same permanent source index.
        if (!_physicalExtraDeckIdentities.TryGetValue(card, out var identity)
            || !ReferenceEquals(identity.Combat, _extraDeckCombat))
        {
            identity = new ExtraDeckIdentity(_extraDeckCombat, ++_nextCombatEntryId);
            _physicalExtraDeckIdentities[card] = identity;
        }

        return new ExtraDeckEntry(card.GetType(), card.CurrentUpgradeLevel,
            card.Enchantment?.ToSerializable(), card.ExtraDeckOwnedEntryIndex,
            identity.EntryId, identity.Combat, CyberDevourState.CaptureCombatSnapshot(card));
    }

    private static void ApplyEntryState(CardModel card, ExtraDeckEntry entry)
    {
        ApplyUpgradeLevel(card, entry.UpgradeLevel);
        ApplyEnchantment(card, entry.Enchantment);
        if (card is XyzMonsterCard xyz)
            xyz.ExtraDeckOwnedEntryIndex = entry.OwnedEntryIndex;
    }

    private static void ApplyEnchantment(CardModel card, SerializableEnchantment serializedEnchantment)
    {
        if (card is null || !HasSerializableEnchantment(serializedEnchantment))
            return;

        try
        {
            var enchantment = EnchantmentModel.FromSerializable(CloneSerializableEnchantment(serializedEnchantment));
            if (enchantment is not null)
                card.EnchantInternal(enchantment, serializedEnchantment.Amount);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"ExtraDeck enchantment apply failed card={card.GetType().Name} error={ex}");
        }
    }

    private Task<CardSelectionResult<XyzMonsterCard>> ChooseXyzMonster(
        PlayerChoiceContext ctx,
        IReadOnlyList<XyzMonsterCard> choices,
        bool allowBack) =>
        CardSelectionHelper.ChooseOneWithBack(
            ctx,
            Owner,
            choices,
            "THERMALVORTEX-XYZ_SUMMON.selectionPrompt",
            allowBack,
            requireManualConfirmation: false);

    private void ShowUnavailableFusion(ExtraDeckSummonMode mode)
    {
        if (mode is not (ExtraDeckSummonMode.FieldOnly or ExtraDeckSummonMode.FieldAndHand)
            || Owner?.Creature is not { IsAlive: true } creature
            || CombatManager.Instance?.IsOverOrEnding == true)
        {
            return;
        }

        ThinkCmd.Play(
            new LocString("cards", "THERMALVORTEX-XYZ_SUMMON.unavailablePrompt"),
            creature,
            2d);
    }

    private async Task<ExtraDeckSummonSelection> ChooseExtraDeckSummonBeforeCommit(
        PlayerChoiceContext ctx,
        ExtraDeckSummonMode mode,
        int? maximumMaterialsOverride = null)
    {
        while (true)
        {
            var result = await RunExtraDeckSummonSelection(
                ctx,
                mode,
                maximumMaterialsOverride);
            if (result.Completed)
                return result.Selection;

            // Fusion Gate is a mandatory trigger. A transiently invalid plan may
            // restart, but even it cannot use Back to leave its first real screen.
            if (result.Outcome != ExtraDeckPrecommitOutcome.PlanInvalidated
                || mode != ExtraDeckSummonMode.FusionGate
                || BuildXyzChoices(mode, maximumMaterialsOverride).Count == 0)
            {
                return null;
            }
        }
    }

    private async Task<ExtraDeckPrecommitResult> RunExtraDeckSummonSelection(
        PlayerChoiceContext ctx,
        ExtraDeckSummonMode mode,
        int? maximumMaterialsOverride)
    {
        var presentedHistory = new List<ExtraDeckSelectionStage>();
        var stage = ExtraDeckSelectionStage.Monster;
        XyzMonsterCard chosen = null;
        IReadOnlyList<CardModel> requiredFieldMaterials = [];

        while (true)
        {
            if (stage == ExtraDeckSelectionStage.Monster)
            {
                var monsterChoices = BuildXyzChoices(mode, maximumMaterialsOverride);
                if (monsterChoices.Count == 0)
                {
                    ShowUnavailableFusion(mode);
                    return new ExtraDeckPrecommitResult(
                        ExtraDeckPrecommitOutcome.PlanInvalidated,
                        null);
                }

                var monsterSelection = await ChooseXyzMonster(
                    ctx,
                    monsterChoices,
                    allowBack: presentedHistory.Count > 0);
                if (!monsterSelection.Confirmed)
                    return new ExtraDeckPrecommitResult(
                        ExtraDeckPrecommitOutcome.SelectionInvalidated,
                        null);

                chosen = monsterSelection.FirstOrDefault;
                if (chosen is null)
                    return new ExtraDeckPrecommitResult(
                        ExtraDeckPrecommitOutcome.SelectionInvalidated,
                        null);

                requiredFieldMaterials = [];
                RecordPresentedStage(
                    presentedHistory,
                    ExtraDeckSelectionStage.Monster,
                    monsterSelection.WasPresented);
                stage = ExtraDeckSelectionStage.RequiredFieldMaterials;
                continue;
            }

            if (chosen is null)
                return new ExtraDeckPrecommitResult(
                    ExtraDeckPrecommitOutcome.PlanInvalidated,
                    null);

            var materialChoices = GetMaterialCandidates(mode);
            if (materialChoices.Count == 0
                || !TryCreateMaterialSelectionPlan(
                    chosen,
                    mode,
                    materialChoices,
                    maximumMaterialsOverride,
                    out var plan))
            {
                ShowUnavailableFusion(mode);
                return new ExtraDeckPrecommitResult(
                    ExtraDeckPrecommitOutcome.PlanInvalidated,
                    null);
            }

            if (stage == ExtraDeckSelectionStage.RequiredFieldMaterials)
            {
                MainFile.Logger.Info(
                    $"ExtraDeck material selection begin mode={mode} summon={chosen.GetType().Name} "
                    + $"choices={plan.Candidates.Count} min={plan.MinimumMaterials} max={plan.MaximumMaterials} "
                    + $"requiredField={plan.RequiredAdditionalFieldMaterials} fixed={plan.FixedMaterials.Count}");

                if (IsAutomatedChoiceContext(ctx) || plan.FixedSelection)
                {
                    var automaticMaterials = plan.FixedSelection
                        ? plan.FixedMaterials
                        : plan.AutomaticSelection;
                    if (TryCreateExtraDeckSummonSelection(
                        chosen,
                        mode,
                        automaticMaterials,
                        maximumMaterialsOverride,
                        out var automaticSelection))
                    {
                        return new ExtraDeckPrecommitResult(
                            ExtraDeckPrecommitOutcome.Completed,
                            automaticSelection);
                    }

                    return new ExtraDeckPrecommitResult(
                        ExtraDeckPrecommitOutcome.PlanInvalidated,
                        null);
                }

                if (plan.RequiredAdditionalFieldMaterials <= 0)
                {
                    requiredFieldMaterials = [];
                    stage = ExtraDeckSelectionStage.RemainingMaterials;
                    continue;
                }

                var fieldChoices = ExcludeMaterials(
                        plan.Candidates.Where(MonsterFieldService.IsOnField),
                        plan.FixedMaterials)
                    .ToList();
                var fieldSelection = await ChooseMaterials(
                    ctx,
                    fieldChoices,
                    plan.RequiredAdditionalFieldMaterials,
                    plan.RequiredAdditionalFieldMaterials,
                    allowBack: presentedHistory.Count > 0,
                    promptKey: "THERMALVORTEX-XYZ_FIELD_MATERIALS.selectionPrompt",
                    sourceCard: chosen,
                    fixedMaterials: plan.FixedMaterials);
                if (fieldSelection.WentBack)
                {
                    if (!TryReturnToPreviousPresentedStage(
                            presentedHistory,
                            ref stage,
                            ref chosen,
                            ref requiredFieldMaterials))
                    {
                        return new ExtraDeckPrecommitResult(
                            ExtraDeckPrecommitOutcome.SelectionInvalidated,
                            null);
                    }

                    await Task.Yield();
                    continue;
                }

                if (!fieldSelection.Confirmed
                    || fieldSelection.Selection.Count != plan.RequiredAdditionalFieldMaterials)
                {
                    return new ExtraDeckPrecommitResult(
                        ExtraDeckPrecommitOutcome.SelectionInvalidated,
                        null);
                }

                requiredFieldMaterials = fieldSelection.Selection;
                RecordPresentedStage(
                    presentedHistory,
                    ExtraDeckSelectionStage.RequiredFieldMaterials,
                    fieldSelection.WasPresented);
                stage = ExtraDeckSelectionStage.RemainingMaterials;
                continue;
            }

            var selectedMaterials = plan.FixedMaterials.ToList();
            var currentFieldChoices = ExcludeMaterials(
                    plan.Candidates.Where(MonsterFieldService.IsOnField),
                    selectedMaterials)
                .ToList();
            var resolvedFieldMaterials = ResolveSelectedMaterials(
                requiredFieldMaterials,
                currentFieldChoices);
            if (resolvedFieldMaterials.Count != plan.RequiredAdditionalFieldMaterials
                || resolvedFieldMaterials.Count != requiredFieldMaterials.Count)
            {
                return new ExtraDeckPrecommitResult(
                    ExtraDeckPrecommitOutcome.PlanInvalidated,
                    null);
            }

            selectedMaterials.AddRange(resolvedFieldMaterials);
            var remainingChoices = ExcludeMaterials(plan.Candidates, selectedMaterials).ToList();
            var remainingMinimum = Math.Max(0, plan.MinimumMaterials - selectedMaterials.Count);
            var remainingMaximum = Math.Min(
                Math.Max(0, plan.MaximumMaterials - selectedMaterials.Count),
                remainingChoices.Count);
            if (remainingMinimum > remainingMaximum)
            {
                return new ExtraDeckPrecommitResult(
                    ExtraDeckPrecommitOutcome.PlanInvalidated,
                    null);
            }

            if (remainingMaximum > 0)
            {
                var remainingSelection = await ChooseMaterials(
                    ctx,
                    remainingChoices,
                    remainingMinimum,
                    remainingMaximum,
                    allowBack: presentedHistory.Count > 0,
                    promptKey: "THERMALVORTEX-XYZ_MATERIALS.selectionPrompt",
                    sourceCard: chosen,
                    fixedMaterials: selectedMaterials);
                if (remainingSelection.WentBack)
                {
                    if (!TryReturnToPreviousPresentedStage(
                            presentedHistory,
                            ref stage,
                            ref chosen,
                            ref requiredFieldMaterials))
                    {
                        return new ExtraDeckPrecommitResult(
                            ExtraDeckPrecommitOutcome.SelectionInvalidated,
                            null);
                    }

                    await Task.Yield();
                    continue;
                }

                if (!remainingSelection.Confirmed
                    || remainingSelection.Selection.Count < remainingMinimum
                    || remainingSelection.Selection.Count > remainingMaximum)
                {
                    return new ExtraDeckPrecommitResult(
                        ExtraDeckPrecommitOutcome.SelectionInvalidated,
                        null);
                }

                selectedMaterials.AddRange(remainingSelection.Selection);
                RecordPresentedStage(
                    presentedHistory,
                    ExtraDeckSelectionStage.RemainingMaterials,
                    remainingSelection.WasPresented);
            }

            MainFile.Logger.Info(
                $"ExtraDeck material selection end mode={mode} summon={chosen.GetType().Name} selected={selectedMaterials.Count}");
            if (TryCreateExtraDeckSummonSelection(
                chosen,
                mode,
                selectedMaterials,
                maximumMaterialsOverride,
                out var completedSelection))
            {
                return new ExtraDeckPrecommitResult(
                    ExtraDeckPrecommitOutcome.Completed,
                    completedSelection);
            }

            return new ExtraDeckPrecommitResult(
                ExtraDeckPrecommitOutcome.PlanInvalidated,
                null);
        }
    }

    private bool TryCreateExtraDeckSummonSelection(
        XyzMonsterCard chosen,
        ExtraDeckSummonMode mode,
        IReadOnlyList<CardModel> materials,
        int? maximumMaterialsOverride,
        out ExtraDeckSummonSelection selection)
    {
        selection = null;
        if (chosen is null || materials is null || materials.Count == 0)
            return false;

        var currentMaterialChoices = GetMaterialCandidates(mode);
        if (!TryGetCombatExtraDeckEntry(chosen, out var entryIndex, out var entry)
            || !IsLegalMaterialCombination(
                chosen,
                mode,
                materials,
                currentMaterialChoices,
                maximumMaterialsOverride))
        {
            MainFile.Logger.Info(
                $"ExtraDeck material selection became invalid mode={mode} summon={chosen.GetType().Name}");
            return false;
        }

        selection = new ExtraDeckSummonSelection(chosen, materials, entryIndex, entry);
        return true;
    }

    private static void RecordPresentedStage(
        ICollection<ExtraDeckSelectionStage> presentedHistory,
        ExtraDeckSelectionStage stage,
        bool wasPresented)
    {
        if (wasPresented)
            presentedHistory.Add(stage);
    }

    private static bool TryReturnToPreviousPresentedStage(
        IList<ExtraDeckSelectionStage> presentedHistory,
        ref ExtraDeckSelectionStage stage,
        ref XyzMonsterCard chosen,
        ref IReadOnlyList<CardModel> requiredFieldMaterials)
    {
        if (presentedHistory.Count == 0)
            return false;

        var previousIndex = presentedHistory.Count - 1;
        var previousStage = presentedHistory[previousIndex];
        presentedHistory.RemoveAt(previousIndex);
        stage = previousStage;

        switch (previousStage)
        {
            case ExtraDeckSelectionStage.Monster:
                chosen = null;
                requiredFieldMaterials = [];
                break;
            case ExtraDeckSelectionStage.RequiredFieldMaterials:
                requiredFieldMaterials = [];
                break;
        }

        return true;
    }

    private async Task<XyzMonsterCard> ChooseExtraDeckCardToConsume(
        PlayerChoiceContext ctx,
        IReadOnlyList<XyzMonsterCard> choices)
    {
        var prefs = new CardSelectorPrefs(
            new LocString("cards", "THERMALVORTEX-EXTRA_DECK_CONSUME.selectionPrompt"),
            1,
            1)
        {
            Cancelable = false,
            RequireManualConfirmation = false,
            PretendCardsCanBePlayed = true
        };
        var selected = await CardSelectCmd.FromSimpleGrid(ctx, choices, Owner, prefs);
        return selected?.OfType<XyzMonsterCard>().FirstOrDefault();
    }

    private async Task<CardSelectionResult<CardModel>> ChooseMaterials(
        PlayerChoiceContext ctx,
        IReadOnlyList<CardModel> choices,
        int minimumMaterials,
        int maximumMaterials,
        bool allowBack,
        string promptKey,
        CardModel sourceCard,
        IReadOnlyList<CardModel> fixedMaterials)
    {
        if (minimumMaterials > maximumMaterials)
            return new CardSelectionResult<CardModel>(CardSelectionOutcome.Invalidated, [], false);

        var result = await CardSelectionHelper.ChooseManyWithBack(
            ctx,
            Owner,
            choices,
            promptKey,
            minimumMaterials,
            maximumMaterials,
            allowBack,
            requireManualConfirmation: true,
            sourceCard: sourceCard,
            preview: new CardEffectSelectionPreview(fixedMaterials, IsFusionSelection: true));
        if (!result.Confirmed)
            return result;

        var resolved = ResolveSelectedMaterials(result.Selection, choices);
        return resolved.Count == result.Selection.Count
            ? new CardSelectionResult<CardModel>(
                CardSelectionOutcome.Confirmed,
                resolved,
                result.WasPresented)
            : new CardSelectionResult<CardModel>(
                CardSelectionOutcome.Invalidated,
                [],
                result.WasPresented);
    }

    private static IReadOnlyList<CardModel> ResolveSelectedMaterials(
        IEnumerable<CardModel> selected,
        IReadOnlyList<CardModel> choices)
    {
        if (selected is null)
            return [];

        var remaining = choices.ToList();
        var resolved = new List<CardModel>();
        foreach (var selectedCard in selected)
        {
            var index = remaining.FindIndex(candidate => ReferenceEquals(candidate, selectedCard));
            if (index < 0)
            {
                MainFile.Logger.Info(
                    $"ExtraDeck material selection returned unknown card type={selectedCard.GetType().Name} id={selectedCard.Id}");
                return [];
            }

            resolved.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return resolved;
    }

    private IReadOnlyList<CardModel> GetMaterialCandidates(ExtraDeckSummonMode mode)
    {
        var fieldMonsters = MonsterFieldService.GetMonsters(Owner).ToList();
        return mode switch
        {
            ExtraDeckSummonMode.FieldOnly => fieldMonsters,
            ExtraDeckSummonMode.FieldAndHand =>
                fieldMonsters.Concat(GetPileMonsters(PileType.Hand)).ToList(),
            ExtraDeckSummonMode.CyberloadFusion =>
                fieldMonsters.Concat(GetPileMonsters(PileType.Exhaust)).ToList(),
            ExtraDeckSummonMode.MiracleFusion =>
                fieldMonsters.Concat(GetPileMonsters(PileType.Discard)).ToList(),
            ExtraDeckSummonMode.FusionGate =>
                fieldMonsters.Concat(GetPileMonsters(PileType.Hand)).ToList(),
            ExtraDeckSummonMode.FutureFusion =>
                GetPileMonsters(PileType.Draw).ToList(),
            _ => fieldMonsters
        };
    }

    private IReadOnlyList<CardModel> GetPileMonsters(PileType pileType) =>
        TryGetOwner() is { PlayerCombatState: not null } owner
            ? CardPile.GetCards(owner, pileType)
                .Where(MonsterFieldService.IsFieldMonster)
                .ToList()
            : [];

    private async Task MoveMaterials(
        PlayerChoiceContext ctx,
        IReadOnlyList<CardModel> materials,
        ExtraDeckSummonMode mode,
        Func<bool> isMaterialUseValid)
    {
        switch (mode)
        {
            case ExtraDeckSummonMode.FieldOnly:
                await MonsterFieldService.SendToDiscard(ctx, materials, this, MonsterFieldLeaveReason.FusionMaterial);
                break;
            case ExtraDeckSummonMode.FieldAndHand:
                var fieldMaterials = materials.Where(MonsterFieldService.IsOnField).ToList();
                var handMaterials = materials.Where(card => !MonsterFieldService.IsOnField(card)).ToList();
                await MonsterFieldService.SendToDiscard(ctx, fieldMaterials, this, MonsterFieldLeaveReason.FusionMaterial);
                if (!isMaterialUseValid())
                    return;
                await MonsterFieldService.SendToDiscard(
                    ctx, handMaterials, this, MonsterFieldLeaveReason.FusionMaterial);
                break;
            case ExtraDeckSummonMode.CyberloadFusion:
                var mainDeckMaterials = materials.Where(card => card is not XyzMonsterCard).ToList();
                var extraDeckMaterials = materials.OfType<XyzMonsterCard>().Cast<CardModel>().ToList();
                await MonsterFieldService.SendToDraw(
                    ctx,
                    mainDeckMaterials,
                    this,
                    MonsterFieldLeaveReason.FusionMaterial,
                    CardPilePosition.Top);
                if (!isMaterialUseValid())
                    return;
                await MonsterFieldService.SendToDraw(
                    ctx,
                    extraDeckMaterials,
                    this,
                    MonsterFieldLeaveReason.FusionMaterial,
                    returnExtraDeckMaterial: StageCyberloadMaterialForReturn);
                if (!isMaterialUseValid())
                    return;
                foreach (var extraMaterial in extraDeckMaterials.OfType<XyzMonsterCard>())
                {
                    await ReturnDepartedMonsterToExtraDeck(extraMaterial);
                    if (!isMaterialUseValid())
                        return;
                }
                if (mainDeckMaterials.Count > 0 && isMaterialUseValid())
                    await CardPileCmd.Shuffle(ctx, Owner);
                break;
            case ExtraDeckSummonMode.MiracleFusion:
                await MonsterFieldService.SendToExhaust(ctx, materials, this, MonsterFieldLeaveReason.FusionMaterial);
                break;
            case ExtraDeckSummonMode.FusionGate:
                await MonsterFieldService.SendToExhaust(ctx, materials, this, MonsterFieldLeaveReason.FusionMaterial);
                break;
        }
    }

    private static bool AllowsRaSphere(ExtraDeckSummonMode mode) =>
        mode is ExtraDeckSummonMode.FieldOnly or ExtraDeckSummonMode.FieldAndHand;

    private static bool RequiresMillenniumMaterials(XyzMonsterCard card) =>
        card is MillenniumGrandThief or ExodiaSummoner or MillenniumMasterKey;

    private static bool IsAutomatedChoiceContext(PlayerChoiceContext ctx) =>
        ctx?.GetType().Name == "BlockingPlayerChoiceContext";

    private bool TryCreateMaterialSelectionPlan(
        XyzMonsterCard choice,
        ExtraDeckSummonMode mode,
        IReadOnlyList<CardModel> materialChoices,
        int? maximumMaterialsOverride,
        out MaterialSelectionPlan plan)
    {
        plan = null;
        if (choice is null
            || materialChoices is null
            || !CanChooseXyzType(choice.GetType())
            || (!AllowsRaSphere(mode) && choice is WingedDragonOfRaSphereMode))
        {
            return false;
        }

        var candidates = materialChoices
            .Where(card => card is not null)
            .Distinct(new ReferenceComparer<CardModel>())
            .ToList();
        if (choice is CyberEndDragon)
            candidates = candidates.Where(CyberSeries.IsCyberMonster).ToList();
        if (RequiresMillenniumMaterials(choice))
            candidates = candidates.Where(MillenniumSeries.IsMillenniumMonster).ToList();

        var maximumOverride = maximumMaterialsOverride ?? int.MaxValue;
        if (maximumOverride <= 0)
            return false;

        var fixedMaterials = new List<CardModel>();
        var fixedSelection = false;
        switch (choice)
        {
            case Relinquished:
                if (!TryGetRelinquishedFieldMaterial(candidates, out var relinquishedMaterial))
                    return false;
                fixedMaterials.Add(relinquishedMaterial);
                fixedSelection = true;
                break;
            case DivineArsenalFurnaceGod:
                fixedMaterials.AddRange(ChooseAllFieldExtraDeckMonsters(candidates));
                fixedSelection = true;
                break;
            case WingedDragonOfRaSphereMode:
                fixedMaterials.AddRange(ChooseRaSphereMaterials(candidates));
                fixedSelection = true;
                break;
            case ChimeratechOverdragon:
                var chimeratechMaterial = candidates.FirstOrDefault(IsRequiredChimeratechMaterial);
                if (chimeratechMaterial is null)
                    return false;
                fixedMaterials.Add(chimeratechMaterial);
                break;
            case CyberDragonInfinity:
                var infinityMaterial = candidates.FirstOrDefault(IsRequiredCyberDragonInfinityMaterial);
                if (infinityMaterial is null)
                    return false;
                fixedMaterials.Add(infinityMaterial);
                break;
        }

        if (fixedSelection)
        {
            if (fixedMaterials.Count == 0
                || fixedMaterials.Count > maximumOverride
                || !IsLegalMaterialCombination(
                    choice,
                    mode,
                    fixedMaterials,
                    materialChoices,
                    maximumMaterialsOverride))
            {
                return false;
            }

            plan = new MaterialSelectionPlan(
                candidates,
                fixedMaterials,
                fixedMaterials.Count,
                fixedMaterials.Count,
                0,
                true,
                fixedMaterials);
            return true;
        }

        var minimumMaterials = Math.Max(0, choice.MinimumMaterials);
        var maximumMaterials = Math.Min(
            Math.Min(Math.Max(0, choice.MaximumMaterials), candidates.Count),
            maximumOverride);
        if (minimumMaterials > maximumMaterials || fixedMaterials.Count > maximumMaterials)
            return false;

        var requiredFieldMaterials = mode == ExtraDeckSummonMode.FutureFusion
            ? 0
            : MonsterFieldService.GetRequiredFieldDeparturesForPlacement(choice);
        var fixedFieldMaterials = fixedMaterials.Count(MonsterFieldService.IsOnField);
        var requiredAdditionalFieldMaterials = Math.Max(0, requiredFieldMaterials - fixedFieldMaterials);
        var availableFieldMaterials = ExcludeMaterials(
                candidates.Where(MonsterFieldService.IsOnField),
                fixedMaterials)
            .ToList();
        if (availableFieldMaterials.Count < requiredAdditionalFieldMaterials
            || fixedMaterials.Count + requiredAdditionalFieldMaterials > maximumMaterials)
        {
            return false;
        }

        var automaticSelection = fixedMaterials.ToList();
        automaticSelection.AddRange(availableFieldMaterials.Take(requiredAdditionalFieldMaterials));
        var remainingCandidates = ExcludeMaterials(candidates, automaticSelection).ToList();
        var remainingMinimum = Math.Max(0, minimumMaterials - automaticSelection.Count);
        var remainingMaximum = maximumMaterials - automaticSelection.Count;
        if (remainingMinimum > remainingMaximum || remainingCandidates.Count < remainingMinimum)
            return false;

        automaticSelection.AddRange(remainingCandidates.Take(remainingMinimum));
        if (!IsLegalMaterialCombination(
                choice,
                mode,
                automaticSelection,
                materialChoices,
                maximumMaterialsOverride))
        {
            return false;
        }

        plan = new MaterialSelectionPlan(
            candidates,
            fixedMaterials,
            minimumMaterials,
            maximumMaterials,
            requiredAdditionalFieldMaterials,
            false,
            automaticSelection);
        return true;
    }

    private bool IsLegalMaterialCombination(
        XyzMonsterCard choice,
        ExtraDeckSummonMode mode,
        IReadOnlyList<CardModel> selectedMaterials,
        IReadOnlyList<CardModel> materialChoices,
        int? maximumMaterialsOverride = null)
    {
        if (choice is null
            || selectedMaterials is null
            || materialChoices is null
            || !CanChooseXyzType(choice.GetType())
            || (!AllowsRaSphere(mode) && choice is WingedDragonOfRaSphereMode))
        {
            return false;
        }

        var available = materialChoices
            .Where(card => card is not null)
            .Distinct(new ReferenceComparer<CardModel>())
            .ToList();
        var selected = selectedMaterials.Where(card => card is not null).ToList();
        var selectedSet = selected.ToHashSet(new ReferenceComparer<CardModel>());
        if (selected.Count == 0
            || selectedSet.Count != selected.Count
            || selected.Any(card => !available.Any(candidate => ReferenceEquals(candidate, card))))
        {
            return false;
        }

        var maximumOverride = maximumMaterialsOverride ?? int.MaxValue;
        if (selected.Count > maximumOverride)
            return false;

        switch (choice)
        {
            case Relinquished:
                if (!TryGetRelinquishedFieldMaterial(available, out var relinquishedMaterial)
                    || selected.Count != 1
                    || !ReferenceEquals(selected[0], relinquishedMaterial))
                {
                    return false;
                }
                break;
            case DivineArsenalFurnaceGod:
                if (!HaveSameMaterialReferences(selected, ChooseAllFieldExtraDeckMonsters(available)))
                    return false;
                break;
            case WingedDragonOfRaSphereMode:
                if (!HaveSameMaterialReferences(selected, ChooseRaSphereMaterials(available)))
                    return false;
                break;
            default:
                if (selected.Count < choice.MinimumMaterials
                    || selected.Count > choice.MaximumMaterials)
                {
                    return false;
                }
                break;
        }

        if (choice is CyberEndDragon && selected.Any(card => !CyberSeries.IsCyberMonster(card)))
            return false;

        if (RequiresMillenniumMaterials(choice) && selected.Any(card => !MillenniumSeries.IsMillenniumMonster(card)))
            return false;

        if (choice is ChimeratechOverdragon
            && !selected.Any(IsRequiredChimeratechMaterial))
        {
            return false;
        }

        if (choice is CyberDragonInfinity
            && !selected.Any(IsRequiredCyberDragonInfinityMaterial))
        {
            return false;
        }

        if (mode == ExtraDeckSummonMode.FutureFusion)
            return true;

        if (!MonsterFieldService.CanPlaceOnFieldAfterRemoving(choice, selected))
        {
            return false;
        }

        // Validate play restrictions and targets before presenting material
        // choices. Project the slots these field materials would free without
        // moving them or invoking any material/leave-field effects.
        var previousMaterials = choice.Materials;
        choice.Materials = selected.Count;
        using var projectedSlots = MonsterFieldService.PushTemporaryCapacityBonus(
            Owner,
            selected.Count(MonsterFieldService.IsOnField));
        try
        {
            return CanPlaySummonedXyz(choice);
        }
        finally
        {
            choice.Materials = previousMaterials;
        }
    }

    private static IEnumerable<CardModel> ExcludeMaterials(
        IEnumerable<CardModel> candidates,
        IEnumerable<CardModel> excluded)
    {
        var excludedSet = excluded.ToHashSet(new ReferenceComparer<CardModel>());
        return candidates.Where(card => !excludedSet.Contains(card));
    }

    private static bool HaveSameMaterialReferences(
        IReadOnlyList<CardModel> left,
        IReadOnlyList<CardModel> right)
    {
        if (left.Count != right.Count)
            return false;

        var leftSet = left.ToHashSet(new ReferenceComparer<CardModel>());
        return leftSet.Count == left.Count && leftSet.SetEquals(right);
    }

    private static IReadOnlyList<CardModel> ChooseAllFieldExtraDeckMonsters(
        IReadOnlyList<CardModel> choices) =>
        choices
            .Where(card => MonsterIdentity.Matches<XyzMonsterCard>(card) && MonsterFieldService.IsOnField(card))
            .ToList();

    private IReadOnlyList<CardModel> ChooseRaSphereMaterials(IReadOnlyList<CardModel> choices)
    {
        var materials = choices.Where(MonsterFieldService.IsOnField).ToList();
        var capacity = MonsterFieldService.GetCapacity(Owner);
        return capacity > 0 && materials.Count == capacity
            ? materials
            : [];
    }

    private static bool IsRequiredChimeratechMaterial(CardModel card) =>
        MonsterIdentity.Matches<CyberDragon>(card) && MonsterFieldService.IsOnField(card);

    private static bool IsRequiredCyberDragonInfinityMaterial(CardModel card) =>
        MonsterIdentity.Matches<CyberDragon>(card) && MonsterFieldService.IsOnField(card);

    private bool CanChooseRelinquished() =>
        TryGetRelinquishedFieldMaterial(null, out _);

    private bool CanChooseRaSphereMode()
    {
        var capacity = MonsterFieldService.GetCapacity(Owner);
        return capacity > 0 && MonsterFieldService.GetMonsters(Owner).Count == capacity;
    }

    private bool TryGetRelinquishedFieldMaterial(
        IReadOnlyList<CardModel> materialChoices,
        out CardModel fieldMonster)
    {
        fieldMonster = null;

        var fieldMonsters = MonsterFieldService.GetMonsters(Owner).ToList();
        if (fieldMonsters.Count != Relinquished.RequiredMaterials)
            return false;

        var onlyFieldMonster = fieldMonsters[0];
        if (materialChoices is not null
            && !materialChoices.Any(choice => ReferenceEquals(choice, onlyFieldMonster)))
        {
            return false;
        }

        if (Owner?.Creature?.CombatState?.HittableEnemies.Any(Relinquished.IsMinion) != true)
            return false;

        fieldMonster = onlyFieldMonster;
        return true;
    }

    private async Task GainCircuitTalismanConsumedBlock(int block)
    {
        Flash();
        await ThermalVortexCommandCompat.GainBlockFromCardEffect(
            Owner.Creature, block, ValueProp.Unpowered, null, false);
    }
}
