using System.Text;
using System.Text.Json;
using Godot;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

internal interface IRewardPoolPresetStore
{
    bool HasStoredLibrary { get; }

    bool TryLoad(
        out RewardPoolPresetLibrary library,
        out RewardPoolPresetOperationResult validation);

    bool TryCommit(
        RewardPoolPresetLibrary library,
        out RewardPoolPresetOperationResult validation);
}

/// <summary>
/// Atomically stores preset drafts. Only the library envelope is validated at
/// this boundary: an incomplete or otherwise unusable draft is still valid
/// user data and must round-trip without being mistaken for corruption.
/// </summary>
internal sealed class AtomicRewardPoolPresetStore : IRewardPoolPresetStore
{
    internal const string DefaultFileName = "ThermalVortex.reward_pool_presets.json";

    private readonly string _path;
    private readonly string _backupPath;
    private readonly RewardPoolStoreFaultPoint? _injectedFaultPoint;

    internal AtomicRewardPoolPresetStore(
        string path,
        RewardPoolStoreFaultPoint? injectedFaultPoint = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A reward-pool preset store path is required.", nameof(path));

        _path = Path.GetFullPath(path);
        _backupPath = _path + ".bak";
        _injectedFaultPoint = injectedFaultPoint;
    }

    internal static AtomicRewardPoolPresetStore CreateDefault()
    {
        var directory = Path.Combine(OS.GetUserDataDir(), "mod_configs");
        return new AtomicRewardPoolPresetStore(Path.Combine(directory, DefaultFileName));
    }

    internal string PrimaryPath => _path;
    internal string BackupPath => _backupPath;

    public bool HasStoredLibrary => File.Exists(_path) || File.Exists(_backupPath);

    public bool TryLoad(
        out RewardPoolPresetLibrary library,
        out RewardPoolPresetOperationResult validation)
    {
        if (TryRejectNewerSchema(out validation))
        {
            library = RewardPoolPresetLibrary.Empty;
            return false;
        }

        if (TryReadValidLibrary(_path, out library, out validation))
            return true;

        var primaryValidation = validation;
        if (TryReadValidLibrary(_backupPath, out library, out validation))
        {
            var backupLibrary = library.Snapshot();
            if (TryRestorePrimaryFromBackup(backupLibrary, out var restoreValidation))
            {
                MainFile.Logger.Info(
                    $"Reward-pool preset primary unavailable validation={primaryValidation}; restored verified backup");
            }
            else
            {
                MainFile.Logger.Info(
                    $"Reward-pool preset primary unavailable validation={primaryValidation}; "
                    + $"using backup without restoring primary validation={restoreValidation}");
            }

            library = backupLibrary;
            validation = RewardPoolPresetOperationResult.Valid;
            return true;
        }

        var backupValidation = validation;
        library = RewardPoolPresetLibrary.Empty;
        if (!HasStoredLibrary)
        {
            validation = RewardPoolPresetOperationResult.Invalid(
                RewardPoolPresetOperationCode.NoSavedLibrary,
                "No reward-pool preset library exists.");
        }
        else if (File.Exists(_path))
        {
            validation = primaryValidation;
        }
        else
        {
            validation = backupValidation;
        }

        return false;
    }

    public bool TryCommit(
        RewardPoolPresetLibrary library,
        out RewardPoolPresetOperationResult validation)
    {
        if (TryRejectNewerSchema(out validation))
            return false;

        validation = library?.ValidateStructure()
            ?? RewardPoolPresetOperationResult.Invalid(
                RewardPoolPresetOperationCode.InvalidLibrary,
                "The reward-pool preset library is missing.");
        if (!validation.IsValid)
            return false;

        var snapshot = library.Snapshot();
        var serialized = snapshot.Serialize();
        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            validation = PersistenceError("The reward-pool preset store has no parent directory.");
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
                canDeleteTransactionBackup = RestorePreviousAfterVerificationFailure(
                    _path,
                    replacedExisting,
                    transactionBackupPath);
                return false;
            }

