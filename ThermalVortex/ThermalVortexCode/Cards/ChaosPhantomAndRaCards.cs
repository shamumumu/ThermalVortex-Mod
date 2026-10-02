using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using System.Reflection;
using System.Runtime.ExceptionServices;
using ThermalVortex.ThermalVortexCode.Character;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.Commands;
using ThermalVortex.ThermalVortexCode.Vfx;
using ThermalVortex.ThermalVortexCode.Patches;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class ChaosPhantom : MonsterCard,
    IMonsterFieldEnterResolvedListener,
    IMonsterFieldLeaveListener,
    IMonsterFieldLeaveResolvedListener,
    IMonsterFieldAttackTargetRule
{
    public override string CustomPortraitPath => "chaos_phantom.png".BigCardImagePath();
    public override string PortraitPath => "chaos_phantom.png".CardImagePath();
    public override string BetaPortraitPath => "chaos_phantom.png".CardImagePath();

    private int? _copiedMaxHp;
    private CardType? _copiedType;
    private TargetType? _copiedTargetType;
    private string _copiedTitle;
    private ChaosPhantomCopyState _copiedState;
    private HashSet<CardKeyword> _keywordsBeforeCopy;
    private long _copyGeneration;
    private bool _copyEnteredField;
    private bool _inspectionShowsCopy;
    private MonsterFieldHealth? _inspectionHealth;
    private CopyLeaveScope _leavingCopy;

    public override int MonsterMaxHp => Math.Max(1, _copiedMaxHp ?? 1);
    public override string Title => _copiedTitle ?? base.Title;
    public override CardType Type => _copiedType ?? base.Type;
    public override TargetType TargetType => _copiedTargetType ?? TargetType.Self;
    public bool CanBeAttackTarget => _copiedState?.CanBeAttackTarget(this) ?? true;
    internal CardModel CopiedMonsterForIdentity => _copiedState?.EffectSource;

    public ChaosPhantom() : base(3, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
        WithTips(card => ((ChaosPhantom)card).CreateCopiedMonsterTips());
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || CardSelectionHelper.DiscardPileMonsters(Owner)
            .Any(card => !ReferenceEquals(card, this)
                && (card is not ChaosPhantom phantom || phantom._copiedState is not null)));

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var owner = Owner;
        var visited = new HashSet<CardModel>(new ReferenceComparer<CardModel>()) { this };
        CardModel selected;
        while (true)
        {
            var candidates = CardSelectionHelper.DiscardPileMonsters(owner)
                .Where(card => !visited.Contains(card)).ToList();
            selected = await CardSelectionHelper.ChooseOne(
                ctx, owner, candidates, "THERMALVORTEX-CHAOS_PHANTOM.selectionPrompt", false);
            if (selected is null
                || !visited.Add(selected)
                || !CardSelectionHelper.IsCurrentPileCard(owner, selected, PileType.Discard))
            {
                await ResolveFailedCopy(ctx);
                return;
            }

            if (selected is not ChaosPhantom { _copiedState: null })
                break;
        }

        if (visited.Any(card => !ReferenceEquals(card, this)
            && !CardSelectionHelper.IsCurrentPileCard(owner, card, PileType.Discard)))
        {
            await ResolveFailedCopy(ctx);
            return;
        }

        var generation = _copyGeneration + 1;
        var summoned = false;
        try
        {
            await RelinquishedControlPower.ReleaseForCopyReset(this);
            generation = CopyAttributes(selected);
            await PhoenixRevivalPower.Synchronize(ctx, Owner);
            summoned = _copiedState is not null && await ChaosPhantomCopyService.Resolve(
                ctx, this, _copiedState.EffectSource, play);
        }
        finally
        {
            if (!summoned)
            {
                await RelinquishedControlPower.ReleaseForCopyReset(this);
                ClearCopiedState(generation);
                await PhoenixRevivalPower.Synchronize(ctx, Owner);
                await ResolveFailedCopy(ctx);
            }
        }
    }

    private async Task ResolveFailedCopy(PlayerChoiceContext ctx)
    {
        if (MonsterFieldService.IsOnField(this))
            return;

        MonsterFieldService.UnmarkPending(this);
        MonsterFieldUpgradeLockService.CancelAwaitingFieldEntry(this);
        // The native wrapper cached the result pile before OnPlay. Moving the
        // failed card out of Play prevents that cached field result from firing.
        if (Pile?.Type == PileType.Play)
            await MonsterFieldService.SendUsedCardToDiscardOrExhaust(ctx, this, this);
    }

    protected override void AddExtraArgsToDescription(LocString locString)
    {
        base.AddExtraArgsToDescription(locString);
        locString.Add("HasCopiedMonster", _copiedState is not null);
        locString.Add("ShowCopiedMonster", ShouldShowCopy);
        locString.Add("CopiedMonster", _copiedTitle ?? string.Empty);
        var health = MonsterFieldService.IsOnField(this)
            ? MonsterFieldHealthService.PeekHealth(this)
            : _inspectionHealth.GetValueOrDefault();
        if (health.IsValid)
        {
            locString.Add("HasPreviewMonsterHp", true);
            locString.Add("PreviewMonsterHp", health.MaxHp);
        }
    }

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _copiedState = _copiedState?.Clone();
        _keywordsBeforeCopy = _keywordsBeforeCopy is null ? null : [.. _keywordsBeforeCopy];
        _leavingCopy = null;
        _copyEnteredField = false;
    }

    internal void AttachInspectionSource(ChaosPhantom source)
    {
        _inspectionShowsCopy = source?._copiedState is not null
            && (MonsterFieldService.IsOnField(source) || source._inspectionShowsCopy);
        _inspectionHealth = _inspectionShowsCopy
            ? source._inspectionHealth ?? MonsterFieldHealthService.PeekHealth(source)
            : null;
    }

    private bool ShouldShowCopy => _copiedState is not null
        && (MonsterFieldService.IsOnField(this) || _inspectionShowsCopy);

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel source)
    {
        if (ReferenceEquals(card, this)
            && oldPileType == MonsterFieldPile.FieldPileType
            && card.Pile?.Type != MonsterFieldPile.FieldPileType
            && _leavingCopy is not null)
        {
            _leavingCopy.Departed = true;
        }

        if (ReferenceEquals(card, this)
            && _copiedState is not null
            && !IsCopyLeaving(_copiedState)
            && card.Pile?.Type is PileType.Hand or PileType.Draw or PileType.Discard or PileType.Exhaust)
        {
            await RelinquishedControlPower.ReleaseForCopyReset(this);
            ClearCopiedState(_copyGeneration);
            await PhoenixRevivalPower.Synchronize(null, Owner);
        }
    }

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        if (!ReferenceEquals(enterEvent.Card, this)
            || _copiedState is null
            || _copyEnteredField
            || IsCopyLeaving(_copiedState))
            return;

        _copyEnteredField = true;
        await _copiedState.AfterSummoned(ctx, this);
        await PhoenixRevivalPower.Synchronize(ctx, Owner);
    }

    public Task AfterMonsterLeftField(
        PlayerChoiceContext ctx,
        MonsterFieldLeaveEvent leaveEvent) =>
        ResolveCopiedLeaving(ctx, leaveEvent, resolved: false);

    public Task AfterMonsterLeftFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldLeaveEvent leaveEvent) =>
        ResolveCopiedLeaving(ctx, leaveEvent, resolved: true);

    private Task ResolveCopiedLeaving(
        PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent, bool resolved)
    {
        if (!ReferenceEquals(leaveEvent.Card, this))
            return Task.CompletedTask;

        var leaving = _leavingCopy;
        var state = leaving?.State ?? _copiedState;
        if (state is null)
            return Task.CompletedTask;

        if (leaving is not null)
        {
            if (resolved ? leaving.ResolvedInvoked : leaving.LeavingInvoked)
                return Task.CompletedTask;

            if (resolved)
                leaving.ResolvedInvoked = true;
            else
                leaving.LeavingInvoked = true;
        }

        return resolved
            ? state.AfterLeftField(ctx, this, leaveEvent)
            : state.BeforeLeftField(ctx, this, leaveEvent);
    }

    internal Task TriggerCopiedUpkeep(PlayerChoiceContext ctx) =>
        _copiedState?.OnUpkeep(ctx, this) ?? Task.CompletedTask;

    internal bool IsCompleteCopyOf<T>() =>
        IsCompleteCopyOf(typeof(T));

    internal bool IsCompleteCopyOf(Type type) =>
        type is not null && type.IsInstanceOfType(_copiedState?.EffectSource);

    internal bool HasCompleteCopyEffect<TEffect>() where TEffect : ICyberCopyableEffect =>
        _copiedState?.HasEffect<TEffect>() == true;

    internal bool HasCompleteCopyEffect<TEffect>(Func<TEffect, bool> predicate)
        where TEffect : ICyberCopyableEffect =>
        _copiedState?.HasEffect(predicate) == true;

    internal int CompleteCopyInheritedAttack => _copiedState?.InheritedAttack ?? 0;
    internal int CompleteCopyAttackContribution => _copiedState?.TotalAttackContribution ?? 0;
    internal bool CopiedMonsterWasUpgraded => _copiedState?.SourceWasUpgraded == true;

    internal Task SynchronizeCompleteCopyPersistentEffects(PlayerChoiceContext ctx) =>
        _copiedState?.SynchronizePersistentEffects(ctx, this) ?? Task.CompletedTask;

    internal bool HasActiveCompleteCopy => _copiedState is not null && IsActiveCompleteCopy(_copiedState);

    internal IEnumerable<ICyberCopyableEffect> CreateCompleteCopyDevourEffects() =>
        _copiedState?.CreateDevourEffects() ?? Array.Empty<ICyberCopyableEffect>();

    internal void SetCompleteCopyMaxHp(int maxHp) =>
        _copiedMaxHp = Math.Max(1, maxHp);

    internal Task ResolveCopiedSummon(CardPlay play, bool showManualSummonVfx) =>
        ResolveMonsterSummon(play, showManualSummonVfx);

    internal ChaosPhantomCopyState CloneCompleteCopyState() => _copiedState?.Clone();

    internal RevivalCopyState CaptureRevivalCopy() =>
        IsCompleteCopyOf<WingedDragonOfRaPhoenix>() ? new RevivalCopyState(this) : null;

    // A revival restores the departing form, not a new copy of its original
    // source. The physical body's own devour ledger remains on this card.
    internal sealed class RevivalCopyState : IDisposable
    {
        private readonly ChaosPhantom _host;
        private readonly long _departingGeneration;
        private readonly int? _maxHp;
        private readonly CardType? _type;
        private readonly TargetType? _targetType;
        private readonly string _title;
        private readonly HashSet<CardKeyword> _keywords;
        private readonly HashSet<CardKeyword> _originalKeywords;
        private ChaosPhantomCopyState _state;
        private long? _restoredGeneration;

        internal RevivalCopyState(ChaosPhantom host)
        {
            _host = host;
            _departingGeneration = host._copyGeneration;
            _maxHp = host._copiedMaxHp;
            _type = host._copiedType;
            _targetType = host._copiedTargetType;
            _title = host._copiedTitle;
            _keywords = host.Keywords.ToHashSet();
            _originalKeywords = host._keywordsBeforeCopy?.ToHashSet();
            _state = host._copiedState?.Clone();
        }

        internal bool CanRestore => _state is not null
            && _host._copiedState is null
            && _host._copyGeneration == _departingGeneration + 1;

        internal bool IsRestoredCopyCurrent => _restoredGeneration is { } generation
            && _host._copyGeneration == generation && _host._copiedState is not null;

        internal bool TryRestore()
        {
            if (!CanRestore)
                return false;

            _host._copiedState = _state;
            _state = null; // The host now owns and releases the cloned form.
            _host._copiedMaxHp = _maxHp;
            _host._copiedType = _type;
            _host._copiedTargetType = _targetType;
            _host._copiedTitle = _title;
            _host._keywordsBeforeCopy = _originalKeywords?.ToHashSet();
            _host._copyGeneration++;
            _restoredGeneration = _host._copyGeneration;
            _host._copyEnteredField = false;
            _host._inspectionShowsCopy = false;
            _host._inspectionHealth = null;
            foreach (var keyword in _host.Keywords.ToList())
                _host.RemoveKeyword(keyword);
            foreach (var keyword in _keywords)
                _host.AddKeyword(keyword);
            return true;
        }

        internal void RollbackUnenteredCopy()
        {
            if (_restoredGeneration is { } generation)
                _host.ClearCopiedState(generation);
        }

        public void Dispose()
        {
            _state?.Release();
            _state = null;
        }
    }

    internal bool IsActiveCompleteCopy(ChaosPhantomCopyState state) =>
        ReferenceEquals(_copiedState, state)
        && !IsCopyLeaving(state)
        && MonsterFieldService.IsOnField(this);

    private long CopyAttributes(CardModel selected)
    {
        RelinquishedControlPower.DetachForCopyReset(this);
        var previous = _copiedState;
        var captured = ChaosPhantomCopyState.Capture(selected);
        _keywordsBeforeCopy ??= Keywords.ToHashSet();
        _copiedState = captured;
        if (previous is not null && !IsCopyLeaving(previous))
            previous.Release();

        _copyGeneration++;
        _copyEnteredField = false;
        _inspectionShowsCopy = false;
        _inspectionHealth = null;
        var copied = _copiedState?.EffectSource ?? selected;
        _copiedMaxHp = Math.Max(1, MonsterFieldHealthService.GetEffectiveMaxHpForCopy(selected));
        _copiedType = copied.Type;
        _copiedTargetType = copied.TargetType;
        _copiedTitle = copied.Title;

        foreach (var keyword in Keywords.ToList())
            RemoveKeyword(keyword);

        foreach (var keyword in copied.Keywords)
            AddKeyword(keyword);

        var effectiveMaxHp = MonsterFieldHealthService.GetInitialMaxHp(this);
        MonsterFieldHealthService.SetHealth(this, effectiveMaxHp, effectiveMaxHp);
        return _copyGeneration;
    }

    private void ClearCopiedState(long generation)
    {
        if (_copyGeneration != generation)
            return;

        RelinquishedControlPower.DetachForCopyReset(this);
        var state = _copiedState;
        _copiedState = null;
        _copiedMaxHp = null;
        _copiedType = null;
        _copiedTargetType = null;
        _copiedTitle = null;
        _copyEnteredField = false;
        _inspectionShowsCopy = false;
        _inspectionHealth = null;
        _copyGeneration++;
        if (_keywordsBeforeCopy is not null)
        {
            foreach (var keyword in Keywords.ToList())
                RemoveKeyword(keyword);
            foreach (var keyword in _keywordsBeforeCopy)
                AddKeyword(keyword);
            _keywordsBeforeCopy = null;
        }

        MonsterFieldService.UnmarkPending(this);
        MonsterFieldHealthService.ForgetCopyMaxHp(this);
        if (MonsterFieldService.IsOnField(this))
        {
            var maxHp = MonsterFieldHealthService.GetInitialMaxHp(this);
            MonsterFieldHealthService.SetHealth(this, maxHp, maxHp);
        }
        else
        {
            MonsterFieldHealthService.Clear(this);
        }

        if (state is not null && !IsCopyLeaving(state))
            state.Release();
    }

    internal static IDisposable BeginFieldLeave(CardModel card)
    {
        if (card is not ChaosPhantom phantom || phantom._copiedState is null)
            return EmptyCopyLeaveScope.Instance;

        var current = phantom._leavingCopy;
        if (current is not null && current.Generation == phantom._copyGeneration)
        {
            current.Depth++;
            return new CopyLeaveHandle(phantom, current);
        }

        var scope = new CopyLeaveScope(phantom._copiedState, phantom._copyGeneration, current);
        phantom._leavingCopy = scope;
        return new CopyLeaveHandle(phantom, scope);
    }

    private bool IsCopyLeaving(ChaosPhantomCopyState state)
    {
        for (var scope = _leavingCopy; scope is not null; scope = scope.Previous)
            if (ReferenceEquals(scope.State, state))
                return true;
        return false;
    }

    private void FinishFieldLeave(CopyLeaveScope scope)
    {
        if (--scope.Depth > 0)
            return;

        var abortedBeforeDeparture = !scope.Departed && MonsterFieldService.IsOnField(this);
        if (ReferenceEquals(_copiedState, scope.State))
        {
            if (!abortedBeforeDeparture)
                ClearCopiedState(scope.Generation);
        }
        if (!ReferenceEquals(_copiedState, scope.State))
            scope.State.Release();
        if (ReferenceEquals(_leavingCopy, scope))
            _leavingCopy = scope.Previous;
    }

    private IEnumerable<IHoverTip> CreateCopiedMonsterTips()
    {
        var preview = ShouldShowCopy ? _copiedState?.CreatePreview(this) : null;
        if (preview is not null)
            yield return new CardHoverTip(preview);
    }

    private sealed class CopyLeaveScope(ChaosPhantomCopyState state, long generation, CopyLeaveScope previous)
    {
        internal ChaosPhantomCopyState State { get; } = state;
        internal long Generation { get; } = generation;
        internal CopyLeaveScope Previous { get; } = previous;
        internal int Depth { get; set; } = 1;
        internal bool LeavingInvoked { get; set; }
        internal bool ResolvedInvoked { get; set; }
        internal bool Departed { get; set; }
    }

    private sealed class CopyLeaveHandle(ChaosPhantom host, CopyLeaveScope scope) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            host.FinishFieldLeave(scope);
        }
    }

    private sealed class EmptyCopyLeaveScope : IDisposable
    {
        internal static readonly EmptyCopyLeaveScope Instance = new();
        public void Dispose() { }
    }
}

