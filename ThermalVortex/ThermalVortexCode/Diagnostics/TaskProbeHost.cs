using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace ThermalVortex.ThermalVortexCode.Diagnostics;

/// <summary>
/// The only contract implemented by a task-specific probe. Probe sources are
/// compiled into a test build and must not be present in a normal build.
/// </summary>
internal interface ITaskProbe
{
    string Id { get; }

    Task RunAsync(TaskProbeContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Minimal runtime surface exposed to a task-specific probe.
/// </summary>
internal sealed class TaskProbeContext
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);
    private readonly CancellationToken _cancellationToken;
    private readonly List<TaskProbeCheckResult> _checks = [];
    private readonly object _checksLock = new();

    internal TaskProbeContext(
        NGame game,
        string sessionId,
        string probeId,
        CancellationToken cancellationToken)
    {
        NGame = game ?? throw new ArgumentNullException(nameof(game));
        SessionId = sessionId;
        ProbeId = probeId;
        _cancellationToken = cancellationToken;
    }

    public NGame NGame { get; }

    public NGame Game => NGame;

    public string SessionId { get; }

    public string ProbeId { get; }

    /// <summary>
    /// Returns the active combat when one exists. Probes that need combat are
    /// responsible for entering it explicitly and waiting for it to become ready.
    /// </summary>
    public ICombatState CombatState =>
        RunManager.Instance?.DebugOnlyGetState()?.Players
            .Select(player => player?.Creature?.CombatState)
            .FirstOrDefault(state => state is not null);

    /// <summary>
    /// Records a non-throwing assertion. A false check makes the final probe
    /// result fail even if the probe continues running.
    /// </summary>
    public bool Check(bool condition, string message = null) =>
        RecordCheck(null, condition, message);

    public bool Check(string name, bool condition, string message = null) =>
        RecordCheck(name, condition, message);

    /// <summary>
    /// Records an assertion and immediately stops the probe when it fails.
    /// </summary>
    public void Assert(bool condition, string message = null) =>
        Assert(null, condition, message);

    public void Assert(string name, bool condition, string message = null)
    {
        if (RecordCheck(name, condition, message))
            return;

        throw new TaskProbeAssertionException(
            string.IsNullOrWhiteSpace(message)
                ? $"Probe assertion failed: {NormalizeCheckName(name)}"
                : message.Trim());
    }

    /// <summary>
    /// Polls on the Godot scene loop until the predicate succeeds or the
    /// timeout expires. Timeout is returned as false so the probe can choose
    /// the assertion message.
    /// </summary>
    public async Task<bool> WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout cannot be negative.");

