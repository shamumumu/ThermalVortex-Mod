using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BaseLib.Abstracts;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.RewardPools;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

/// <summary>One reliable envelope type, explicitly registered before RunManager exists.</summary>
public sealed class RewardPoolSharedDraftMessage : ICustomMessage
{
    internal const int MaxUtf8Bytes = 131072;
    public string Json { get; set; } = string.Empty;
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public void Serialize(PacketWriter writer)
    {
        var bytes = Encoding.UTF8.GetBytes(Json);
        if (bytes.Length > MaxUtf8Bytes)
            throw new InvalidDataException("Shared draft message is too large.");
        writer.WriteInt(bytes.Length, 32);
        writer.WriteBytes(bytes, bytes.Length);
    }
    public void Deserialize(PacketReader reader)
    {
        var length = reader.ReadInt(32);
        if (length < 0 || length > MaxUtf8Bytes)
        {
            Json = string.Empty;
            return;
        }
        var bytes = new byte[length];
        reader.ReadBytes(bytes, length);
        Json = Encoding.UTF8.GetString(bytes);
    }
    public void HandleMessage(ulong senderId) => RewardPoolMultiplayerLobby.Receive(Json, senderId);
}

internal sealed class RewardPoolSharedDraftEnvelope
{
    public int Protocol { get; set; } = 1;
    public string Catalog { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public long Revision { get; set; }
    public long Sequence { get; set; }
    public List<string> Packages { get; set; } = [];
    public List<string> Main { get; set; } = [];
    public List<string> Extra { get; set; } = [];
    public RewardPoolSharedDraftState State { get; set; }
    public List<ulong> Connected { get; set; } = [];
    public bool CanEmbark { get; set; }
    public string Error { get; set; } = string.Empty;
    public string ClientToken { get; set; } = string.Empty;
    public Dictionary<ulong, long> AcceptedSequences { get; set; } = [];
}

/// <summary>
/// The host accepts provisional intentions, including collisions. Only the domain
/// engine decides whether a common revision can be confirmed. Transport order
/// never awards a package or card to the first arrival.
/// </summary>
internal sealed class RewardPoolMultiplayerLobby : IDisposable
{
    private const int ProtocolVersion = 1;
    private static RewardPoolMultiplayerLobby _current;
    private static Dictionary<ulong, RewardPoolDefinition> _launchDefinitions;
    private static string _launchSessionId;
    private static readonly AsyncLocal<SharedLaunchContext> LaunchContext = new();
    [ThreadStatic] private static ulong? _transportSender;
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 32 };
    private readonly NCharacterSelectScreen _screen;
    private readonly StartRunLobby _lobby;
    private readonly INetGameService _net;
    private readonly MessageHandlerDelegate<CustomMessageWrapper> _handler;
    private readonly HashSet<ulong> _compatible = [];
    private readonly HashSet<ulong> _finalAcks = [];
    private readonly Dictionary<ulong, long> _editSequences = [];
    private readonly Dictionary<ulong, string> _clientTokens = [];
    private readonly string _clientToken = Guid.NewGuid().ToString("N");
    private readonly HashSet<ulong> _connected = [];
    private readonly HashSet<ulong> _reconnecting = [];
    private readonly string _catalogFingerprint;
    private RewardPoolSharedDraftSession _session;
    private RewardPoolMultiplayerScreen _view;
    private long _ownSequence;
    private long _broadcastSequence;
    private long _lastHostSequence;
    private long _lastOwnAcknowledged;
    private double _helloDelay;
    private double _refreshDelay;
    private bool _registered;
    private bool _disposed;
    private bool _remoteCanEmbark;
    private bool _launchApplied;
    private string _error = string.Empty;