internal static class ChaosPhantomCopyService
{
    private static readonly MethodInfo CardOnPlay = typeof(CardModel).GetMethod(
        "OnPlay",
        BindingFlags.Instance | BindingFlags.NonPublic,
        null,
        [typeof(PlayerChoiceContext), typeof(CardPlay)],
        null);
    private static readonly AsyncLocal<CopyExecutionScope> CurrentExecution = new();

    internal static async Task<bool> Resolve(
        PlayerChoiceContext ctx,
        ChaosPhantom host,
        CardModel selected,
        CardPlay play)
    {
        if (host?.Owner is null || selected is null)
            return false;

        var target = await EffectTargeting.ResolveForAutoPlay(ctx, host.Owner, selected, play.Target);
        if (target is null)
            return false;

        var copiedPlay = new CardPlay
        {
            Card = host,
            Target = target,
            ResultPile = play.ResultPile,
            Resources = play.Resources,
            IsAutoPlay = play.IsAutoPlay,
            PlayIndex = play.PlayIndex,
            PlayCount = play.PlayCount
        };
        var execution = new CopyExecutionScope(host, selected, CurrentExecution.Value);
        CurrentExecution.Value = execution;
        try
        {
            if (selected is not MonsterCard)
            {
                execution.SummonRequested = true;
                await host.ResolveCopiedSummon(copiedPlay, showManualSummonVfx: false);
            }

            var invocation = CardOnPlay?.Invoke(selected, [ctx, copiedPlay]) as Task;
            if (invocation is not null)
                await invocation;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
        finally
        {
            CurrentExecution.Value = execution.Previous;
        }

        return execution.SummonRequested;
    }

    internal static async Task<bool> TryResolveCopiedMonsterSummon(
        CardModel card, CardPlay play, bool showManualSummonVfx)
    {
        var execution = CurrentExecution.Value;
        if (execution is null || !ReferenceEquals(execution.Card, card))
            return false;

        if (execution.SummonRequested)
            return true;

        execution.SummonRequested = true;
        await execution.Host.ResolveCopiedSummon(play, showManualSummonVfx);
        return true;
    }

    internal static CardModel ResolveEffectHost(CardModel card)
    {
        var execution = CurrentExecution.Value;
        return execution is not null && ReferenceEquals(execution.Card, card)
            ? execution.Host
            : card;
    }

    private sealed class CopyExecutionScope(
        ChaosPhantom host,
        CardModel card,
        CopyExecutionScope previous)
    {
        internal ChaosPhantom Host { get; } = host;
        internal CardModel Card { get; } = card;
        internal CopyExecutionScope Previous { get; } = previous;
        internal bool SummonRequested { get; set; }
    }
}

public interface IChaosPhantomCopyableEffectProvider
{
    IEnumerable<ICyberCopyableEffect> CreateChaosPhantomCopyableEffects();
}

internal sealed class ChaosPhantomCopyState
{
    private CardModel _effectSource;
    private List<ICyberCopyableEffect> _effects;
    private bool _delegateNativeLifecycle;
    private int _inheritedAttack;
    private bool _sourceWasUpgraded;