        var interval = pollInterval ?? DefaultPollInterval;
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "Poll interval must be positive.");

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _cancellationToken,
            cancellationToken);
        var token = linkedCancellation.Token;
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (predicate())
                return true;

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
                return false;

            await WaitForNextPollAsync(
                remaining < interval ? remaining : interval,
                token);
        }
    }

    public Task<bool> WaitUntilAsync(
        Func<bool> predicate,
        double timeoutSeconds,
        double pollSeconds = 0.05d,
        CancellationToken cancellationToken = default) =>
        WaitUntilAsync(
            predicate,
            TimeSpan.FromSeconds(timeoutSeconds),
            TimeSpan.FromSeconds(pollSeconds),
            cancellationToken);

    internal bool HasFailedChecks
    {
        get
        {
            lock (_checksLock)
                return _checks.Any(check => check.Status == TaskProbeResultStatus.Fail);
        }
    }

    internal IReadOnlyList<TaskProbeCheckResult> SnapshotChecks()
    {
        lock (_checksLock)
            return _checks.ToArray();
    }

    internal string DescribeFailedChecks()
    {
        lock (_checksLock)
        {
            return string.Join(
                "; ",
                _checks
                    .Where(check => check.Status == TaskProbeResultStatus.Fail)
                    .Select(check => string.IsNullOrWhiteSpace(check.Message)
                        ? check.Name
                        : $"{check.Name}: {check.Message}"));
        }
    }

    private bool RecordCheck(string name, bool condition, string message)
    {
        lock (_checksLock)
        {
            _checks.Add(new TaskProbeCheckResult
            {
                Name = NormalizeCheckName(name, _checks.Count + 1),
                Status = condition ? TaskProbeResultStatus.Pass : TaskProbeResultStatus.Fail,
                Message = NormalizeOptional(message),
                RecordedUtc = DateTimeOffset.UtcNow
            });
        }

        return condition;
    }

    private async Task WaitForNextPollAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (GodotObject.IsInstanceValid(NGame)
            && NGame.IsInsideTree()
            && NGame.GetTree() is { } tree)
        {
            var seconds = Math.Max(interval.TotalSeconds, 0.001d);
            await NGame.ToSignal(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        await Task.Delay(interval, cancellationToken);
    }

    private static string NormalizeCheckName(string name, int fallbackIndex = 1) =>
        string.IsNullOrWhiteSpace(name) ? $"check-{fallbackIndex}" : name.Trim();

    private static string NormalizeOptional(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Loads at most one explicitly requested, task-specific probe. With no marker
/// beside the loaded mod assembly the host performs no I/O, discovery, or work.
/// </summary>
internal static class TaskProbeHost
{
    internal const string MarkerFileName = ".task-probe-session.json";

    private const string LogPrefix = "THERMALVORTEX_TASK_PROBE";
    private const int ResultSchemaVersion = 1;
    private static readonly TimeSpan MarkerMaxAge = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MarkerFutureTolerance = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions MarkerJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static TaskProbeMarker _marker;
    private static string _markerDirectory;
    private static int _initialized;
    private static int _claimed;

    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        string markerPath;
        try
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            _markerDirectory = string.IsNullOrWhiteSpace(assemblyPath)
                ? null
                : Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
            markerPath = string.IsNullOrWhiteSpace(_markerDirectory)
                ? null
                : Path.Combine(_markerDirectory, MarkerFileName);
        }
        catch (Exception ex)
        {
            Log($"marker_path_error error={ex.GetType().Name} message={Sanitize(ex.Message)}");
            return;
        }

        if (string.IsNullOrWhiteSpace(markerPath) || !File.Exists(markerPath))
            return;

        try
        {
            var json = File.ReadAllText(markerPath, Encoding.UTF8);
            var marker = JsonSerializer.Deserialize<TaskProbeMarker>(json, MarkerJsonOptions);
            if (marker?.Enabled == true)
                _marker = marker;
        }
        catch (Exception ex)
        {
            Log($"marker_read_error error={ex.GetType().Name} message={Sanitize(ex.Message)}");
        }
    }

    private static void OnGameReady(NGame game)
    {
        var marker = _marker;
        if (marker is null || Interlocked.Exchange(ref _claimed, 1) != 0)
            return;

        _ = ExecuteOnceAsync(game, marker);
    }

    private static async Task ExecuteOnceAsync(NGame game, TaskProbeMarker marker)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        TaskProbeContext context = null;
        TaskProbeResult result;

        try
        {
            ValidateMarker(marker, startedUtc);
            var probe = ResolveProbe(marker.ProbeId);
            context = new TaskProbeContext(
                game,
                marker.SessionId.Trim(),
                marker.ProbeId.Trim(),
                CancellationToken.None);

            Log($"start session={Sanitize(marker.SessionId)} probe={Sanitize(marker.ProbeId)}");

            try
            {
                await probe.RunAsync(context, CancellationToken.None);

                if (context.HasFailedChecks)
                {
                    throw new TaskProbeAssertionException(
                        $"One or more checks failed: {context.DescribeFailedChecks()}");
                }

                result = CreateResult(
                    marker,
                    TaskProbeResultStatus.Pass,
                    null,
                    null,
                    null,
                    null,
                    startedUtc,
                    context.SnapshotChecks());
            }
            catch (Exception ex)
            {
                var actualException = Unwrap(ex);
                result = CreateResult(
                    marker,
                    TaskProbeResultStatus.Fail,
                    TaskProbeFailureKind.Assertion,
                    NormalizeOptional(actualException.Message) ?? actualException.GetType().Name,
                    actualException.GetType().FullName,
                    actualException.StackTrace,
                    startedUtc,
                    context.SnapshotChecks());
            }
        }
        catch (Exception ex)
        {
            var actualException = Unwrap(ex);
            result = CreateResult(
                marker,
                TaskProbeResultStatus.Fail,
                TaskProbeFailureKind.Infrastructure,
                NormalizeOptional(actualException.Message) ?? actualException.GetType().Name,
                actualException.GetType().FullName,
                actualException.StackTrace,
                startedUtc,
                context?.SnapshotChecks() ?? []);
        }

        try
        {
            WriteResultAtomically(marker, result);
            Log(
                $"end session={Sanitize(marker.SessionId)} probe={Sanitize(marker.ProbeId)} " +
                $"status={result.Status} failureKind={result.FailureKind ?? "-"}");
        }
        catch (Exception ex)
        {
            Log(
                $"result_write_error session={Sanitize(marker.SessionId)} probe={Sanitize(marker.ProbeId)} " +
                $"error={ex.GetType().Name} message={Sanitize(ex.Message)}");
        }
    }

    private static ITaskProbe ResolveProbe(string requestedProbeId)
    {
        Type[] assemblyTypes;
        try
        {
            assemblyTypes = Assembly.GetExecutingAssembly().GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var loaderFailures = string.Join(
                "; ",
                ex.LoaderExceptions
                    .Where(loaderException => loaderException is not null)
                    .Select(loaderException => loaderException.GetType().Name + ": " + loaderException.Message));
            throw new TaskProbeInfrastructureException(
                $"Could not inspect task probe types. {loaderFailures}",
                ex);
        }
        catch (Exception ex)
        {
            throw new TaskProbeInfrastructureException("Could not inspect task probe types.", ex);
        }

        var implementations = assemblyTypes
            .Where(type => type is
            {
                IsAbstract: false,
                IsInterface: false,
                ContainsGenericParameters: false
            })
            .Where(type => typeof(ITaskProbe).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        var matches = new List<ITaskProbe>();
        var discoveredIds = new List<string>();
        foreach (var implementation in implementations)
        {
            ITaskProbe instance;
            try
            {
                instance = Activator.CreateInstance(implementation, nonPublic: true) as ITaskProbe;
            }
            catch (Exception ex)
            {
                throw new TaskProbeInfrastructureException(
                    $"Could not instantiate task probe type '{implementation.FullName}'.",
                    ex);
            }

            if (instance is null)
            {
                throw new TaskProbeInfrastructureException(
                    $"Task probe type '{implementation.FullName}' did not create an ITaskProbe instance.");
            }

            string id;
            try
            {
                id = instance.Id?.Trim();
            }
            catch (Exception ex)
            {
                throw new TaskProbeInfrastructureException(
                    $"Could not read Id from task probe type '{implementation.FullName}'.",
                    ex);
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new TaskProbeInfrastructureException(
                    $"Task probe type '{implementation.FullName}' has an empty Id.");
            }

            discoveredIds.Add(id);
            if (string.Equals(id, requestedProbeId.Trim(), StringComparison.Ordinal))
                matches.Add(instance);
        }

        if (matches.Count == 1)
            return matches[0];

        var available = discoveredIds.Count == 0
            ? "none"
            : string.Join(",", discoveredIds.OrderBy(id => id, StringComparer.Ordinal));
        throw new TaskProbeInfrastructureException(
            matches.Count == 0
                ? $"No task probe matched Id '{requestedProbeId}'. Available: {available}."
                : $"More than one task probe matched Id '{requestedProbeId}'.");
    }

    private static void ValidateMarker(TaskProbeMarker marker, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(marker.SessionId))
            throw new TaskProbeInfrastructureException("Task probe marker is missing sessionId.");
        if (string.IsNullOrWhiteSpace(marker.ProbeId))
            throw new TaskProbeInfrastructureException("Task probe marker is missing probeId.");
        if (string.IsNullOrWhiteSpace(marker.ResultPath))
            throw new TaskProbeInfrastructureException("Task probe marker is missing resultPath.");
        if (marker.CreatedUtc == default)
            throw new TaskProbeInfrastructureException("Task probe marker is missing createdUtc.");

        var age = now - marker.CreatedUtc;
        if (age > MarkerMaxAge)
            throw new TaskProbeInfrastructureException(
                $"Task probe marker is stale (age {age.TotalMinutes:F1} minutes).");
        if (age < -MarkerFutureTolerance)
            throw new TaskProbeInfrastructureException("Task probe marker createdUtc is in the future.");
    }

    private static TaskProbeResult CreateResult(
        TaskProbeMarker marker,
        string status,
        string failureKind,
        string message,
        string exceptionType,
        string stackTrace,
        DateTimeOffset startedUtc,
        IReadOnlyList<TaskProbeCheckResult> checks) =>
        new()
        {
            SchemaVersion = ResultSchemaVersion,
            SessionId = marker.SessionId?.Trim(),
            ProbeId = marker.ProbeId?.Trim(),
            Status = status,
            FailureKind = failureKind,
            Message = NormalizeOptional(message),
            ExceptionType = NormalizeOptional(exceptionType),
            StackTrace = NormalizeOptional(stackTrace),
            StartedUtc = startedUtc,
            CompletedUtc = DateTimeOffset.UtcNow,
            Checks = checks
        };

    private static void WriteResultAtomically(TaskProbeMarker marker, TaskProbeResult result)
    {
        if (string.IsNullOrWhiteSpace(marker.ResultPath))
            throw new TaskProbeInfrastructureException("Cannot write a result without resultPath.");

        var resultPath = Path.IsPathFullyQualified(marker.ResultPath)
            ? Path.GetFullPath(marker.ResultPath)
            : Path.GetFullPath(Path.Combine(_markerDirectory, marker.ResultPath));
        var resultDirectory = Path.GetDirectoryName(resultPath);
        if (string.IsNullOrWhiteSpace(resultDirectory))
            throw new TaskProbeInfrastructureException("Task probe resultPath has no parent directory.");

        Directory.CreateDirectory(resultDirectory);
        var temporaryPath = Path.Combine(
            resultDirectory,
            $".{Path.GetFileName(resultPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var json = JsonSerializer.Serialize(result, ResultJsonOptions);
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, resultPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // The result move already failed or succeeded. Temp cleanup is best-effort.
            }
        }
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException or AggregateException
               && exception.InnerException is not null)
        {
            exception = exception.InnerException;
        }

        return exception;
    }

    private static string NormalizeOptional(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Sanitize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Replace('\r', ' ').Replace('\n', ' ').Replace(' ', '_');

    private static void Log(string message) =>
        MainFile.Logger.Info($"{LogPrefix} {message}");

    private sealed class TaskProbeMarker
    {
        public bool Enabled { get; set; }

        public string SessionId { get; set; }

        public string ProbeId { get; set; }

        public string ResultPath { get; set; }

        public DateTimeOffset CreatedUtc { get; set; }
    }

    [HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
    private static class NGameReadyPatch
    {
        private static void Postfix(NGame __instance) => OnGameReady(__instance);
    }
}

internal static class TaskProbeResultStatus
{
    internal const string Pass = "PASS";
    internal const string Fail = "FAIL";
}

internal static class TaskProbeFailureKind
{
    internal const string Assertion = "ASSERTION";
    internal const string Infrastructure = "INFRASTRUCTURE";
}

internal sealed class TaskProbeAssertionException : Exception
{
    internal TaskProbeAssertionException(string message)
        : base(message)
    {
    }
}

internal sealed class TaskProbeInfrastructureException : Exception
{
    internal TaskProbeInfrastructureException(string message)
        : base(message)
    {
    }

    internal TaskProbeInfrastructureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class TaskProbeResult
{
    public int SchemaVersion { get; set; }

    public string SessionId { get; set; }

    public string ProbeId { get; set; }

    public string Status { get; set; }

    public string FailureKind { get; set; }

    public string Message { get; set; }

    public string ExceptionType { get; set; }

    public string StackTrace { get; set; }

    public DateTimeOffset StartedUtc { get; set; }

    public DateTimeOffset CompletedUtc { get; set; }

    public IReadOnlyList<TaskProbeCheckResult> Checks { get; set; } = [];
}

internal sealed class TaskProbeCheckResult
{
    public string Name { get; set; }

    public string Status { get; set; }

    public string Message { get; set; }

    public DateTimeOffset RecordedUtc { get; set; }
}