    private RewardPoolMultiplayerLobby(NCharacterSelectScreen screen)
    {
        _screen = screen;
        _lobby = screen.Lobby;
        _net = _lobby.NetService;
        _catalogFingerprint = ComputeCatalogFingerprint();
        _handler = OnWrapper;
        try
        {
            _net.RegisterMessageHandler(_handler);
            _registered = true;
            _screen.TreeExiting += OnScreenTreeExiting;
            if (_net is INetHostGameService host)
                host.ClientDisconnected += OnPeerDisconnected;
            _compatible.Add(_net.NetId);
            RefreshPresence();
            _view = new RewardPoolMultiplayerScreen(screen, this);
            if (!IsHost)
                SendHello();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal bool IsHost => _net.Type == NetGameType.Host;
    internal ulong LocalId => _net.NetId;
    internal RewardPoolSharedDraftState State => _session?.State;
    internal string Error => _error;
    internal IReadOnlyList<ulong> Connected => _connected.ToArray();
    internal bool IsCompatible => IsHost
        ? _lobby.Players.All(player => _compatible.Contains(player.id))
        : _compatible.Contains(HostId);
    internal bool CanStart => IsHost && EligibleRoster() && IsCompatible && _session is null;
    internal bool IsAwaitingResponse => !IsHost && _ownSequence > _lastOwnAcknowledged;
    internal bool HasMissingPlayers => _session is not null && !AllPresent();
    internal bool CanConfirm => _session is not null && AllPresent()
        && !IsAwaitingResponse && string.IsNullOrEmpty(_session.GetValidation(LocalId));
    internal string Validation => _session?.GetValidation(LocalId) ?? string.Empty;
    internal bool ReadyToEmbark => _launchApplied || (_session?.State.Phase == RewardPoolSharedDraftPhase.Finalized
        && AllPresent() && (IsHost ? _session.State.ParticipantIds.All(_finalAcks.Contains) : _remoteCanEmbark));
    internal int MainCount(ulong player) => (_session?.GetSelectedMainIds(player).Count ?? 0) + 5;
    internal string PlayerName(ulong player)
    {
        var participants = _session?.State.ParticipantIds ?? _lobby.Players.OrderBy(p => p.slotId).Select(p => p.id).ToList();
        return RewardPoolMultiplayerScreen.Text("sharedDraftPlayer") + " " + (participants.IndexOf(player) + 1)
            + (player == LocalId ? " (" + RewardPoolMultiplayerScreen.Text("sharedDraftYou") + ")" : string.Empty);
    }
    private ulong HostId => IsHost ? LocalId : ((INetClientGameService)_net).NetClient.HostNetId;

    internal static void Open(NCharacterSelectScreen screen)
    {
        var lobby = screen?.Lobby;
        if (lobby?.GameMode != GameMode.Standard
            || lobby.NetService?.Type is not (NetGameType.Host or NetGameType.Client))
            return;
        if (_current is not null && ReferenceEquals(_current._lobby, lobby))
            return;
        _current?.Dispose();
        ClearPendingLaunch();
        try { _current = new RewardPoolMultiplayerLobby(screen); }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Shared reward-pool lobby unavailable: " + exception);
            _current?.Dispose();
            _current = null;
        }
    }

    internal static void Tick(NCharacterSelectScreen screen, double delta)
    {
        if (_current is not { } current || !ReferenceEquals(current._screen, screen) || current._disposed)
            return;
        current._refreshDelay -= delta;
        if (current._refreshDelay > 0)
            return;
        current._refreshDelay = .15;
        current.RefreshPresence();
        current._helloDelay -= .15;
        if (!current.IsHost && !current.IsCompatible && current._helloDelay <= 0)
        {
            current._helloDelay = 2;
            current.SendHello();
        }
        current._view.Refresh();
    }

    internal static void Close(NCharacterSelectScreen screen, bool starting)
    {
        if (_current is not { } current || !ReferenceEquals(current._screen, screen))
            return;
        if (!starting && !current._launchApplied)
        {
            current.Cancel();
            ClearPendingLaunch();
        }
        current.Dispose();
        _current = null;
    }

    internal static bool CanEmbark(NCharacterSelectScreen screen)
    {
        if (_current is not { } current || !ReferenceEquals(current._screen, screen) || current._session is null)
            return true;
        current.RefreshPresence();
        if (current.ReadyToEmbark)
        {
            current.StageLaunch();
            current._view.Hide();
            return true;
        }
        current._error = "sharedDraftWaiting";
        current._view.Show();
        return false;
    }

    internal static bool AllowNativeStart(StartRunLobby lobby)
    {
        if (_current is not { } current || !ReferenceEquals(current._lobby, lobby) || current._session is null)
            return true;
        current.RefreshPresence();
        return current._session is null || current.ReadyToEmbark;
    }

    internal static bool HasOpenScreen(NCharacterSelectScreen screen) =>
        _current is { } current && ReferenceEquals(current._screen, screen) && current._view?.IsOpen == true;

    internal static bool HandleInput(NCharacterSelectScreen screen, InputEvent input)
    {
        if (!HasOpenScreen(screen))
            return false;
        if (input is not InputEventKey { Echo: true } && input.IsActionPressed(MegaInput.cancel, false, false))
        {
            _current._view.Hide();
            screen.GetViewport()?.SetInputAsHandled();
        }
        return true;
    }

    internal static void ClearPendingLaunch()
    {
        _launchDefinitions = null;
        _launchSessionId = null;
    }

    internal sealed class SharedLaunchContext
    {
        internal RewardPoolMultiplayerLobby Owner;
        internal string SessionId;
        internal long Revision;
        internal Dictionary<ulong, RewardPoolDefinition> Definitions;
        internal bool Applied;
    }

    internal static SharedLaunchContext EnterNativeLaunch(StartRunLobby lobby)
    {
        var previous = LaunchContext.Value;
        LaunchContext.Value = null;
        if (_current is not { } current || !ReferenceEquals(current._lobby, lobby) || current._session is null)
        {
            ClearPendingLaunch();
            return previous;
        }
        current.RefreshPresence();
        if (!current.ReadyToEmbark)
            throw new InvalidOperationException("All shared draft participants must confirm and synchronize before starting.");
        current.StageLaunch();
        LaunchContext.Value = new SharedLaunchContext
        {
            Owner = current, SessionId = _launchSessionId, Revision = current._session.State.ContentRevision,
            Definitions = _launchDefinitions
        };
        return previous;
    }

    internal static void ExitNativeLaunch(SharedLaunchContext previous) => LaunchContext.Value = previous;

    internal static void BeforeSavedRun()
    {
        ClearPendingLaunch();
        LaunchContext.Value = null;
        _current?.Dispose();
        _current = null;
    }

    internal static bool TryApplyFreshRun(RunState run)
    {
        var context = LaunchContext.Value;
        if (context is null)
            return false;
        var definitions = context.Definitions;
        var owner = context.Owner;
        if (context.Applied || owner._disposed || owner._session?.State is not { } currentState
            || currentState.SessionId != context.SessionId || currentState.ContentRevision != context.Revision
            || currentState.Phase != RewardPoolSharedDraftPhase.Finalized || !owner.ReadyToEmbark)
            throw new InvalidOperationException("Shared reward-pool launch context is stale or already consumed.");
        if (run?.GameMode != GameMode.Standard || run.Players.Count != definitions.Count
            || run.Players.Any(player => player.Character is not ThermalVortexCharacter
                || !definitions.ContainsKey(player.NetId)))
            throw new InvalidOperationException("Shared reward-pool launch roster changed.");
        foreach (var player in run.Players)
        {
            var core = player.GetRelic<ThermalVortexCore>()
                ?? throw new InvalidOperationException("Shared reward pool requires each player's starting core.");
            var definition = definitions[player.NetId];
            var validation = definition.Validate();
            if (!validation.IsValid)
                throw new InvalidOperationException("Invalid shared reward-pool snapshot: " + validation.Message);
            core.ApplyRewardPoolDefinition(definition);
            RewardPoolForeignMechanics.ApplyPersistentCapabilities(player, RewardPoolCapabilityApplicationPhase.FreshRun);
        }
        if (!owner._session.TryMarkConsumed(context.SessionId, out var error))
            throw new InvalidOperationException("Could not consume shared reward-pool session: " + error);
        context.Applied = true;
        owner._launchApplied = true;
        owner.DetachNetwork();
        ClearPendingLaunch();
        return true;
    }

    internal void Start()
    {
        if (!CanStart)
        {
            _error = EligibleRoster() ? "sharedDraftVersionMismatch" : "sharedDraftRosterRequired";
            return;
        }
        if (!RewardPoolSharedDraftSession.TryCreate(
                _lobby.Players.OrderBy(player => player.slotId).Select(player => player.id).ToArray(),
                out _session, out _error))
            return;
        _finalAcks.Clear();
        _editSequences.Clear();
        _remoteCanEmbark = false;
        ClearPendingLaunch();
        _lobby.SetReady(false);
        Broadcast();
        _view.Show();
    }

    internal void SetPackages(IReadOnlyList<string> ids) => Submit(new RewardPoolSharedDraftEnvelope
    {
        Kind = "packages", Packages = ids.ToList()
    });

    internal void SetCards(IReadOnlyList<string> main, IReadOnlyList<string> extra) => Submit(new RewardPoolSharedDraftEnvelope
    {
        Kind = "cards", Main = main.ToList(), Extra = extra.ToList()
    });

    internal void Confirm() => Submit(new RewardPoolSharedDraftEnvelope { Kind = "confirm" });

    internal void Cancel()
    {
        if (_session is null)
            return;
        if (IsHost)
            CancelLocally("sharedDraftCancelled", true);
        else
            Submit(new RewardPoolSharedDraftEnvelope { Kind = "cancel" });
    }

    private void Submit(RewardPoolSharedDraftEnvelope envelope)
    {
        if (_session is null || !_net.IsConnected || (envelope.Kind != "cancel" && !AllPresent()))
        {
            _error = "sharedDraftDisconnected";
            return;
        }
        envelope.SessionId = _session.State.SessionId;
        envelope.Revision = _session.State.ContentRevision;
        envelope.Sequence = ++_ownSequence;
        if (IsHost)
            HandleIntent(envelope, LocalId);
        else
            Send(envelope);
    }

    private void HandleIntent(RewardPoolSharedDraftEnvelope envelope, ulong sender)
    {
        var state = _session?.State;
        if (state is null || envelope.SessionId != state.SessionId || !state.ParticipantIds.Contains(sender)
            || !_connected.Contains(sender) || !_compatible.Contains(sender))
            return;
        if (_editSequences.TryGetValue(sender, out var previous) && envelope.Sequence <= previous)
            return;
        _editSequences[sender] = envelope.Sequence;
        if (envelope.Kind == "cancel")
        {
            CancelLocally("sharedDraftCancelled", true);
            return;
        }
        if (!AllPresent())
        {
            SendError(sender, "sharedDraftDisconnected");
            return;
        }
        var accepted = envelope.Kind switch
        {
            "packages" => _session.TrySetPackages(sender, envelope.Packages, out _error),
            "cards" => _session.TrySetCards(sender, envelope.Main, envelope.Extra, out _error),
            "confirm" => _session.TryConfirm(sender, envelope.Revision, out _error),
            _ => false
        };
        if (!accepted)
        {
            SendError(sender, _error);
            return;
        }
        _error = string.Empty;
        if (_session.State.ContentRevision != state.ContentRevision)
        {
            _finalAcks.Clear();
            ClearPendingLaunch();
        }
        if (_session.State.Phase == RewardPoolSharedDraftPhase.Finalized)
        {
            StageLaunch();
            _finalAcks.Add(LocalId);
        }
        Broadcast();
    }

    private void RefreshPresence()
    {
        var now = _lobby.Players.Select(player => player.id).ToHashSet();
        var changed = !_connected.SetEquals(now);
        if (IsHost && _session is not null)
        {
            foreach (var id in _session.State.ParticipantIds.Where(id => !now.Contains(id)))
            {
                _compatible.Remove(id);
                _reconnecting.Add(id);
            }
            foreach (var player in _lobby.Players.Where(player => player.character is ThermalVortexCharacter))
                _reconnecting.Remove(player.id);
        }
        _connected.Clear();
        _connected.UnionWith(now);
        if (!IsHost || _session is null)
            return;
        var state = _session.State;
        if (now.Any(id => !state.ParticipantIds.Contains(id))
            || _lobby.Players.Any(player => player.character is not ThermalVortexCharacter && !_reconnecting.Contains(player.id)))
        {
            CancelLocally("sharedDraftRosterChanged", true);
            return;
        }
        if (changed)
        {
            _finalAcks.IntersectWith(now);
            Broadcast();
        }
    }

    private bool EligibleRoster() => _lobby.Players.Count is >= 2 and <= 4
        && _lobby.Players.All(player => player.character is ThermalVortexCharacter);

    private bool AllPresent() => _session is not null
        && _session.State.ParticipantIds.All(_connected.Contains)
        && EligibleRoster() && IsCompatible;

    private void OnPeerDisconnected(ulong peer, NetErrorInfo info)
    {
        if (_session is null || !_session.State.ParticipantIds.Contains(peer) || _launchApplied)
            return;
        if (info.GetReason() is NetError.Quit or NetError.Kicked or NetError.HostAbandoned)
            CancelLocally("sharedDraftRosterChanged", true);
        else
            RefreshPresence();
    }

    private void OnWrapper(CustomMessageWrapper wrapper, ulong sender)
    {
        // Do not steal other mods' pre-run messages or duplicate their dispatch.
        if (wrapper.Message is RewardPoolSharedDraftMessage message)
            Receive(message.Json, sender);
    }

    internal static void Receive(string json, ulong sender)
    {
        var current = _current;
        if (current is null || current._disposed || !current._registered || string.IsNullOrEmpty(json)
            || json.Length > RewardPoolSharedDraftMessage.MaxUtf8Bytes || _transportSender != sender)
            return;
        try
        {
            var envelope = JsonSerializer.Deserialize<RewardPoolSharedDraftEnvelope>(json, JsonOptions);
            if (envelope is not null)
                current.HandleEnvelope(envelope, sender);
        }
        catch (Exception exception)
        {
            current._error = "sharedDraftInvalidMessage";
            MainFile.Logger.Info("Rejected shared reward-pool message: " + exception.Message);
        }
    }

    private void HandleEnvelope(RewardPoolSharedDraftEnvelope envelope, ulong sender)
    {
        if (envelope.Protocol != ProtocolVersion || envelope.Catalog != _catalogFingerprint)
        {
            if (sender == HostId || IsHost && _lobby.Players.Any(player => player.id == sender))
            {
                _compatible.Remove(sender);
                _error = "sharedDraftVersionMismatch";
                if (IsHost && envelope.Kind == "hello")
                    Send(new RewardPoolSharedDraftEnvelope { Kind = "helloAck" }, sender);
            }
            return;
        }
        if (IsHost)
        {
            if (!_lobby.Players.Any(player => player.id == sender))
                return;
            if (envelope.Kind == "hello")
            {
                if (!_clientTokens.TryGetValue(sender, out var token) || token != envelope.ClientToken)
                {
                    _editSequences.Remove(sender);
                    _clientTokens[sender] = envelope.ClientToken;
                }
                _compatible.Add(sender);
                Send(new RewardPoolSharedDraftEnvelope { Kind = "helloAck" }, sender);
                if (_session is not null)
                    Broadcast();
                return;
            }
            if (envelope.Kind == "finalAck" && _session?.State is { } state
                && _compatible.Contains(sender)
                && state.Phase == RewardPoolSharedDraftPhase.Finalized
                && envelope.SessionId == state.SessionId && envelope.Revision == state.ContentRevision)
            {
                if (_finalAcks.Add(sender))
                    Broadcast();
                return;
            }
            if (!_clientTokens.TryGetValue(sender, out var currentToken) || currentToken != envelope.ClientToken)
                return;
            HandleIntent(envelope, sender);
            return;
        }
        // Clients accept authoritative state only from the actual host peer.
        if (sender != HostId)
            return;
        _compatible.Add(sender);
        if (envelope.Kind == "helloAck")
            return;
        if (envelope.Kind == "error")
        {
            if (envelope.SessionId != _session?.State.SessionId)
                return;
            _error = envelope.Error;
            _lastOwnAcknowledged = Math.Max(_lastOwnAcknowledged, envelope.Sequence);
            return;
        }
        if (envelope.Sequence <= _lastHostSequence)
            return;
        _lastHostSequence = envelope.Sequence;
        if (envelope.Kind == "cancel")
        {
            CancelLocally(envelope.Error, false);
            return;
        }
        if (envelope.Kind != "snapshot" || envelope.State is null
            || envelope.SessionId != envelope.State.SessionId
            || _session is not null && _session.State.SessionId != envelope.SessionId
            || !envelope.State.ParticipantIds.Contains(LocalId))
            return;
        if (!RewardPoolSharedDraftSession.TryRestore(envelope.State, out var restored, out var error))
        {
            _error = error;
            return;
        }
        var wasActive = _session is not null;
        _session = restored;
        if (envelope.AcceptedSequences.TryGetValue(LocalId, out var acknowledged))
            _lastOwnAcknowledged = Math.Max(_lastOwnAcknowledged, acknowledged);
        _connected.Clear();
        _connected.UnionWith(envelope.Connected);
        _remoteCanEmbark = envelope.CanEmbark;
        _error = string.Empty;
        if (!wasActive)
        {
            if (_lobby.LocalPlayer.character is not ThermalVortexCharacter)
                _lobby.SetLocalCharacter(ModelDb.Character<ThermalVortexCharacter>());
            _lobby.SetReady(false);
            _view.Show();
        }
        if (_session.State.Phase == RewardPoolSharedDraftPhase.Finalized)
        {
            StageLaunch();
            if (!envelope.CanEmbark)
                Send(new RewardPoolSharedDraftEnvelope
                {
                    Kind = "finalAck", SessionId = _session.State.SessionId,
                    Revision = _session.State.ContentRevision
                });
        }
    }

    private void StageLaunch()
    {
        if (!_session.TryGetDefinitions(out var definitions, out var error))
            throw new InvalidOperationException("Shared reward-pool finalization failed: " + error);
        _launchDefinitions = definitions.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
        _launchSessionId = _session.State.SessionId;
    }

    private void SendHello() => Send(new RewardPoolSharedDraftEnvelope { Kind = "hello" });

    private void Broadcast()
    {
        if (!IsHost || _session is null)
            return;
        Send(new RewardPoolSharedDraftEnvelope
        {
            Kind = "snapshot", Sequence = ++_broadcastSequence, State = _session.State,
            SessionId = _session.State.SessionId,
            Connected = _connected.ToList(), CanEmbark = ReadyToEmbark,
            AcceptedSequences = new Dictionary<ulong, long>(_editSequences)
        });
    }

    private void SendError(ulong target, string error)
    {
        if (target == LocalId)
            _error = error;
        else
            Send(new RewardPoolSharedDraftEnvelope
            {
                Kind = "error", Error = error,
                SessionId = _session?.State.SessionId ?? string.Empty,
                Sequence = _editSequences.GetValueOrDefault(target)
            }, target);
    }

    private void Send(RewardPoolSharedDraftEnvelope envelope, ulong? target = null)
    {
        if (!_registered || !_net.IsConnected)
            return;
        envelope.Protocol = ProtocolVersion;
        envelope.Catalog = _catalogFingerprint;
        envelope.ClientToken = _clientToken;
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > RewardPoolSharedDraftMessage.MaxUtf8Bytes)
            throw new InvalidDataException("Shared draft snapshot exceeds the message size limit.");
        var wrapper = new CustomMessageWrapper
        {
            Message = new RewardPoolSharedDraftMessage { Json = json }
        };
        if (target is { } peer)
            _net.SendMessage(wrapper, peer);
        else
            _net.SendMessage(wrapper);
    }

