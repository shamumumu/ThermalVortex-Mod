using System.Text.Json;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

public sealed record RewardPoolDraftSessionEntry(RewardPoolDraftState State, bool IsSelected, int Number);

public static partial class RewardPoolDraftService
{
    private static DraftLibrary _library = new();

    private static DraftPayload SelectedSession => _library.Sessions.FirstOrDefault(session => session.Selected);

    public static IReadOnlyList<RewardPoolDraftSessionEntry> GetSessions()
    {
        lock (StateLock)
        {
            InitializeLocked();
            return Array.AsReadOnly(_library.Sessions.Select(session =>
                new RewardPoolDraftSessionEntry(session.State.Snapshot(), session.Selected, session.Number)).ToArray());
        }
    }

    public static bool TryOpenSession(string sessionId, out string error)
    {
        lock (StateLock)
        {
            InitializeLocked();
            var session = FindSession(sessionId);
            if (session is null)
            {
                error = "This draft session no longer exists.";
                return Fail(error);
            }

            // Browsing a draft has no effect on the selection used at embark.
            _payload = session;
            error = _lastError = string.Empty;
            return true;
        }
    }

    public static bool TryDeleteSession(string sessionId, out string error)
    {
        lock (StateLock)
        {
            if (!CanWriteLocked(out error))
                return false;
            var session = FindSession(sessionId);
            if (session is null)
            {
                error = "This draft session no longer exists.";
                return Fail(error);
            }

            var remaining = _library.Sessions.Where(item => item != session).ToArray();
            return CommitLibraryLocked(_library with { Sessions = remaining }, _payload?.State.SessionId, out error);
        }
    }

    private static DraftPayload FindSession(string sessionId) =>
        _library.Sessions.FirstOrDefault(session => session.State.SessionId == sessionId);

    private static bool CommitLocked(DraftPayload candidate, out string error,
        Func<bool> commitSelection = null, bool openAfterCommit = false)
    {
        if (!ValidateStructure(candidate, out error))
            return Fail(error);

        var existing = FindSession(candidate.State.SessionId);
        var snapshot = candidate.Snapshot();
        snapshot.Number = existing?.Number ?? checked(_library.LastAssignedNumber + 1);
        var sessions = _library.Sessions.Select(session =>
        {
            if (ReferenceEquals(session, existing))
                return snapshot;
            if (!snapshot.Selected || !session.Selected)
                return session;
            var deselected = session.Snapshot();
            deselected.Selected = false;
            return deselected;
        });
        if (existing is null)
            sessions = sessions.Prepend(snapshot);
        var library = _library with
        {
            Sessions = sessions.ToArray(),
            LastAssignedNumber = Math.Max(_library.LastAssignedNumber, snapshot.Number)
        };
        var editingSessionId = openAfterCommit ? snapshot.State.SessionId : _payload?.State.SessionId;
        return CommitLibraryLocked(library, editingSessionId, out error, commitSelection);
    }

    private static bool TryDeserializeLibrary(string serialized, out DraftLibrary library, out string error)
    {
        library = null;
        using var document = JsonDocument.Parse(serialized);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            error = "The saved draft library is not an object.";
            return false;
        }

        if (document.RootElement.TryGetProperty("libraryVersion", out _))
        {
            library = document.RootElement.Deserialize<DraftLibrary>(JsonOptions);
            if (!ValidateLibrary(library, out error))
                return false;
        }
        else
        {
            var oldSession = document.RootElement.Deserialize<DraftPayload>(JsonOptions);
            if (!ValidateStructure(oldSession, out error))
                return false;
            oldSession.Number = 1;
            library = new DraftLibrary { LastAssignedNumber = 1, Sessions = [oldSession] };
        }

        foreach (var session in library.Sessions)
        {
            session.State = session.State with
            {
                IsLegacy = session.State.IsLegacy || session.SchemaVersion < 2
                    || session.State.RulesVersion != RewardPoolSizePolicy.CurrentRulesVersion
                    || session.State.PackageVersion != RewardPoolConstructionCatalog.PackageVersion
                    || (session.State.PackageVariantsEnabled && session.State.PackageVariantVersion != RewardPoolConstructionCatalog.PackageVariantVersion)
            };
            // Older saves kept incomplete or already consumed drafts selected.
            if (session.State is not { IsLegacy: false, Stage: RewardPoolDraftStage.Complete })
                session.Selected = false;
        }
        return true;
    }

    private static bool ValidateLibrary(DraftLibrary library, out string error)
    {
        error = "The saved draft library structure is invalid.";
        if (library is not { LibraryVersion: 1, Sessions: not null } || library.LastAssignedNumber < 0)
            return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var numbers = new HashSet<int>();
        var foundSelected = false;
        foreach (var session in library.Sessions)
        {
            if (!ValidateStructure(session, out var sessionError))
            {
                error = sessionError;
                return false;
            }
            if (session.Number < 1 || session.Number > library.LastAssignedNumber
                || !ids.Add(session.State.SessionId) || !numbers.Add(session.Number)
                || (session.Selected && foundSelected))
                return false;
            foundSelected |= session.Selected;
        }
        error = string.Empty;
        return true;
    }

    private sealed record DraftLibrary
    {
        public int LibraryVersion { get; init; } = 1;
        public int LastAssignedNumber { get; init; }
        public DraftPayload[] Sessions { get; init; } = [];
    }
}
