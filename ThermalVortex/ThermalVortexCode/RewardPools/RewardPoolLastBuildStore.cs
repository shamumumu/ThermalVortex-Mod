using System.Text;
using System.Text.Json;
using Godot;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

internal interface IRewardPoolLastBuildStore
{
    bool HasStoredDefinition { get; }

    bool TryLoad(
        out RewardPoolDefinition definition,
        out RewardPoolValidationResult validation);

    bool TryCommit(
        RewardPoolDefinition definition,
        out RewardPoolValidationResult validation);
}

internal enum RewardPoolStoreFaultPoint
{
    Write,
    Flush,
    Replace,
    ReadBack
}

/// <summary>
/// Stores the last manual construction independently from BaseLib's general
/// configuration file. BaseLib's save API is fire-and-forget and truncates its
/// destination before writing, so it cannot be the commit point for a setup
/// transaction that promises to preserve the previous valid construction.
/// </summary>
internal sealed class AtomicRewardPoolLastBuildStore : IRewardPoolLastBuildStore
{
    internal const string DefaultFileName = "ThermalVortex.reward_pool.json";

    private readonly string _path;
    private readonly string _backupPath;
    private readonly RewardPoolStoreFaultPoint? _injectedFaultPoint;

    internal AtomicRewardPoolLastBuildStore(
        string path,
        RewardPoolStoreFaultPoint? injectedFaultPoint = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A reward-pool store path is required.", nameof(path));

        _path = Path.GetFullPath(path);
        _backupPath = _path + ".bak";
        _injectedFaultPoint = injectedFaultPoint;
    }

    internal static AtomicRewardPoolLastBuildStore CreateDefault()
    {
        var directory = Path.Combine(OS.GetUserDataDir(), "mod_configs");
        return new AtomicRewardPoolLastBuildStore(Path.Combine(directory, DefaultFileName));
    }

    internal string PrimaryPath => _path;
    internal string BackupPath => _backupPath;

    public bool HasStoredDefinition => File.Exists(_path) || File.Exists(_backupPath);

    public bool TryLoad(
        out RewardPoolDefinition definition,
        out RewardPoolValidationResult validation)
    {
        if (TryRejectNewerSchema(out validation))
        {
            definition = RewardPoolDefinition.Standard;
            return false;
        }

        if (TryReadValidManual(_path, out definition, out validation))
            return true;

        var primaryValidation = validation;
        if (TryReadValidManual(_backupPath, out definition, out validation))
        {
            var backupDefinition = definition;
            if (TryRestorePrimaryFromBackup(backupDefinition, out var restoreValidation))
            {
                MainFile.Logger.Info(
                    $"Reward-pool last-build primary unavailable validation={primaryValidation}; restored verified backup");
            }
            else
            {
                MainFile.Logger.Info(
                    $"Reward-pool last-build primary unavailable validation={primaryValidation}; "
                    + $"using backup without restoring primary validation={restoreValidation}");
            }

            definition = backupDefinition;
            return true;
        }

        definition = RewardPoolDefinition.Standard;
        if (!HasStoredDefinition)
        {
            validation = RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.NoSavedManualDefinition,
                "No atomically saved manual reward-pool definition exists.");
        }
        else if (validation.Code == RewardPoolValidationCode.NoSavedManualDefinition)
        {
            validation = primaryValidation;
        }