    private ChaosPhantomCopyState(
        CardModel effectSource,
        IEnumerable<ICyberCopyableEffect> effects,
        bool delegateNativeLifecycle,
        int inheritedAttack,
        bool sourceWasUpgraded)
    {
        _effectSource = effectSource;
        _effects = effects?.Where(effect => effect is not null).ToList() ?? [];
        _delegateNativeLifecycle = delegateNativeLifecycle;
        _inheritedAttack = Math.Max(0, inheritedAttack);
        _sourceWasUpgraded = sourceWasUpgraded;
    }

    internal CardModel EffectSource => _effectSource;
    internal int InheritedAttack => _inheritedAttack;
    internal int TotalAttackContribution
    {
        get
        {
            if (_effectSource is null)
                return _inheritedAttack;

            var snapshotInherited = CyberDevourState.GetStoredInheritedAttackContribution(_effectSource);
            return Math.Max(
                0,
                CyberDevourState.GetTotalAttackContribution(_effectSource)
                    + _inheritedAttack
                    - snapshotInherited);
        }
    }
    internal bool SourceWasUpgraded => _sourceWasUpgraded;

    internal static ChaosPhantomCopyState Capture(CardModel source)
    {
        if (source is ChaosPhantom phantom)
        {
            var copied = phantom.CloneCompleteCopyState();
            // A complete copy borrows the source body's own combat growth as
            // part of its temporary form. A replay clone does not use Capture:
            // it clones this form and the physical body's growth separately.
            copied?.AddBorrowedGrowth(CyberDevourState.CaptureCombatSnapshot(source));
            copied?.PrepareForCopyPlay();
            return copied;
        }

        if (source?.MutableClone() is not CardModel snapshot)
            return null;

        CyberDevourState.CopyDynamicStateForCompleteCopy(source, snapshot);
        var effects = CyberDevourState.CreateEffectsForCompleteCopy(source).ToList();
        if (source is IChaosPhantomCopyableEffectProvider completeProvider)
        {
            effects.AddRange(completeProvider.CreateChaosPhantomCopyableEffects()
                .Select(effect => effect?.Clone())
                .Where(effect => effect is not null));
        }

        var state = new ChaosPhantomCopyState(
            snapshot,
            effects,
            source is not ICyberCopyableEffectProvider
                && source is not IChaosPhantomCopyableEffectProvider,
            CyberDevourState.GetStoredInheritedAttackContribution(source),
            source.CurrentUpgradeLevel > 0);
        state.PrepareForCopyPlay();
        return state;
    }

