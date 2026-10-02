using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Pooling;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using ThermalVortex.ThermalVortexCode.RewardPools;

namespace ThermalVortex.ThermalVortexCode.Patches;

/// <summary>Lobby-only built-in Godot controls; no mod-defined Node bridge is required.</summary>
internal sealed class RewardPoolMultiplayerScreen : IDisposable
{
    private readonly RewardPoolMultiplayerLobby _lobby;
    private readonly Control _root;
    private readonly Button _entry;
    private readonly Label _status;
    private readonly Label _roster;
    private readonly Label _phase;
    private readonly Label _detail;
    private readonly ItemList _choices;
    private readonly ItemList _packageCards;
    private readonly CenterContainer _previewHost;
    private readonly Button _toggle;
    private readonly Button _confirm;
    private readonly Button _start;
    private readonly Button _abandon;
    private readonly Button _mainTab;
    private readonly Button _extraTab;
    private readonly Button _packageTab;
    private readonly ConfirmationDialog _abandonDialog;
    private readonly NHotkeyManager _hotkeys;
    private readonly List<string> _choiceIds = [];
    private readonly List<string> _detailIds = [];
    private string _tab = "packages";
    private string _selectedId = string.Empty;
    private string _renderKey = string.Empty;
    private RewardPoolSharedDraftPhase? _previousPhase;
    private NCard _preview;
    private bool _blocked;
    private bool _disposed;

    internal bool IsOpen => !_disposed && _root.Visible;

    internal RewardPoolMultiplayerScreen(NCharacterSelectScreen host, RewardPoolMultiplayerLobby lobby)
    {
        _lobby = lobby;
        _hotkeys = NHotkeyManager.Instance;
        try
        {
        _entry = MakeButton("sharedDraftEntry");
        _entry.Name = "ThermalVortexSharedRewardPoolEntry";
        _entry.AnchorLeft = _entry.AnchorRight = .5f;
        _entry.AnchorTop = _entry.AnchorBottom = 1;
        _entry.OffsetLeft = -150;
        _entry.OffsetRight = 150;
        _entry.OffsetTop = -130;
        _entry.OffsetBottom = -82;
        _entry.ZIndex = 30;
        host.AddChild(_entry);
        _entry.Pressed += Show;

        _root = new Control
        {
            Name = "ThermalVortexSharedRewardPoolScreen", Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop, ZIndex = 210, ZAsRelative = false
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        host.AddChild(_root);
        var background = new ColorRect { Color = new Color(.025f, .035f, .07f, .99f), MouseFilter = Control.MouseFilterEnum.Stop };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(background);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "top", "right", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 24);
        _root.AddChild(margin);
        var layout = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        layout.AddThemeConstantOverride("separation", 12);
        margin.AddChild(layout);
        var title = MakeLabel(Text("sharedDraftTitle"));
        title.AddThemeFontSizeOverride("font_size", 27);
        layout.AddChild(title);
        _phase = MakeLabel(string.Empty);
        layout.AddChild(_phase);

        var tabs = new HBoxContainer();
        layout.AddChild(tabs);
        _packageTab = MakeButton("sharedDraftPackages");
        _mainTab = MakeButton("sharedDraftMain");
        _extraTab = MakeButton("sharedDraftExtra");
        tabs.AddChild(_packageTab);
        tabs.AddChild(_mainTab);
        tabs.AddChild(_extraTab);
        _packageTab.Pressed += () => ChangeTab("packages");
        _mainTab.Pressed += () => ChangeTab("main");
        _extraTab.Pressed += () => ChangeTab("extra");

        var panes = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        panes.AddThemeConstantOverride("separation", 18);
        layout.AddChild(panes);
        var rosterPane = new VBoxContainer { CustomMinimumSize = new Vector2(210, 0), SizeFlagsHorizontal = Control.SizeFlags.Fill };
        panes.AddChild(rosterPane);
        _roster = MakeLabel(string.Empty);
        rosterPane.AddChild(_roster);
        _status = MakeLabel(string.Empty);
        _status.Modulate = new Color(1f, .82f, .42f);
        rosterPane.AddChild(_status);
        var choicesPane = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.35f };
        panes.AddChild(choicesPane);
        _choices = new ItemList
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(300, 170),
            AllowReselect = true, SelectMode = ItemList.SelectModeEnum.Single
        };
        _choices.AddThemeFontSizeOverride("font_size", 18);
        choicesPane.AddChild(_choices);
        _toggle = MakeButton("sharedDraftToggle");
        choicesPane.AddChild(_toggle);
        _choices.ItemSelected += index => SelectChoice((int)index);
        _choices.ItemActivated += index => { SelectChoice((int)index); ToggleChoice(); };
        _toggle.Pressed += ToggleChoice;

