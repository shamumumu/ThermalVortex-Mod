using BaseLib.Config;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

internal sealed class RewardPoolConfig : SimpleModConfig
{
    [ConfigHideInUI]
    [ConfigIgnoreRestoreDefaults]
    public static string LastValidManualRewardPool { get; set; } = string.Empty;
}

public static class RewardPoolConfigRegistration
{
    private static readonly object RegistrationLock = new();

    public static void Initialize()
    {
        lock (RegistrationLock)
        {
            if (ModConfigRegistry.Get<RewardPoolConfig>() is not null)
                return;

            ModConfigRegistry.Register(MainFile.ModId, new RewardPoolConfig());
        }
    }
}

internal readonly record struct RewardPoolPendingContext(
    string CharacterId,
    long Generation,
    GameMode GameMode,
    NetGameType NetType)
{
    internal static RewardPoolPendingContext Unscoped { get; } =
        new(string.Empty, 0L, GameMode.None, NetGameType.None);

    internal bool IsScoped =>
        !string.IsNullOrWhiteSpace(CharacterId)
        && Generation > 0L
        && GameMode != GameMode.None
        && NetType != NetGameType.None;
}

internal sealed record PendingRewardPoolSetup(
    RewardPoolDefinition Definition,
    RewardPoolPendingContext Context)
{
    internal PendingRewardPoolSetup Snapshot() =>
        new((Definition ?? RewardPoolDefinition.Standard).Clone(), Context);
}

public static class RewardPoolSetupService
{
    private static readonly object StateLock = new();
    private static PendingRewardPoolSetup _pending;
    private static RewardPoolDefinition _lastValidManual;
    private static IRewardPoolLastBuildStore _lastBuildStore;
    private static bool _storeInitialized;
    private static bool _allowLegacyConfigMigration = true;
    private static bool _loggedInvalidLastDefinition;

    public static void Initialize()
    {
        RewardPoolConfigRegistration.Initialize();
        lock (StateLock)
            InitializeStoreLocked();
        RewardPoolPresetService.Initialize();
    }

    public static void UseStandard() =>
        UseStandard(RewardPoolPendingContext.Unscoped);

    internal static void UseStandard(RewardPoolPendingContext context)
    {
        var pending = new PendingRewardPoolSetup(RewardPoolDefinition.Standard, context);
        lock (StateLock)
            _pending = pending;
    }

    public static bool StageManual(
        RewardPoolDefinition definition,
        out RewardPoolValidationResult validation) =>
        StageManual(definition, RewardPoolPendingContext.Unscoped, out validation);

    internal static bool StageManual(
        RewardPoolDefinition definition,
        RewardPoolPendingContext context,
        out RewardPoolValidationResult validation)
    {
        if (!TryPrepareManualPending(definition, context, out var prepared, out validation))
            return false;
        var cachedLast = prepared.Definition.Clone();

        lock (StateLock)
        {
            InitializeStoreLocked();
            if (!_lastBuildStore.TryCommit(prepared.Definition, out validation))
                return false;

            // All allocations and validation happened before the durable commit.
            // Once the on-disk replacement succeeds, these assignments complete
            // the in-process transaction without exposing a partial setup.
            _lastValidManual = cachedLast;
            _pending = prepared;
            _loggedInvalidLastDefinition = false;
            MirrorLegacyConfigBestEffortLocked(_lastValidManual);
            return true;
        }
    }

    internal static bool StageManualTransient(
        RewardPoolDefinition definition,
        out RewardPoolValidationResult validation) =>
        StageManualTransient(
            definition,
            RewardPoolPendingContext.Unscoped,
            out validation);

    internal static bool StageManualTransient(
        RewardPoolDefinition definition,
        RewardPoolPendingContext context,
        out RewardPoolValidationResult validation)
    {
        if (!TryPrepareManualPending(definition, context, out var prepared, out validation))
            return false;

        lock (StateLock)
            _pending = prepared;
        return true;
    }

    public static bool TryStageLast(out RewardPoolValidationResult validation) =>
        TryStageLast(RewardPoolPendingContext.Unscoped, out validation);

    internal static bool TryStageLast(
        RewardPoolPendingContext context,
        out RewardPoolValidationResult validation)
    {
        if (!TryGetLast(out var definition, out validation))
            return false;

        var prepared = new PendingRewardPoolSetup(definition.Clone(), context);
        lock (StateLock)
            _pending = prepared;
        return true;
    }