    private void PrepareForCopyPlay()
    {
        CyberDevourState.SetCompleteCopyInheritedAttack(_effectSource, _inheritedAttack);
        if (_effectSource is PhantomSummoningGodExodia god)
            god.ResetExecutionForCompleteCopy();
    }

    internal void Release()
    {
        CyberDevourState.ReleaseCompleteCopySnapshot(_effectSource);
        _effectSource = null;
        _effects.Clear();
        _inheritedAttack = 0;
    }

    internal ChaosPhantomCopyState Clone()
    {
        if (_effectSource?.MutableClone() is not CardModel clonedSource)
            return null;

        CyberDevourState.CopyDynamicStateForCompleteCopy(_effectSource, clonedSource);
        return new ChaosPhantomCopyState(
            clonedSource,
            _effects.Select(effect => effect?.Clone()),
            _delegateNativeLifecycle,
            _inheritedAttack,
            _sourceWasUpgraded);
    }

    internal CardModel CreatePreview(ChaosPhantom host)
    {
        if (_effectSource is null)
            return null;
        var preview = CyberDevourState.CreateInspectionSnapshot(_effectSource);
        var borrowed = CyberDevourState.CaptureDisplaySnapshot(_effectSource);
        var own = CyberDevourState.CaptureDisplaySnapshot(host);
        var combined = new CyberDevourCombatSnapshot(
            (borrowed?.MaxHpBonus ?? 0) + (own?.MaxHpBonus ?? 0),
            (borrowed?.CurrentHpBonus ?? 0) + (own?.CurrentHpBonus ?? 0),
            (borrowed?.AttackContribution ?? 0) + (own?.AttackContribution ?? 0),
            (borrowed?.Effects ?? []).Concat(own?.Effects ?? []),
            (borrowed?.Records ?? []).Concat(own?.Records ?? []));
        CyberDevourState.AttachCombatPreviewSnapshot(preview, combined);
        return preview;
    }

