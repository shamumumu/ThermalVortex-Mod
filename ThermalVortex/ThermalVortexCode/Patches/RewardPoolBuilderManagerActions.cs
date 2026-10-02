using Godot;
using ThermalVortex.ThermalVortexCode.RewardPools;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal sealed partial class RewardPoolBuilderSession
{
    private void ShowManagerCreationModes()
    {
        var content = BeginManagerModal(ManagerText("新建卡组", "New Deck"),
            ManagerText("选择构筑模式", "Choose a construction mode"), out var actions);
        void AddMode(string name, string nodeName, Action action)
        {
            var button = CreateManagerButton(name, new Vector2(170f, 60f));
            button.Name = nodeName;
            button.Pressed += () =>
            {
                CloseModal();
                _managerPendingCreationGroup = _managerGroup;
                _managerExistingBeforeCreation = RewardPoolPresetService.GetLibrarySnapshot().Presets.Select(preset => preset.Id).ToHashSet();
                action();
            };
            actions.AddChild(button);
        }
        AddMode(ManagerText("一般", "General"), "ManagerCreateGeneral", CreatePreset);
        AddMode("Genesis", "ManagerCreateGenesis", () =>
        {
            var now = DateTime.UtcNow;
            ShowEditorView(new RewardPoolPresetDraft(Guid.Empty, RewardPoolPresetService.GetDefaultName(),
                [], [], now, now, RewardPoolConstructionMethod.Genesis));
        });
        AddMode(ManagerText("三选一", "Draft"), "ManagerCreateDraft", () =>
        {
            _managerPendingCreationGroup = null;
            _managerExistingBeforeCreation = null;
            OpenDraft();
        });
        FinishModal(content, actions.GetChild<Control>(0));
    }

    // This is organization metadata only. Original SaveEditor and
    // RequestLeaveEditor retain their card, selection, and return behavior.
    private void AssignManagerCreatedPresetGroup()
    {
        if (_managerPendingCreationGroup is null || _managerExistingBeforeCreation is null) return;
        var group = _managerPendingCreationGroup;
        var existing = _managerExistingBeforeCreation;
        var savedId = _editingDraft?.Id ?? Guid.Empty;
        _managerPendingCreationGroup = null;
        _managerExistingBeforeCreation = null;
        if (group.Length == 0 || _managerGroups is null || savedId == Guid.Empty || existing.Contains(savedId)
            || !RewardPoolPresetService.TryGet(savedId, out _)) return;
        // SaveEditor publishes the durable ID before returning here. A discarded
        // name edit must not change the saved deck's creation group.
        if (!_managerGroups.TryAssign(savedId, group, out var error))
            MainFile.Logger.Info("Manager group assignment failed; saved preset remains intact: " + error);
    }

    private VBoxContainer BeginManagerModal(string title, string message, out GridContainer actions)
    {
        var content = BeginModal(title, message, out actions);
        _modalPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.008f, 0.015f, 0.024f, 0.99f), BorderColor = ManagerVisualBorder,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 20, CornerRadiusBottomRight = 20, CornerDetail = 1,
            ShadowColor = new Color(0f, 0f, 0f, 0.7f), ShadowSize = 12
        });
        foreach (var child in content.GetChildren())
            if (child is Label label) label.Modulate = ManagerVisualWhite;
        return content;
    }

    private void ManagerNotice(string message)
    {
        var content = BeginManagerModal(ManagerText("卡组管理", "Deck Manager"), message, out var actions);
        var close = CreateManagerButton(Localize("managerClose"), new Vector2(170f, 60f));
        close.Pressed += CloseModal;
        actions.AddChild(close);
        FinishModal(content, close);
    }

    private void ShowManagerDeckActions(Guid id)
    {
        if (!RewardPoolPresetService.TryGet(id, out var preset)) return;
        _selectedPresetId = id;
        _rememberedPresetId = id;
        _standardSelected = false;
        var content = BeginManagerModal(preset.Name, ConstructionMethodName(preset.ConstructionMethod), out var actions);
        void Add(string name, Action action)
        {
            var button = CreateManagerButton(name, new Vector2(170f, 60f));
            button.Pressed += () => { CloseModal(); action(); };
            actions.AddChild(button);
        }
        Add(Localize("managerEdit"), EditSelectedPreset);
        Add(Localize("managerUse"), UseSelectedPreset);
        Add(Localize("managerDuplicate"), () => DuplicateManagerPreset(id, _managerGroup));
        Add(Localize("managerRename"), RenameSelectedPreset);
        Add(Localize("managerDelete"), DeleteSelectedPreset);
        Add(Localize("managerCancel"), () => { });
        FinishModal(content, actions.GetChild<Control>(0));
    }

    private void ShowManagerGroups()
    {
        var content = BeginManagerModal(ManagerText("卡组分组", "Deck Groups"), string.Empty, out var actions);
        void Add(string name, Action action, bool disabled = false)
        {
            var button = CreateManagerButton(name, new Vector2(170f, 60f));
            button.Disabled = disabled;
            button.Pressed += () => { CloseModal(); action(); };
            actions.AddChild(button);
        }
        var readOnly = _managerGroups.LoadError.Length > 0;
        Add(ManagerText("新建分组", "New Group"), CreateManagerGroup, readOnly);
        Add(ManagerText("删除分组", "Delete Group"), DeleteManagerGroup, readOnly || _managerGroup.Length == 0);
        Add(ManagerText("重命名分组", "Rename Group"), RenameManagerGroup, readOnly || _managerGroup.Length == 0);
        Add(ManagerText("移动到分组", "Move to Group"), () => ShowManagerGroupTarget(ManagerBatchOperation.Move), readOnly);
        Add(ManagerText("复制到分组", "Copy to Group"), () => ShowManagerGroupTarget(ManagerBatchOperation.Copy), readOnly);
        Add(ManagerText("默认分组", "Default Group"), () => ChangeManagerGroup(string.Empty));
        foreach (var group in _managerGroups.GroupNames)
        {
            var captured = group;
            Add(captured, () => ChangeManagerGroup(captured));
        }
        // Keep the existing standard-pool choice accessible without adding a
        // synthetic deck tile or permanent bottom detail panel.
        Add(Localize("managerUseStandard"), UseStandard);
        Add(Localize("managerCancel"), () => { });
        FinishModal(content, actions.GetChild<Control>(Math.Min(5, actions.GetChildCount() - 1)));
    }

    private void ChangeManagerGroup(string group)
    {
        _managerGroup = group;
        _selectedPresetId = null;
        _rememberedPresetId = null;
        _managerMarkedPresets.Clear();
        RefreshLibrary(null);
        DeferFocus(_createPresetTile);
    }

    private void ManagerNameModal(string title, string initial, Action<string> commit)
    {
        var content = BeginManagerModal(title, string.Empty, out var actions);
        var input = CreateLineEdit(ManagerText("分组名称", "Group name"));
        input.Text = initial;
        input.MaxLength = RewardPoolManagerGroups.MaximumNameLength;
        content.AddChild(input);
        content.MoveChild(input, Math.Max(0, content.GetChildCount() - 2));
        var confirm = CreateManagerButton(ManagerText("确定", "Confirm"), new Vector2(170f, 60f));
        confirm.Pressed += () => { var name = input.Text; CloseModal(); commit(name); };
        actions.AddChild(confirm);
        var cancel = CreateManagerButton(Localize("managerCancel"), new Vector2(170f, 60f));
        cancel.Pressed += CloseModal;
        actions.AddChild(cancel);
        FinishModal(content, input);
    }

    private void CreateManagerGroup() => ManagerNameModal(ManagerText("新建分组", "New Group"), string.Empty, name =>
    {
        if (!_managerGroups.TryCreate(name, out var error)) { ManagerNotice(error); return; }
        ChangeManagerGroup(name.Trim());
    });

    private void RenameManagerGroup()
    {
        var previous = _managerGroup;
        ManagerNameModal(ManagerText("重命名分组", "Rename Group"), previous, name =>
        {
            if (!_managerGroups.TryRename(previous, name, out var error)) { ManagerNotice(error); return; }
            ChangeManagerGroup(name.Trim());
        });
    }

    private void DeleteManagerGroup()
    {
        var group = _managerGroup;
        var content = BeginManagerModal(ManagerText("删除分组", "Delete Group"),
            ManagerText($"删除“{group}”分组？其中的卡组将保留在默认分组。", $"Delete group '{group}'? Its decks will remain in the default group."), out var actions);
        var confirm = CreateManagerButton(ManagerText("确定", "Confirm"), new Vector2(170f, 60f));
        confirm.Pressed += () =>
        {
            CloseModal();
            if (!_managerGroups.TryDelete(group, out var error)) { ManagerNotice(error); return; }
            ChangeManagerGroup(string.Empty);
        };
        actions.AddChild(confirm);
        var cancel = CreateManagerButton(Localize("managerCancel"), new Vector2(170f, 60f));
        cancel.Pressed += CloseModal;
        actions.AddChild(cancel);
        FinishModal(content, cancel);
    }

    private void ShowManagerGroupTarget(ManagerBatchOperation operation)
    {
        var content = BeginManagerModal(ManagerText("选择目标分组", "Choose Destination Group"), string.Empty, out var actions);
        foreach (var group in new[] { string.Empty }.Concat(_managerGroups.GroupNames))
        {
            var captured = group;
            var button = CreateManagerButton(group.Length == 0 ? ManagerText("默认分组", "Default Group") : group, new Vector2(170f, 60f));
            button.Pressed += () => { CloseModal(); BeginManagerBatch(operation, captured); };
            actions.AddChild(button);
        }
        FinishModal(content, actions.GetChild<Control>(0));
    }

    private void BeginManagerBatch(ManagerBatchOperation operation, string targetGroup = "")
    {
        _managerBatch = operation;
        _managerTargetGroup = targetGroup;
        _managerMarkedPresets.Clear();
        UpdateManagerBatchControls();
        ApplyManagerTileStyles();
        ConfigureManagerFocus();
    }

    private void CancelManagerBatch()
    {
        _managerBatch = ManagerBatchOperation.None;
        _managerMarkedPresets.Clear();
        UpdateManagerBatchControls();
        ApplyManagerTileStyles();
        ConfigureManagerFocus();
        DeferFocus(_managerDeleteButton);
    }

    private void UpdateManagerBatchControls()
    {
        if (!IsValid(_managerTitle)) return;
        var batch = _managerBatch != ManagerBatchOperation.None;
        _managerTitle.Text = _managerBatch switch
        {
            ManagerBatchOperation.Delete => ManagerText("删除卡组", "Delete Decks"),
            ManagerBatchOperation.Move => ManagerText("移动卡组", "Move Decks"),
            ManagerBatchOperation.Copy => ManagerText("复制卡组", "Copy Decks"),
            _ => ManagerText("编辑卡组", "Edit Deck")
        };
        _managerGroupButton.Visible = !batch;
        _managerPickupButton.Visible = !batch;
        _managerDeleteButton.Visible = !batch;
        _managerCancelButton.Visible = batch;
        _managerOnlineButton.Visible = !batch;
        _librarySearch.Visible = !batch;
        _managerConfirmButton.Visible = batch;
        _managerConfirmButton.Disabled = _managerMarkedPresets.Count == 0;
        _managerConfirmButton.Text = _managerBatch switch
        {
            ManagerBatchOperation.Delete => ManagerText("确认删除", "Confirm Delete"),
            ManagerBatchOperation.Move => ManagerText("确认移动", "Confirm Move"),
            _ => ManagerText("确认复制", "Confirm Copy")
        };
    }

    private void ConfirmManagerBatch()
    {
        if (_managerMarkedPresets.Count == 0 || _managerBatch == ManagerBatchOperation.None) return;
        var ids = _managerMarkedPresets.ToArray();
        var operation = _managerBatch;
        var target = _managerTargetGroup;
        if (operation != ManagerBatchOperation.Delete) { CommitManagerBatch(operation, ids, target); return; }
        var content = BeginManagerModal(Localize("managerDeleteTitle"),
            ManagerText($"删除选中的 {ids.Length} 个卡组？", $"Delete the {ids.Length} selected decks?"), out var actions);
        var confirm = CreateManagerButton(Localize("managerDeleteConfirm"), new Vector2(170f, 60f));
        confirm.Pressed += () => { CloseModal(); CommitManagerBatch(operation, ids, target); };
        actions.AddChild(confirm);
        var cancel = CreateManagerButton(Localize("managerCancel"), new Vector2(170f, 60f));
        cancel.Pressed += CloseModal;
        actions.AddChild(cancel);
        FinishModal(content, cancel);
    }

    private void CommitManagerBatch(ManagerBatchOperation operation, IReadOnlyList<Guid> ids, string target)
    {
        var completed = 0;
        string error = null;
        if (operation == ManagerBatchOperation.Move)
        {
            if (!_managerGroups.TryAssignMany(ids, target, out error)) completed = 0;
            else completed = ids.Count;
        }
        else
        {
            foreach (var id in ids)
            {
                if (!RewardPoolPresetService.TryGet(id, out _)) { error = Localize("errorPresetMissing"); break; }
                if (operation == ManagerBatchOperation.Copy)
                {
                    if (!RewardPoolPresetService.TryDuplicate(id, out var copy, out var result)) { error = FormatOperationError(result); break; }
                    _libraryChanged = true;
                    if (!_managerGroups.TryAssign(copy.Id, target, out error))
                    {
                        var failure = BuildManagerCopyGroupFailureMessages(copy, error);
                        error = ManagerText(failure.Chinese, failure.English);
                        break;
                    }
                    completed++;
                }
                else
                {
                    var wasActive = RewardPoolPresetService.GetLibrarySnapshot().ActivePresetId == id;
                    if (!RewardPoolPresetService.TryDelete(id, out var result)) { error = FormatOperationError(result); break; }
                    _libraryChanged = true;
                    _selectionChanged |= wasActive;
                    completed++;
                }
            }
        }
        CancelManagerBatch();
        RefreshLibrary(null);
        if (!string.IsNullOrEmpty(error))
            ManagerNotice(ManagerText($"已完成 {completed} 项；其余操作未完成。\n{error}", $"Completed {completed} items; the remaining operations did not complete.\n{error}"));
    }

    private void DuplicateManagerPreset(Guid id, string group)
    {
        if (!RewardPoolPresetService.TryDuplicate(id, out var copy, out var result)) { ShowOperationError(Localize("managerSaveFailed"), result); return; }
        _libraryChanged = true;
        if (!_managerGroups.TryAssign(copy.Id, group, out var error))
        {
            RefreshLibrary(null);
            var failure = BuildManagerCopyGroupFailureMessages(copy, error);
            ManagerNotice(ManagerText(failure.Chinese, failure.English));
            return;
        }
        RefreshLibrary(copy.Id);
        DeferFocus(GetSelectedPresetTileButton());
    }

    private static (string Chinese, string English) BuildManagerCopyGroupFailureMessages(
        RewardPoolPresetDraft copy, string error) =>
        ($"副本“{copy.Name}”已保存到默认分组，但目标分组归属未完成。可在默认分组移动这个已有副本，无需再次复制。\n{error}",
         $"Copy '{copy.Name}' was saved in the default group, but its destination group assignment did not complete. Move this existing copy from the default group; it does not need to be copied again.\n{error}");
}