        return false;
    }

    public bool TryCommit(
        RewardPoolDefinition definition,
        out RewardPoolValidationResult validation)
    {
        if (TryRejectNewerSchema(out validation))
            return false;

        validation = definition?.Validate()
            ?? RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.MissingDefinition,
                "The reward-pool definition is missing.");
        if (!validation.IsValid)
            return false;
        if (!definition.IsManual)
        {
            validation = RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.UnsupportedBuildMode,
                "Only a manual reward-pool definition can be stored as the last construction.");
            return false;
        }

        var serialized = definition.Serialize();
        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            validation = PersistenceError("The reward-pool store has no parent directory.");
            return false;
        }

        var transactionId = Guid.NewGuid().ToString("N");
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{transactionId}.tmp");
        var transactionBackupPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_path)}.{transactionId}.bak");
        var replacedExisting = false;
        var installedNewPrimary = false;
        var canDeleteTransactionBackup = true;

        try
        {
            Directory.CreateDirectory(directory);
            ThrowIfFaultInjected(RewardPoolStoreFaultPoint.Write);
            WriteDurably(tempPath, serialized);
            if (!TryReadExact(tempPath, serialized, out validation))
                return false;

            ThrowIfFaultInjected(RewardPoolStoreFaultPoint.Replace);
            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, transactionBackupPath, true);
                replacedExisting = true;
            }
            else
            {
                File.Move(tempPath, _path);
            }
            installedNewPrimary = true;

            ThrowIfFaultInjected(RewardPoolStoreFaultPoint.ReadBack);
            if (!TryReadExact(_path, serialized, out validation))
            {
                canDeleteTransactionBackup = RestorePreviousAfterVerificationFailure(replacedExisting, transactionBackupPath);
                return false;
            }

            RotateVerifiedBackup(transactionBackupPath);
            validation = RewardPoolValidationResult.Valid;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (installedNewPrimary)
                canDeleteTransactionBackup = RestorePreviousAfterVerificationFailure(replacedExisting, transactionBackupPath);
            validation = PersistenceError(
                $"The manual reward-pool definition could not be committed atomically: {ex.Message}");
            return false;
        }
        finally
        {
            DeleteBestEffort(tempPath);
            if (canDeleteTransactionBackup)
                DeleteBestEffort(transactionBackupPath);
        }
    }

    private void WriteDurably(string path, string contents)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            System.IO.FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough);
        var bytes = new UTF8Encoding(false).GetBytes(contents);
        stream.Write(bytes, 0, bytes.Length);
        ThrowIfFaultInjected(RewardPoolStoreFaultPoint.Flush);
        stream.Flush(flushToDisk: true);
    }

    private bool TryReadExact(
        string path,
        string expectedSerialized,
        out RewardPoolValidationResult validation)
    {
        if (!TryReadValidManual(path, out var readBack, out validation))
            return false;

        if (string.Equals(readBack.Serialize(), expectedSerialized, StringComparison.Ordinal))
            return true;

        validation = PersistenceError(
            "The atomically written reward-pool definition did not match the committed snapshot.");
        return false;
    }

    private static bool TryReadValidManual(
        string path,
        out RewardPoolDefinition definition,
        out RewardPoolValidationResult validation)
    {
        definition = RewardPoolDefinition.Standard;
        if (!File.Exists(path))
        {
            validation = RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.NoSavedManualDefinition,
                $"Reward-pool store file '{path}' does not exist.");
            return false;
        }

        try
        {
            var serialized = File.ReadAllText(path, Encoding.UTF8);
            if (RewardPoolDefinition.TryDeserialize(serialized, out definition, out validation)
                && definition.IsManual)
            {
                return true;
            }

            if (validation.IsValid)
            {
                validation = RewardPoolValidationResult.Invalid(
                    RewardPoolValidationCode.NoSavedManualDefinition,
                    "The reward-pool store contains a non-manual definition.");
            }

            definition = RewardPoolDefinition.Standard;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            validation = PersistenceError(
                $"The reward-pool store could not be read: {ex.Message}");
            definition = RewardPoolDefinition.Standard;
            return false;
        }
    }

    private bool TryRestorePrimaryFromBackup(
        RewardPoolDefinition backupDefinition,
        out RewardPoolValidationResult validation)
    {
        var serialized = backupDefinition.Serialize();
        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            validation = PersistenceError("The reward-pool store has no parent directory.");
            return false;
        }

        var transactionId = Guid.NewGuid().ToString("N");
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{transactionId}.restore.tmp");
        var displacedPrimaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_path)}.{transactionId}.invalid");
        var replacedExisting = false;
        var canDeleteDisplacedPrimary = true;

        try
        {
            Directory.CreateDirectory(directory);
            WriteDurably(tempPath, serialized);
            if (!TryReadExact(tempPath, serialized, out validation))
                return false;

            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, displacedPrimaryPath, true);
                replacedExisting = true;
            }
            else
            {
                File.Move(tempPath, _path);
            }

            if (!TryReadExact(_path, serialized, out validation))
            {
                canDeleteDisplacedPrimary = RestorePreviousAfterVerificationFailure(replacedExisting, displacedPrimaryPath);
                return false;
            }

            validation = RewardPoolValidationResult.Valid;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            validation = PersistenceError(
                $"The verified reward-pool backup could not restore the primary: {ex.Message}");
            return false;
        }
        finally
        {
            DeleteBestEffort(tempPath);
            if (canDeleteDisplacedPrimary)
                DeleteBestEffort(displacedPrimaryPath);
        }
    }

    private bool RestorePreviousAfterVerificationFailure(
        bool replacedExisting,
        string transactionBackupPath)
    {
        try
        {
            if (replacedExisting)
            {
                if (File.Exists(_path))
                    File.Replace(transactionBackupPath, _path, null, true);
                else
                    File.Move(transactionBackupPath, _path);
            }
            else
            {
                File.Delete(_path);
            }
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info(
                $"Reward-pool last-build rollback failed primary={_path} backup={transactionBackupPath} error={ex}");
            return false;
        }
    }

    private void RotateVerifiedBackup(string transactionBackupPath)
    {
        if (!File.Exists(transactionBackupPath))
            return;

        // File.Replace faithfully backs up the previous primary even when
        // that file was externally corrupted after this service initialized.
        // Never let such bytes overwrite the last known-good .bak.
        if (!TryReadValidManual(
                transactionBackupPath,
                out _,
                out var backupValidation))
        {
            MainFile.Logger.Info(
                $"Reward-pool last-build rejected invalid transaction backup validation={backupValidation}; preserving existing backup={_backupPath}");
            return;
        }

        try
        {
            File.Move(transactionBackupPath, _backupPath, true);
        }
        catch (Exception ex)
        {
            // The new primary has already been read back and validated. Failure
            // to rotate a convenience backup must not turn a successful commit
            // into a false negative whose disk and memory outcomes disagree.
            MainFile.Logger.Info(
                $"Reward-pool last-build backup rotation failed backup={_backupPath} error={ex}");
        }
    }

    private bool TryRejectNewerSchema(out RewardPoolValidationResult validation)
    {
        foreach (var path in new[] { _path, _backupPath })
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    continue;

                var hasVersion = false;
                long version = 0;
                // Match the serializer's case-insensitive, last-property-wins behavior.
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase))
                        hasVersion = property.Value.ValueKind == JsonValueKind.Number
                            && property.Value.TryGetInt64(out version);
                }
                if (!hasVersion || version <= RewardPoolDefinition.CurrentSchemaVersion)
                    continue;

                validation = RewardPoolValidationResult.Invalid(
                    RewardPoolValidationCode.UnsupportedSchemaVersion,
                    $"The reward-pool last-build file '{path}' uses schema version {version}, newer than this mod supports ({RewardPoolDefinition.CurrentSchemaVersion}). Both primary and backup files were preserved.");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
            {
                // Missing, corrupt, or unreadable files retain the existing recovery behavior.
            }
        }

        validation = RewardPoolValidationResult.Valid;
        return false;
    }

    private static RewardPoolValidationResult PersistenceError(string message) =>
        RewardPoolValidationResult.Invalid(
            RewardPoolValidationCode.ConfigPersistenceError,
            message);

    private void ThrowIfFaultInjected(RewardPoolStoreFaultPoint point)
    {
        if (_injectedFaultPoint == point)
            throw new IOException($"Injected reward-pool store failure at stage '{point}'.");
    }

    private static void DeleteBestEffort(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A stale transaction file is harmless and has a unique name.
        }
    }
}