    internal bool CanBeAttackTarget(CardModel host) =>
        !_effects.Any(effect => effect.PreventsEnemyAttackTarget(host))
        && (_effectSource is not IMonsterFieldAttackTargetRule rule || rule.CanBeAttackTarget);

    internal bool HasEffect<TEffect>() where TEffect : ICyberCopyableEffect =>
        _effects.Any(effect => effect is TEffect);

    internal bool HasEffect<TEffect>(Func<TEffect, bool> predicate)
        where TEffect : ICyberCopyableEffect =>
        _effects.OfType<TEffect>().Any(predicate);

    internal IEnumerable<ICyberCopyableEffect> CreateDevourEffects() =>
        _effects
            // These transitions belong to complete copying, and are not
            // devourable effects of the original Herz or Ra monsters either.
            .Where(effect => effect is not ChaosPhantomHerzTransitionEffect
                && effect is not ChaosPhantomRaTransitionEffect
                && effect is not ChaosPhantomSleepingTabletTransitionEffect)
            .Select(effect => effect.Clone());

    private void AddBorrowedGrowth(CyberDevourCombatSnapshot growth)
    {
        if (growth is null)
            return;
        _inheritedAttack += growth.AttackContribution;
        _effects.AddRange(growth.Effects.Select(effect => effect.Clone()));
        CyberDevourState.AddSnapshotGrowth(_effectSource, growth);
    }