    private void CancelLocally(string reason, bool broadcast)
    {
        if (broadcast)
            Send(new RewardPoolSharedDraftEnvelope
            {
                Kind = "cancel", Error = reason, Sequence = ++_broadcastSequence,
                SessionId = _session?.State.SessionId ?? string.Empty
            });
        _session = null;
        _finalAcks.Clear();
        _editSequences.Clear();
        _reconnecting.Clear();
        _remoteCanEmbark = false;
        _lastOwnAcknowledged = _ownSequence;
        _error = reason;
        ClearPendingLaunch();
        if (_net.IsConnected)
            _lobby.SetReady(false);
    }

    private static string ComputeCatalogFingerprint()
    {
        var text = new StringBuilder("shared-draft:1;supply:(n+1)*3,(n+1)*20;main:80;packages:3;")
            .Append(RewardPoolConstructionCatalog.PackageVersion).Append(';')
            .Append(RewardPoolSizePolicy.CurrentRulesVersion).Append(';');
        foreach (var package in RewardPoolConstructionCatalog.Packages.OrderBy(package => package.Id, StringComparer.Ordinal))
            text.Append(package.Id).Append(':').Append(package.Origin).Append(':').Append(package.IsSupport)
                .Append(':').AppendJoin(',', package.CardIds.Order(StringComparer.Ordinal)).Append(';');
        foreach (var card in RewardPoolCatalog.GetRequiredMainCards().Concat(RewardPoolCatalog.GetMainRewardCandidates(true))
                     .Concat(RewardPoolCatalog.GetAllExtraRewardCandidates()).OrderBy(card => card.Id.ToString(), StringComparer.Ordinal))
            text.Append(card.Id).Append(':').Append(card.Rarity).Append(':').Append(card.MultiplayerConstraint).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private void DetachNetwork()
    {
        if (!_registered)
            return;
        _net.UnregisterMessageHandler(_handler);
        if (_net is INetHostGameService host)
            host.ClientDisconnected -= OnPeerDisconnected;
        _registered = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(_screen))
            _screen.TreeExiting -= OnScreenTreeExiting;
        DetachNetwork();
        _view?.Dispose();
        _view = null;
    }