    public static bool TryPeek(out RewardPoolDefinition definition)
    {
        if (TryPeekPending(out var pending))
        {
            definition = pending.Definition.Clone();
            return true;
        }

        definition = RewardPoolDefinition.Standard;
        return false;
    }

    internal static bool TryPeekPending(out PendingRewardPoolSetup setup)
    {
        lock (StateLock)
        {
            setup = _pending?.Snapshot();
            return setup is not null;
        }
    }

    public static bool TryConsume(out RewardPoolDefinition definition)
    {
        if (TryConsumePending(out var pending))
        {
            definition = pending.Definition.Clone();
            return true;
        }

        definition = RewardPoolDefinition.Standard;
        return false;
    }

    internal static bool TryConsumePending(out PendingRewardPoolSetup setup) =>
        TryConsumePending(_ => true, out setup);

    internal static bool TryConsumePending(
        Func<PendingRewardPoolSetup, bool> predicate,
        out PendingRewardPoolSetup setup)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        lock (StateLock)
        {
            var snapshot = _pending?.Snapshot();
            if (snapshot is null || !predicate(snapshot))
            {
                setup = null;
                return false;
            }

            _pending = null;
            setup = snapshot;
            return true;
        }
    }

    public static bool ValidatePending(out RewardPoolValidationResult validation)
    {
        lock (StateLock)
            return ValidatePendingLocked(null, out validation);
    }

    internal static bool ValidatePending(
        RewardPoolPendingContext expectedContext,
        out RewardPoolValidationResult validation)
    {
        lock (StateLock)
            return ValidatePendingLocked(expectedContext, out validation);
    }

    public static void Clear()
    {
        lock (StateLock)
            _pending = null;
    }

    public static bool TryGetLast(out RewardPoolDefinition definition) =>
        TryGetLast(out definition, out _);

    public static bool TryGetLast(
        out RewardPoolDefinition definition,
        out RewardPoolValidationResult validation)
    {
        EnsureConfigRegistered();
        lock (StateLock)
        {
            InitializeStoreLocked();
            if (_lastValidManual is not null)
            {
                definition = _lastValidManual.Clone();
                validation = definition.Validate();
                if (validation.IsValid && definition.IsManual)
                    return true;
            }

            definition = RewardPoolDefinition.Standard;
            validation = RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.NoSavedManualDefinition,
                "No saved manual reward-pool definition exists.");
            return false;
        }
    }

    public static bool SaveLast(
        RewardPoolDefinition definition,
        out RewardPoolValidationResult validation)
    {
        validation = ValidateManualDefinition(definition);
        if (!validation.IsValid)
            return false;

        var snapshot = definition.Clone();
        lock (StateLock)
        {
            InitializeStoreLocked();
            if (!_lastBuildStore.TryCommit(snapshot, out validation))
                return false;

            _lastValidManual = snapshot;
            _loggedInvalidLastDefinition = false;
            MirrorLegacyConfigBestEffortLocked(snapshot);
            return true;
        }
    }

    /// <summary>
    /// Replaces the durable store for a bounded diagnostic. Disposing the
    /// returned scope restores all previous service state.
    /// </summary>
    internal static IDisposable PushLastBuildStoreForTests(
        IRewardPoolLastBuildStore store,
        bool allowLegacyConfigMigration = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        lock (StateLock)
        {
            var previous = new StoreStateSnapshot(
                _lastBuildStore,
                _storeInitialized,
                _allowLegacyConfigMigration,
                _lastValidManual?.Clone(),
                _pending?.Snapshot(),
                _loggedInvalidLastDefinition);
            _lastBuildStore = store;
            _storeInitialized = false;
            _allowLegacyConfigMigration = allowLegacyConfigMigration;
            _lastValidManual = null;
            _pending = null;
            _loggedInvalidLastDefinition = false;
            InitializeStoreLocked();
            return new StoreOverrideScope(previous);
        }
    }

    private static bool TryPrepareManualPending(
        RewardPoolDefinition definition,
        RewardPoolPendingContext context,
        out PendingRewardPoolSetup prepared,
        out RewardPoolValidationResult validation)
    {
        validation = ValidateManualDefinition(definition);
        if (!validation.IsValid)
        {
            prepared = null;
            return false;
        }

        // Preallocate every snapshot before the disk commit so an allocation
        // failure cannot leave the persisted and pending states divergent.
        prepared = new PendingRewardPoolSetup(definition.Clone(), context);
        return true;
    }

    private static RewardPoolValidationResult ValidateManualDefinition(
        RewardPoolDefinition definition)
    {
        var validation = definition?.Validate()
            ?? RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.MissingDefinition,
                "The manual reward-pool definition is missing.");
        if (!validation.IsValid)
            return validation;

        return definition.IsManual
            ? RewardPoolValidationResult.Valid
            : RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.UnsupportedBuildMode,
                "A manual reward-pool operation requires a manual definition.");
    }

    private static bool ValidatePendingLocked(
        RewardPoolPendingContext? expectedContext,
        out RewardPoolValidationResult validation)
    {
        if (_pending is null)
        {
            validation = RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.MissingDefinition,
                "No pending reward-pool setup has been staged.");
            return false;
        }

        if (expectedContext.HasValue && _pending.Context != expectedContext.Value)
        {
            validation = RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.PendingContextMismatch,
                "The pending reward-pool setup belongs to a different character-select session.");
            return false;
        }

        validation = RewardPoolConstruction.ValidateForLaunch(_pending.Definition);
        return validation.IsValid;
    }

    private static void InitializeStoreLocked()
    {
        if (_storeInitialized)
            return;

        _lastBuildStore ??= AtomicRewardPoolLastBuildStore.CreateDefault();
        _storeInitialized = true;
        if (_lastBuildStore.TryLoad(out var stored, out var storedValidation)
            && stored.IsManual)
        {
            _lastValidManual = stored.Clone();
            _loggedInvalidLastDefinition = false;
            return;
        }

        var legacyValidation = RewardPoolValidationResult.Invalid(
            RewardPoolValidationCode.NoSavedManualDefinition,
            "No legacy BaseLib reward-pool definition exists.");
        if (_allowLegacyConfigMigration
            && !_lastBuildStore.HasStoredDefinition
            && RewardPoolDefinition.TryDeserialize(
                RewardPoolConfig.LastValidManualRewardPool,
                out var legacy,
                out legacyValidation)
            && legacy.IsManual)
        {
            if (_lastBuildStore.TryCommit(legacy, out var migrationValidation))
            {
                _lastValidManual = legacy.Clone();
                _loggedInvalidLastDefinition = false;
                MainFile.Logger.Info("Migrated legacy BaseLib reward-pool construction to atomic storage");
                return;
            }

            LogInvalidLastOnce(migrationValidation);
            return;
        }

        _lastValidManual = null;
        if (_lastBuildStore.HasStoredDefinition)
            LogInvalidLastOnce(storedValidation);
        else if (!string.IsNullOrWhiteSpace(RewardPoolConfig.LastValidManualRewardPool))
            LogInvalidLastOnce(legacyValidation);
    }

    private static void MirrorLegacyConfigBestEffortLocked(RewardPoolDefinition definition)
    {
        try
        {
            EnsureConfigRegistered();
            RewardPoolConfig.LastValidManualRewardPool = definition.Serialize();
            ModConfig.SaveDebounced<RewardPoolConfig>(0);
        }
        catch (Exception ex)
        {
            // Atomic storage is already committed. The BaseLib property is now
            // only a backward-compatible mirror, never the source of truth.
            MainFile.Logger.Info($"Reward-pool legacy config mirror failed error={ex}");
        }
    }

    private static void EnsureConfigRegistered()
    {
        try
        {
            RewardPoolConfigRegistration.Initialize();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"Reward-pool config registration failed error={ex}");
        }
    }

    private static void LogInvalidLastOnce(RewardPoolValidationResult validation)
    {
        if (_loggedInvalidLastDefinition)
            return;

        _loggedInvalidLastDefinition = true;
        MainFile.Logger.Info($"Saved reward-pool definition ignored validation={validation}");
    }

    private sealed record StoreStateSnapshot(
        IRewardPoolLastBuildStore Store,
        bool StoreInitialized,
        bool AllowLegacyConfigMigration,
        RewardPoolDefinition LastValidManual,
        PendingRewardPoolSetup Pending,
        bool LoggedInvalidLastDefinition);

    private sealed class StoreOverrideScope(StoreStateSnapshot previous) : IDisposable
    {
        private StoreStateSnapshot _previous = previous;

        public void Dispose()
        {
            lock (StateLock)
            {
                if (_previous is null)
                    return;

                _lastBuildStore = _previous.Store;
                _storeInitialized = _previous.StoreInitialized;
                _allowLegacyConfigMigration = _previous.AllowLegacyConfigMigration;
                _lastValidManual = _previous.LastValidManual?.Clone();
                _pending = _previous.Pending?.Snapshot();
                _loggedInvalidLastDefinition = _previous.LoggedInvalidLastDefinition;
                _previous = null;
            }
        }
    }
}