    internal Task SynchronizePersistentEffects(PlayerChoiceContext ctx, ChaosPhantom host) =>
        CyberDevourState.SynchronizePersistentEffects(
            ctx, host, _effects.Concat(CyberDevourState.GetOwnInheritedEffects(host)),
            () => host.IsActiveCompleteCopy(this));

    internal async Task AfterSummoned(PlayerChoiceContext ctx, ChaosPhantom host)
    {
        if (!host.IsActiveCompleteCopy(this))
            return;
        using var effectSource = AshBlossomActionNegation.EnterSource(host);
        await SynchronizePersistentEffects(ctx, host);
        foreach (var effect in _effects.ToList())
        {
            if (!host.IsActiveCompleteCopy(this))
                return;
            await effect.OnCyberSummoned(ctx, host, host);
        }

        if (host.IsActiveCompleteCopy(this)
            && _delegateNativeLifecycle
            && _effectSource is IMonsterFieldEnterResolvedListener listener)
        {
            await listener.AfterMonsterEnteredFieldResolved(
                ctx,
                new MonsterFieldEnterEvent(host, MonsterFieldHealthService.PeekHealth(host)));
        }
    }

    internal async Task OnUpkeep(PlayerChoiceContext ctx, ChaosPhantom host)
    {
        if (!host.IsActiveCompleteCopy(this))
            return;
        using var effectSource = AshBlossomActionNegation.EnterSource(host);
        await SynchronizePersistentEffects(ctx, host);
        foreach (var effect in _effects.ToList())
        {
            if (!host.IsActiveCompleteCopy(this))
                return;
            await effect.OnCyberUpkeep(ctx, host, host);
        }
    }

    internal async Task BeforeLeftField(
        PlayerChoiceContext ctx,
        ChaosPhantom host,
        MonsterFieldLeaveEvent leaveEvent)
    {
        using var effectSource = AshBlossomActionNegation.EnterSource(host);
        var isMaterialUseValid = leaveEvent.WasUsedAsMaterial
            ? MonsterFieldService.CaptureMaterialUseValidity(host.Owner)
            : null;
        foreach (var effect in _effects.ToList())
        {
            if (isMaterialUseValid?.Invoke() == false)
                return;
            await effect.OnCyberLeavingField(ctx, host, leaveEvent);
            if (isMaterialUseValid?.Invoke() == false)
                return;
        }

        if (isMaterialUseValid?.Invoke() == false)
            return;
        if (_delegateNativeLifecycle && _effectSource is IMonsterFieldLeaveListener listener)
            await listener.AfterMonsterLeftField(ctx, leaveEvent);
    }