            if (replacedExisting)
            {
                RotateVerifiedBackup(transactionBackupPath);
            }
            else if (!TryInstallInitialBackup(serialized, out validation))
            {
                canDeleteTransactionBackup = RestorePreviousAfterVerificationFailure(
                    _path,
                    replacedExisting: false,
                    transactionBackupPath);
                return false;
            }
            validation = RewardPoolPresetOperationResult.Valid;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (installedNewPrimary)
            {
                canDeleteTransactionBackup = RestorePreviousAfterVerificationFailure(
                    _path,
                    replacedExisting,
                    transactionBackupPath);
            }
            validation = PersistenceError(
                $"The reward-pool preset library could not be committed atomically: {ex.Message}");
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
        out RewardPoolPresetOperationResult validation)
    {
        if (!TryReadValidLibrary(path, out var readBack, out validation))
            return false;

        if (string.Equals(readBack.Serialize(), expectedSerialized, StringComparison.Ordinal))
            return true;

        validation = PersistenceError(
            "The atomically written reward-pool preset library did not match the committed snapshot.");
        return false;
    }

    private static bool TryReadValidLibrary(
        string path,
        out RewardPoolPresetLibrary library,
        out RewardPoolPresetOperationResult validation)
    {
        library = RewardPoolPresetLibrary.Empty;
        if (!File.Exists(path))
        {
            validation = RewardPoolPresetOperationResult.Invalid(
                RewardPoolPresetOperationCode.NoSavedLibrary,
                $"Reward-pool preset store file '{path}' does not exist.");
            return false;
        }

        try
        {
            var serialized = File.ReadAllText(path, Encoding.UTF8);
            return RewardPoolPresetLibrary.TryDeserialize(serialized, out library, out validation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            validation = PersistenceError(
                $"The reward-pool preset store could not be read: {ex.Message}");
            library = RewardPoolPresetLibrary.Empty;
            return false;
        }
    }

    private bool TryRestorePrimaryFromBackup(
        RewardPoolPresetLibrary backupLibrary,
        out RewardPoolPresetOperationResult validation)
    {
        var serialized = backupLibrary.Serialize();
        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            validation = PersistenceError("The reward-pool preset store has no parent directory.");
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
                canDeleteDisplacedPrimary = RestorePreviousAfterVerificationFailure(
                    _path,
                    replacedExisting,
                    displacedPrimaryPath);
                return false;
            }

            validation = RewardPoolPresetOperationResult.Valid;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            validation = PersistenceError(
                $"The verified reward-pool preset backup could not restore the primary: {ex.Message}");
            return false;
        }
        finally
        {
            DeleteBestEffort(tempPath);
            if (canDeleteDisplacedPrimary)
                DeleteBestEffort(displacedPrimaryPath);
        }
    }

    private bool TryInstallInitialBackup(
        string serialized,
        out RewardPoolPresetOperationResult validation)
    {
        var directory = Path.GetDirectoryName(_backupPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            validation = PersistenceError("The reward-pool preset backup has no parent directory.");
            return false;
        }

        var transactionId = Guid.NewGuid().ToString("N");
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_backupPath)}.{transactionId}.tmp");
        var displacedBackupPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_backupPath)}.{transactionId}.previous");
        var replacedExisting = false;
        var installedBackup = false;
        var canDeleteDisplacedBackup = true;

        try
        {
            WriteDurably(tempPath, serialized);
            if (!TryReadExact(tempPath, serialized, out validation))
                return false;

            if (File.Exists(_backupPath))
            {
                File.Replace(tempPath, _backupPath, displacedBackupPath, true);
                replacedExisting = true;
            }
            else
            {
                File.Move(tempPath, _backupPath);
            }
            installedBackup = true;

            if (!TryReadExact(_backupPath, serialized, out validation))
            {
                canDeleteDisplacedBackup = RestorePreviousAfterVerificationFailure(
                    _backupPath,
                    replacedExisting,
                    displacedBackupPath);
                return false;
            }

            validation = RewardPoolPresetOperationResult.Valid;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (installedBackup)
            {
                canDeleteDisplacedBackup = RestorePreviousAfterVerificationFailure(
                    _backupPath,
                    replacedExisting,
                    displacedBackupPath);
            }
            validation = PersistenceError(
                $"The initial reward-pool preset backup could not be installed: {ex.Message}");
            return false;
        }
        finally
        {
            DeleteBestEffort(tempPath);
            if (canDeleteDisplacedBackup)
                DeleteBestEffort(displacedBackupPath);
        }
    }

    private bool RestorePreviousAfterVerificationFailure(
        string targetPath,
        bool replacedExisting,
        string transactionBackupPath)
    {
        try
        {
            if (replacedExisting)
            {
                if (File.Exists(targetPath))
                    File.Replace(transactionBackupPath, targetPath, null, true);
                else
                    File.Move(transactionBackupPath, targetPath);
            }
            else
            {
                File.Delete(targetPath);
            }
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info(
                $"Reward-pool preset rollback failed target={targetPath} backup={transactionBackupPath} error={ex}");
            return false;
        }
    }

    private void RotateVerifiedBackup(string transactionBackupPath)
    {
        if (!File.Exists(transactionBackupPath))
            return;

        if (!TryReadValidLibrary(transactionBackupPath, out _, out var backupValidation))
        {
            MainFile.Logger.Info(
                $"Reward-pool preset rejected invalid transaction backup validation={backupValidation}; preserving existing backup={_backupPath}");
            return;
        }

        try
        {
            File.Move(transactionBackupPath, _backupPath, true);
        }
        catch (Exception ex)
        {
            // Primary has already passed a full structural read-back. A backup
            // rotation failure therefore remains advisory rather than turning
            // a committed library into an in-memory/disk split-brain result.
            MainFile.Logger.Info(
                $"Reward-pool preset backup rotation failed backup={_backupPath} error={ex}");
        }
    }

    private bool TryRejectNewerSchema(out RewardPoolPresetOperationResult validation)
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
                if (!hasVersion || version <= RewardPoolPresetLibrary.CurrentSchemaVersion)
                    continue;

                validation = RewardPoolPresetOperationResult.Invalid(
                    RewardPoolPresetOperationCode.UnsupportedLibraryVersion,
                    $"The reward-pool preset file '{path}' uses schema version {version}, newer than this mod supports ({RewardPoolPresetLibrary.CurrentSchemaVersion}). Both primary and backup files were preserved.");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
            {
                // Missing, corrupt, or unreadable files retain the existing recovery behavior.
            }
        }

        validation = RewardPoolPresetOperationResult.Valid;
        return false;
    }

    private static RewardPoolPresetOperationResult PersistenceError(string message) =>
        RewardPoolPresetOperationResult.Invalid(
            RewardPoolPresetOperationCode.PersistenceError,
            message);

    private void ThrowIfFaultInjected(RewardPoolStoreFaultPoint point)
    {
        if (_injectedFaultPoint == point)
            throw new IOException($"Injected reward-pool preset store failure at stage '{point}'.");
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