        var detailPane = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(285, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = .85f
        };
        panes.AddChild(detailPane);
        _detail = MakeLabel(Text("sharedDraftChooseDetail"));
        detailPane.AddChild(_detail);
        _packageCards = new ItemList
        {
            CustomMinimumSize = new Vector2(0, 120), SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            AllowReselect = true
        };
        detailPane.AddChild(_packageCards);
        _packageCards.ItemSelected += index =>
        {
            if (index >= 0 && index < _detailIds.Count)
                Preview(_detailIds[(int)index]);
        };
        _previewHost = new CenterContainer
        {
            CustomMinimumSize = new Vector2(275, 340),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        detailPane.AddChild(_previewHost);
        _previewHost.Resized += FitPreview;

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 12);
        layout.AddChild(actions);
        _start = MakeButton("sharedDraftStart");
        _confirm = MakeButton("sharedDraftConfirm");
        _abandon = MakeButton("sharedDraftAbandon");
        var close = MakeButton("sharedDraftClose");
        actions.AddChild(_start);
        actions.AddChild(_confirm);
        actions.AddChild(_abandon);
        actions.AddChild(close);
        _start.Pressed += _lobby.Start;
        _confirm.Pressed += _lobby.Confirm;
        close.Pressed += Hide;
        _abandonDialog = new ConfirmationDialog
        {
            Title = Text("sharedDraftAbandon"), DialogText = Text("sharedDraftAbandonPrompt"),
            OkButtonText = Text("sharedDraftAbandon"), CancelButtonText = Text("sharedDraftKeep")
        };
        _root.AddChild(_abandonDialog);
        _abandon.Pressed += () => _abandonDialog.PopupCentered(new Vector2I(530, 190));
        _abandonDialog.Confirmed += _lobby.Cancel;
        }
        catch
        {
            if (_root is not null && GodotObject.IsInstanceValid(_root))
                _root.QueueFree();
            if (_entry is not null && GodotObject.IsInstanceValid(_entry))
                _entry.QueueFree();
            throw;
        }
    }

    internal static string Text(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;
        return new LocString("characters", "THERMALVORTEX-THERMAL_VORTEX.rewardPool." + key).GetFormattedText();
    }

    internal void Show()
    {
        if (_disposed)
            return;
        _root.Visible = true;
        _entry.Visible = false;
        if (!_blocked && GodotObject.IsInstanceValid(_hotkeys))
        {
            _hotkeys.AddBlockingScreen(_root);
            _blocked = true;
        }
        _renderKey = string.Empty;
        Refresh();
        _choices.GrabFocus();
    }

    internal void Hide()
    {
        if (_disposed)
            return;
        _root.Visible = false;
        _entry.Visible = true;
        if (_blocked && GodotObject.IsInstanceValid(_hotkeys))
            _hotkeys.RemoveBlockingScreen(_root);
        _blocked = false;
        _entry.GrabFocus();
    }

    internal void Refresh()
    {
        if (_disposed || !IsOpen)
            return;
        var state = _lobby.State;
        var key = state is null ? "none" : state.SessionId + ":" + state.ContentRevision + ":" + state.Phase
            + ":" + string.Join(',', state.Players.Select(pair => pair.Key + "=" + pair.Value.Confirmed));
        key += ":" + string.Join(',', _lobby.Connected) + ":" + _lobby.Error + ":" + _lobby.ReadyToEmbark
            + ":" + _lobby.IsCompatible + ":" + _lobby.IsAwaitingResponse + ":" + _lobby.HasMissingPlayers + ":" + _tab;
        if (_renderKey == key)
            return;
        _renderKey = key;
        if (_previousPhase != state?.Phase)
        {
            _previousPhase = state?.Phase;
            _tab = state?.Phase == RewardPoolSharedDraftPhase.Packages ? "packages" : "main";
            if (state is null)
                _tab = "packages";
            _selectedId = string.Empty;
        }
        var phaseKey = state?.Phase switch
        {
            RewardPoolSharedDraftPhase.Packages => "sharedDraftPhasePackages",
            RewardPoolSharedDraftPhase.Cards => "sharedDraftPhaseCards",
            RewardPoolSharedDraftPhase.Finalized or RewardPoolSharedDraftPhase.Consumed => "sharedDraftPhaseFinalized",
            _ => "sharedDraftIntro"
        };
        _phase.Text = Text(phaseKey);
        _start.Visible = state is null && _lobby.IsHost;
        _start.Disabled = !_lobby.CanStart;
        _confirm.Visible = state is not null && state.Phase is RewardPoolSharedDraftPhase.Packages or RewardPoolSharedDraftPhase.Cards;
        _confirm.Disabled = !_lobby.CanConfirm || state?.Players.GetValueOrDefault(_lobby.LocalId)?.Confirmed == true;
        _abandon.Visible = state is not null;
        _mainTab.Disabled = state is null || state.Phase == RewardPoolSharedDraftPhase.Packages;
        _extraTab.Disabled = _mainTab.Disabled;
        _packageTab.Disabled = state is null;
        _status.Text = Text(_lobby.Error);
        if (string.IsNullOrEmpty(_status.Text))
            _status.Text = _lobby.HasMissingPlayers ? Text("sharedDraftDisconnected")
                : _lobby.IsAwaitingResponse ? Text("sharedDraftSyncing")
                : state is null ? Text(_lobby.IsHost ? "sharedDraftRosterRequired" : "sharedDraftWaitHost")
                : _lobby.ReadyToEmbark ? Text("sharedDraftReady")
                : Text(_lobby.Validation);
        _roster.Text = state is null ? string.Empty : string.Join("\n\n", state.ParticipantIds.Select(id =>
        {
            var player = state.Players[id];
            var badge = !_lobby.Connected.Contains(id) ? "sharedDraftOffline"
                : player.Confirmed ? "sharedDraftConfirmed" : "sharedDraftEditing";
            return _lobby.PlayerName(id) + " · " + Text(badge)
                + "\n" + Text("sharedDraftPackages") + " " + player.PackageIds.Count + "/3"
                + "\n" + Text("sharedDraftMain") + " " + _lobby.MainCount(id) + "/80"
                + " · " + Text("sharedDraftExtra") + " " + (player.ExtraIds.Count + 1) + "/10";
        }));
        PopulateChoices(state);
    }

    private void ChangeTab(string tab)
    {
        _tab = tab;
        _selectedId = string.Empty;
        _renderKey = string.Empty;
        Refresh();
    }

    private void PopulateChoices(RewardPoolSharedDraftState state)
    {
        var scroll = _choices.GetVScrollBar().Value;
        _choices.Clear();
        _choiceIds.Clear();
        if (state is null)
        {
            ClearChoiceDetail();
            return;
        }
        var own = state.Players[_lobby.LocalId];
        IEnumerable<string> ids = _tab switch
        {
            "packages" => state.PackageOfferIds,
            "extra" => RewardPoolCatalog.GetExtraCandidates().Select(card => card.Id.ToString()),
            _ => state.CommonStock.Keys
        };
        var selected = _tab == "packages" ? own.PackageIds : _tab == "extra" ? own.ExtraIds : own.MainSingleIds;
        foreach (var id in ids)
        {
            var label = selected.Contains(id) ? "✓ " : "    ";
            var conflict = false;
            if (_tab == "packages")
            {
                var package = Package(id);
                label += RewardPoolConstructionCatalog.PackageName(package) + " (" + package.CardIds.Count + ")";
                var owners = state.ParticipantIds.Where(actor => state.Players[actor].PackageIds.Contains(id)).ToList();
                var ownCards = package.CardIds.ToHashSet(StringComparer.Ordinal);
                var cardOwners = state.ParticipantIds.Where(actor => state.Players[actor].PackageIds
                    .Any(packId => Package(packId).CardIds.Any(ownCards.Contains))).ToList();
                conflict = owners.Count > 1 || owners.Any(owner => cardOwners.Any(cardOwner => cardOwner != owner));
                if (owners.Count > 0)
                    label += " — " + string.Join(", ", owners.Select(_lobby.PlayerName));
            }
            else
            {
                label += CardTitle(id);
                if (_tab == "main")
                {
                    var wanted = state.Players.Values.Count(player => player.MainSingleIds.Contains(id));
                    var stock = state.CommonStock[id];
                    conflict = wanted > stock;
                    label += "  [" + wanted + "/" + stock + "]";
                }
            }
            if (conflict)
                label += "  ⚠ " + Text("sharedDraftConflict");
            var index = _choices.AddItem(label);
            if (conflict)
                _choices.SetItemCustomFgColor(index, new Color(1f, .45f, .42f));
            else if (selected.Contains(id))
                _choices.SetItemCustomFgColor(index, new Color(.6f, 1f, .7f));
            _choiceIds.Add(id);
        }
        _choices.GetVScrollBar().Value = scroll;
        var selectedIndex = _choiceIds.IndexOf(_selectedId);
        if (selectedIndex >= 0)
        {
            _choices.Select(selectedIndex);
            SelectChoice(selectedIndex);
        }
        else
        {
            ClearChoiceDetail();
        }
    }

    private void ClearChoiceDetail()
    {
        _selectedId = string.Empty;
        _toggle.Disabled = true;
        _detail.Text = Text("sharedDraftChooseDetail");
        _packageCards.Clear();
        _packageCards.Visible = false;
        _detailIds.Clear();
        ReleasePreview();
    }

    private void SelectChoice(int index)
    {
        if (index < 0 || index >= _choiceIds.Count)
            return;
        _selectedId = _choiceIds[index];
        var state = _lobby.State;
        _toggle.Disabled = _lobby.IsAwaitingResponse || state is null
            || (_tab == "packages" ? state.Phase != RewardPoolSharedDraftPhase.Packages : state.Phase != RewardPoolSharedDraftPhase.Cards);
        _packageCards.Clear();
        _detailIds.Clear();
        _packageCards.Visible = _tab == "packages";
        if (_tab == "packages")
        {
            var package = Package(_selectedId);
            _detail.Text = RewardPoolConstructionCatalog.PackageName(package) + " · " + package.CardIds.Count
                + "\n" + RewardPoolConstructionCatalog.PackageSynergy(package);
            foreach (var cardId in package.CardIds)
            {
                _detailIds.Add(cardId);
                var owners = state?.ParticipantIds.Where(actor => state.Players[actor].PackageIds
                    .Any(packageId => Package(packageId).CardIds.Contains(cardId))).ToArray() ?? [];
                var title = CardTitle(cardId);
                if (owners.Length > 0)
                    title += " — " + string.Join(", ", owners.Select(_lobby.PlayerName));
                var item = _packageCards.AddItem(title);
                if (owners.Length > 1)
                    _packageCards.SetItemCustomFgColor(item, new Color(1f, .45f, .42f));
            }
            if (_detailIds.Count > 0)
            {
                _packageCards.Select(0);
                Preview(_detailIds[0]);
            }
        }
        else
        {
            _detail.Text = CardTitle(_selectedId);
            if (_tab == "main" && state is not null)
            {
                var owners = state.ParticipantIds.Where(id => state.Players[id].MainSingleIds.Contains(_selectedId));
                _detail.Text += "\n" + Text("sharedDraftStock") + " " + state.CommonStock[_selectedId]
                    + "\n" + string.Join(", ", owners.Select(_lobby.PlayerName));
            }
            Preview(_selectedId);
        }
    }

    private void ToggleChoice()
    {
        if (_toggle.Disabled || string.IsNullOrEmpty(_selectedId) || _lobby.State is not { } state)
            return;
        var own = state.Players[_lobby.LocalId];
        var selection = _tab == "packages" ? own.PackageIds : _tab == "extra" ? own.ExtraIds : own.MainSingleIds;
        if (!selection.Remove(_selectedId))
            selection.Add(_selectedId);
        if (_tab == "packages")
            _lobby.SetPackages(selection);
        else
            _lobby.SetCards(own.MainSingleIds, own.ExtraIds);
        _renderKey = string.Empty;
        Refresh();
    }

    private static RewardPoolPackage Package(string id) => RewardPoolConstructionCatalog.Packages.First(package => package.Id == id);
    private static CardModel Card(string id) => RewardPoolCatalog.GetMainBuildCandidate(id, true)
        ?? RewardPoolCatalog.GetAllExtraRewardCandidates().FirstOrDefault(card => card.Id.ToString() == id);
    private static string CardTitle(string id)
    {
        try { return Card(id)?.Title ?? id; }
        catch { return id; }
    }

    private void Preview(string id)
    {
        ReleasePreview();
        try
        {
            var model = Card(id)?.ToMutable();
            if (model is null)
                return;
            _preview = NCard.Create(model, ModelVisibility.Visible);
            _preview.MouseFilter = Control.MouseFilterEnum.Ignore;
            _previewHost.AddChild(_preview);
            _preview.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
            Callable.From(FitPreview).CallDeferred();
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Shared reward-pool preview unavailable: " + exception.Message);
            ReleasePreview();
        }
    }

    private void FitPreview()
    {
        if (_preview is null || !GodotObject.IsInstanceValid(_preview))
            return;
        _preview.Scale = Vector2.One;
        var size = _preview.GetCurrentSize();
        if (size.X > 0 && size.Y > 0)
            _preview.Scale = Vector2.One * Math.Clamp(Math.Min((_previewHost.Size.X - 12) / size.X,
                (_previewHost.Size.Y - 12) / size.Y), .2f, 1f);
    }

    private void ReleasePreview()
    {
        if (_preview is not null && GodotObject.IsInstanceValid(_preview))
        {
            _preview.GetParent()?.RemoveChild(_preview);
            NodePool.Free(_preview);
        }
        _preview = null;
    }

    private static Button MakeButton(string key) => new()
    {
        Text = Text(key), CustomMinimumSize = new Vector2(160, 42),
        FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Stop
    };

    private static Label MakeLabel(string text) => new()
    {
        Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        Hide();
        _disposed = true;
        ReleasePreview();
        if (GodotObject.IsInstanceValid(_root))
            _root.QueueFree();
        if (GodotObject.IsInstanceValid(_entry))
            _entry.QueueFree();
    }
}