    internal async Task AfterLeftField(
        PlayerChoiceContext ctx,
        ChaosPhantom host,
        MonsterFieldLeaveEvent leaveEvent)
    {
        using var effectSource = AshBlossomActionNegation.EnterSource(host);
        var isMaterialUseValid = leaveEvent.WasUsedAsMaterial
            ? MonsterFieldService.CaptureMaterialUseValidity(host.Owner)
            : null;
        foreach (var effect in _effects.ToList())
        {
            if (isMaterialUseValid?.Invoke() == false)
                return;
            await effect.OnCyberLeftFieldResolved(ctx, host, leaveEvent);
            if (isMaterialUseValid?.Invoke() == false)
                return;
        }

        if (isMaterialUseValid?.Invoke() == false)
            return;
        if (_delegateNativeLifecycle && _effectSource is IMonsterFieldLeaveResolvedListener listener)
            await listener.AfterMonsterLeftFieldResolved(ctx, leaveEvent);
    }
}

internal sealed class ChaosPhantomSleepingTabletTransitionEffect(bool choosePiece) : ICyberCopyableEffect
{
    public ICyberCopyableEffect Clone() => new ChaosPhantomSleepingTabletTransitionEffect(choosePiece);

    // This transition belongs to complete copying, never to devour inheritance.
    public string GetDevourDisplayText() => string.Empty;

    public async Task OnCyberSummoned(PlayerChoiceContext ctx, CardModel host, AbstractModel source)
    {
        var owner = host?.Owner?.Creature;
        if (owner is null || !MonsterFieldService.IsOnField(host))
            return;

        if (owner.GetPower<MillenniumSleepingTabletPower>() is null)
            await ThermalVortexCommandCompat.ApplyPower<MillenniumSleepingTabletPower>(ctx, owner, 1, owner, host, false);

        owner.GetPower<MillenniumSleepingTabletPower>()?.Bind(host, choosePiece);
    }

    public Task OnCyberUpkeep(PlayerChoiceContext ctx, CardModel host, AbstractModel source) =>
        Task.CompletedTask;
}

internal sealed class ChaosPhantomHerzTransitionEffect(bool copiedUpgraded) : ICyberCopyableEffect
{
    private readonly bool _copiedUpgraded = copiedUpgraded;

    public ICyberCopyableEffect Clone() =>
        new ChaosPhantomHerzTransitionEffect(_copiedUpgraded);

    public string GetDevourDisplayText() => string.Empty;

    public async Task OnCyberSummoned(
        PlayerChoiceContext ctx,
        CardModel host,
        AbstractModel source)
    {
        var owner = host?.Owner?.Creature;
        if (owner is null)
            return;

        if (!owner.HasPower<CyberDragonHerzPower>())
            await ThermalVortexCommandCompat.ApplyPower<CyberDragonHerzPower>(ctx, owner, 1, owner, host, false);

        owner.GetPower<CyberDragonHerzPower>()?.BindCompleteCopy(host, _copiedUpgraded);
    }

    public Task OnCyberUpkeep(
        PlayerChoiceContext ctx,
        CardModel host,
        AbstractModel source) => Task.CompletedTask;
}

internal sealed class ChaosPhantomRaTransitionEffect(RaTransitionTarget target) : ICyberCopyableEffect
{
    private readonly RaTransitionTarget _target = target;

    public ICyberCopyableEffect Clone() => new ChaosPhantomRaTransitionEffect(_target);

    public string GetDevourDisplayText() => string.Empty;

    public async Task OnCyberSummoned(
        PlayerChoiceContext ctx,
        CardModel host,
        AbstractModel source)
    {
        var owner = host?.Owner?.Creature;
        if (owner is null)
            return;

        if (!owner.HasPower<RaTransitionPower>())
            await ThermalVortexCommandCompat.ApplyPower<RaTransitionPower>(ctx, owner, 1, owner, host, false);

        owner.GetPower<RaTransitionPower>()?.Bind(host, _target);
    }