    private void OnScreenTreeExiting()
    {
        if (!_launchApplied)
        {
            Cancel();
            ClearPendingLaunch();
        }
        Dispose();
        if (ReferenceEquals(_current, this))
            _current = null;
    }

    internal static ulong? EnterTransportPacket(ulong sender)
    {
        var previous = _transportSender;
        _transportSender = sender;
        return previous;
    }

    internal static void ExitTransportPacket(ulong? previous) => _transportSender = previous;
}

// NetMessageBus exposes the serialized sender header. Bind our non-broadcast
// messages to the actual transport peer as well, so that header cannot nominate
// another player's draft intentions.
[HarmonyPatch]
internal static class RewardPoolSharedDraftTransportPeerPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(NetHostGameService), "OnPacketReceived");
        yield return AccessTools.Method(typeof(NetClientGameService), "OnPacketReceived");
    }

    [HarmonyPrefix]
    private static void Prefix(ulong __0, out ulong? __state) =>
        __state = RewardPoolMultiplayerLobby.EnterTransportPacket(__0);

    [HarmonyFinalizer]
    private static Exception Finalizer(ulong? __state, Exception __exception)
    {
        RewardPoolMultiplayerLobby.ExitTransportPacket(__state);
        return __exception;
    }
}

[HarmonyPatch]
internal static class RewardPoolSharedDraftNativeLaunchPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(NCharacterSelectScreen), "StartNewMultiplayerRun");
        yield return AccessTools.Method(typeof(NGame), nameof(NGame.StartNewMultiplayerRun));
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(object __instance, object[] __args,
        out RewardPoolMultiplayerLobby.SharedLaunchContext __state)
    {
        var lobby = (__instance as NCharacterSelectScreen)?.Lobby ?? __args.OfType<StartRunLobby>().FirstOrDefault();
        __state = RewardPoolMultiplayerLobby.EnterNativeLaunch(lobby);
    }

    [HarmonyPostfix]
    private static void Postfix(RewardPoolMultiplayerLobby.SharedLaunchContext __state) =>
        RewardPoolMultiplayerLobby.ExitNativeLaunch(__state);

    [HarmonyFinalizer]
    private static Exception Finalizer(RewardPoolMultiplayerLobby.SharedLaunchContext __state, Exception __exception)
    {
        RewardPoolMultiplayerLobby.ExitNativeLaunch(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(RunManager), "InitializeSavedRun")]
internal static class RewardPoolSharedDraftSavedRunPatch
{
    [HarmonyPrefix]
    private static void Prefix() => RewardPoolMultiplayerLobby.BeforeSavedRun();
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen._Process))]
internal static class RewardPoolMultiplayerProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance, double __0) => RewardPoolMultiplayerLobby.Tick(__instance, __0);
}

[HarmonyPatch(typeof(StartRunLobby), nameof(StartRunLobby.IsAboutToBeginGame))]
internal static class RewardPoolSharedDraftReadyPatch
{
    [HarmonyPostfix]
    private static void Postfix(StartRunLobby __instance, ref bool __result)
    {
        if (__result && !RewardPoolMultiplayerLobby.AllowNativeStart(__instance))
            __result = false;
    }
}