    public Task OnCyberUpkeep(
        PlayerChoiceContext ctx,
        CardModel host,
        AbstractModel source) => Task.CompletedTask;
}

public class WingedDragonOfRaSphereMode : XyzMonsterCard,
    IMonsterFieldEnterResolvedListener,
    IChaosPhantomCopyableEffectProvider
{
    public const int BaseMaxHp = 1;
    private int _summonedMaxHp = BaseMaxHp;

    public override string CustomPortraitPath => "winged_dragon_of_ra_sphere_mode.png".BigCardImagePath();
    public override string PortraitPath => "winged_dragon_of_ra_sphere_mode.png".CardImagePath();
    public override string BetaPortraitPath => "winged_dragon_of_ra_sphere_mode.png".CardImagePath();
    public override int MinimumMaterials => 1;
    public override int MaximumMaterials => int.MaxValue;
    public override int MonsterMaxHp => CurrentUpgradeLevel > 0
        ? Math.Max(BaseMaxHp, _summonedMaxHp)
        : BaseMaxHp;

    public WingedDragonOfRaSphereMode() : base(CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            CardPreviewExplanation<XyzSummon>(),
            CardPreviewExplanation<WingedDragonOfRaPhoenix>(matchSourceUpgrade: false),
            KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    internal void CaptureMaterialHealth(IEnumerable<CardModel> materials)
    {
        if (CurrentUpgradeLevel <= 0)
            return;

        _summonedMaxHp = Math.Max(
            BaseMaxHp,
            materials?.Sum(card => MonsterFieldHealthService.PeekHealth(card).CurrentHp) ?? 0);
    }

    protected override Task OnPlay(PlayerChoiceContext ctx, CardPlay play) =>
        Task.CompletedTask;

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        await ThermalVortexCommandCompat.ApplyPower<RaTransitionPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
        Owner.Creature.GetPower<RaTransitionPower>()?.Bind(this, RaTransitionTarget.Phoenix);
    }

    public IEnumerable<ICyberCopyableEffect> CreateChaosPhantomCopyableEffects()
    {
        yield return new ChaosPhantomRaTransitionEffect(RaTransitionTarget.Phoenix);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public abstract class GeneratedColorlessMonsterCard(int cost, CardType type, CardRarity rarity, TargetType target) :
    MonsterCard(cost, type, rarity, target)
{
    public override int MaxUpgradeLevel => 0;
}

public class WingedDragonOfRaPhoenix : GeneratedColorlessMonsterCard,
    IMonsterFieldEnterResolvedListener,
    IChaosPhantomCopyableEffectProvider
{
    public const int BaseMaxHp = 1;
    public override string CustomPortraitPath => "winged_dragon_of_ra_phoenix.png".BigCardImagePath();
    public override string PortraitPath => "winged_dragon_of_ra_phoenix.png".CardImagePath();
    public override string BetaPortraitPath => "winged_dragon_of_ra_phoenix.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public WingedDragonOfRaPhoenix() : base(0, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            CardPreviewExplanation<WingedDragonOfRa>(matchSourceUpgrade: false));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        TcgMonsterCutinVfx.StartPreload(this);
        await ResolveMonsterSummon(play);
        await TcgMonsterCutinVfx.PlaySummonAsync(this, play);
    }

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        await ThermalVortexCommandCompat.ApplyPower<RaTransitionPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
        Owner.Creature.GetPower<RaTransitionPower>()?.Bind(this, RaTransitionTarget.Ra);
        await PhoenixRevivalPower.Synchronize(ctx, Owner);
    }

    public IEnumerable<ICyberCopyableEffect> CreateChaosPhantomCopyableEffects()
    {
        yield return new ChaosPhantomRaTransitionEffect(RaTransitionTarget.Ra);
    }
}

public class WingedDragonOfRa : GeneratedColorlessMonsterCard
{
    public const int BaseMaxHp = 1;
    public override string CustomPortraitPath => "winged_dragon_of_ra.png".BigCardImagePath();
    public override string PortraitPath => "winged_dragon_of_ra.png".CardImagePath();
    public override string BetaPortraitPath => "winged_dragon_of_ra.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public WingedDragonOfRa() : base(0, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
        WithFormulaDamage(
            1,
            1,
            (card, target) => ((WingedDragonOfRa)card).GetRecordedDamage(target),
            ValueProp.Unpowered);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        TcgMonsterCutinVfx.StartPreload(this);
        await ResolveMonsterSummon(play);
        await TcgMonsterCutinVfx.PlaySummonAsync(this, play);
        await DealRecordedEnemyDamage(ctx, Owner, this, play.Target);
    }

    internal static async Task DealRecordedEnemyDamage(
        PlayerChoiceContext ctx,
        MegaCrit.Sts2.Core.Entities.Players.Player owner,
        CardModel source,
        Creature target)
    {
        if (target?.IsEnemy != true)
            return;

        var enemies = owner?.Creature?.CombatState?.HittableEnemies.ToList();
        if (enemies is not { Count: > 0 })
            return;

        var damage = TryGetCore(owner)?.GetEnemyAttackDamageThisCombat(target) ?? 0;
        if (damage <= 0)
            return;

        var attacks = enemies
            .Select(enemy => ThermalVortexCombatVfx.CardAttack(source, enemy, damage, 1).Unpowered())
            .ToArray();
        using var mainOutput = CyberWeldingPower.BeginMainAttackOutput(source, attacks);
        foreach (var attack in attacks)
            await attack.Execute(ctx);
    }

    private static ThermalVortexCore TryGetCore(MegaCrit.Sts2.Core.Entities.Players.Player player)
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

    internal int GetRecordedDamage(Creature target)
    {
        return TryGetCore(TryGetOwner())?.GetEnemyAttackDamageThisCombat(target) ?? 0;
    }
}
