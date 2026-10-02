using System.Globalization;
using System.Security;
using System.Text.Json;
using Godot;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

/// <summary>
/// Stores manager-only organization separately from the existing preset library.
/// Loading never writes, and an unreadable or unsupported file remains untouched.
/// </summary>
internal sealed class RewardPoolManagerGroups
{
    internal const string DefaultFileName = "ThermalVortex.reward_pool_manager_groups.json";
    internal const int MaximumNameLength = 64;
    private const int SchemaVersion = 1;
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly object _gate = new();
    private readonly string _path;
    private List<string> _groups = [];
    private Dictionary<Guid, string> _assignments = [];
    private byte[] _loadedBytes;

    internal RewardPoolManagerGroups(string absolutePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !Path.IsPathFullyQualified(absolutePath))
            {
                LoadError = "分组存储路径必须是绝对路径。";
                return;
            }

            _path = Path.GetFullPath(absolutePath);
            var bytes = ReadCurrentBytes();
            if (bytes is null)
                return;

            Parse(bytes, out var groups, out var assignments);
            _groups = groups;
            _assignments = assignments;
            _loadedBytes = bytes;
            GroupNames = Array.AsReadOnly(groups.ToArray());
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            LoadError = $"分组文件无法读取，已保留原文件：{exception.Message}";
        }
    }

    internal static RewardPoolManagerGroups LoadDefault()
    {
        try
        {
            return new RewardPoolManagerGroups(Path.Combine(OS.GetUserDataDir(), "mod_configs", DefaultFileName));
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return new RewardPoolManagerGroups(string.Empty)
            {
                LoadError = $"分组存储路径无法读取：{exception.Message}"
            };
        }
    }

    internal string LoadError { get; private set; } = string.Empty;

    // The default group is represented by an empty string, not an item here.
    internal IReadOnlyList<string> GroupNames { get; private set; } = Array.Empty<string>();

    internal string GroupOf(Guid presetId)
    {
        lock (_gate)
            return _assignments.TryGetValue(presetId, out var group) ? group : string.Empty;
    }

    internal bool TryCreate(string name, out string error)
    {
        lock (_gate)
        {
            if (!CanWrite(out error) || !TryNormalizeName(name, out name, out error))
                return false;
            if (_groups.Any(group => NameComparer.Equals(group, name)))
            {
                error = "已存在同名分组。";
                return false;
            }

            return TryCommit([.. _groups, name], new Dictionary<Guid, string>(_assignments), out error);
        }
    }

    internal bool TryRename(string old, string name, out string error)
    {
        lock (_gate)
        {
            if (!CanWrite(out error) || !TryResolveNamedGroup(old, out old, out error)
                || !TryNormalizeName(name, out name, out error))
                return false;
            if (_groups.Any(group => !NameComparer.Equals(group, old) && NameComparer.Equals(group, name)))
            {
                error = "已存在同名分组。";
                return false;
            }
            if (string.Equals(old, name, StringComparison.Ordinal))
                return true;

            var groups = _groups.Select(group => NameComparer.Equals(group, old) ? name : group).ToList();
            var assignments = _assignments.ToDictionary(pair => pair.Key,
                pair => NameComparer.Equals(pair.Value, old) ? name : pair.Value);
            return TryCommit(groups, assignments, out error);
        }
    }

    internal bool TryDelete(string name, out string error)
    {
        lock (_gate)
        {
            if (!CanWrite(out error) || !TryResolveNamedGroup(name, out name, out error))
                return false;

            var groups = _groups.Where(group => !NameComparer.Equals(group, name)).ToList();
            // Removing an association returns the preset to the default group.
            // No preset content or preset identifier is deleted by this store.
            var assignments = _assignments.Where(pair => !NameComparer.Equals(pair.Value, name))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            return TryCommit(groups, assignments, out error);
        }
    }

    internal bool TryAssign(Guid presetId, string name, out string error) =>
        TryAssignMany([presetId], name, out error);

    internal bool TryAssignMany(IReadOnlyList<Guid> presetIds, string name, out string error)
    {
        lock (_gate)
        {
            if (!CanWrite(out error) || !TryResolveAssignmentGroup(name, out name, out error))
                return false;
            if (presetIds is null || presetIds.Any(id => id == Guid.Empty))
            {
                error = "请选择已保存的牌组。";
                return false;
            }

            var assignments = new Dictionary<Guid, string>(_assignments);
            var changed = false;
            foreach (var id in presetIds)
            {
                if (name.Length == 0)
                    changed |= assignments.Remove(id);
                else if (!assignments.TryGetValue(id, out var previous)
                    || !string.Equals(previous, name, StringComparison.Ordinal))
                {
                    assignments[id] = name;
                    changed = true;
                }
            }
            return !changed || TryCommit(new List<string>(_groups), assignments, out error);
        }
    }

    private bool CanWrite(out string error)
    {
        error = LoadError;
        return string.IsNullOrEmpty(error);
    }

    private bool TryResolveAssignmentGroup(string name, out string resolved, out string error)
    {
        resolved = string.Empty;
        error = string.Empty;
        if (name == string.Empty)
            return true;
        return TryResolveNamedGroup(name, out resolved, out error);
    }

    private bool TryResolveNamedGroup(string name, out string resolved, out string error)
    {
        resolved = string.Empty;
        if (!TryNormalizeName(name, out name, out error))
            return false;
        resolved = _groups.FirstOrDefault(group => NameComparer.Equals(group, name));
        if (resolved is not null)
            return true;
        resolved = string.Empty;
        error = "该分组不存在。";
        return false;
    }

    private static bool TryNormalizeName(string value, out string name, out string error)
    {
        name = value?.Trim() ?? string.Empty;
        error = string.Empty;
        if (name.Length == 0 || name.Length > MaximumNameLength)
        {
            error = $"分组名须为 1–{MaximumNameLength} 个字符；默认分组不能重命名或删除。";
            return false;
        }
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsControl(character) || char.GetUnicodeCategory(character) == UnicodeCategory.Format
                || (char.IsHighSurrogate(character) && (index + 1 >= name.Length || !char.IsLowSurrogate(name[++index])))
                || char.IsLowSurrogate(character))
            {
                error = "分组名不能包含控制字符或无效字符。";
                return false;
            }
        }
        return true;
    }

    private bool TryCommit(List<string> groups, Dictionary<Guid, string> assignments, out string error)
    {
        error = string.Empty;
        string temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(directory))
                throw new IOException("分组文件没有有效的父目录。");

            // Reject stale instances instead of overwriting another edit, including
            // a file that became corrupt after this manager opened.
            if (!MatchesLoadedFile(ReadCurrentBytes()))
                throw new IOException("分组文件已被其它操作更改，请重新打开牌组管理页。");

            var bytes = Serialize(groups, assignments);
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, System.IO.FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (!File.ReadAllBytes(temporaryPath).AsSpan().SequenceEqual(bytes))
                throw new IOException("分组文件暂存校验失败。");
            if (!MatchesLoadedFile(ReadCurrentBytes()))
                throw new IOException("分组文件已被其它操作更改，请重新打开牌组管理页。");

            // The atomic rename is the commit point. Everything fallible happens
            // before it; only in-memory publication follows a successful rename.
            var groupNames = Array.AsReadOnly(groups.ToArray());
            if (_loadedBytes is null)
                File.Move(temporaryPath, _path);
            else
                File.Replace(temporaryPath, _path, null, ignoreMetadataErrors: true);
            temporaryPath = null;
            _loadedBytes = bytes;
            _groups = groups;
            _assignments = assignments;
            GroupNames = groupNames;
            return true;
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            error = $"分组未保存，现有分组保持不变：{exception.Message}";
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception exception) when (IsStoreException(exception)) { }
            }
        }
    }

    private byte[] ReadCurrentBytes()
    {
        try { return File.ReadAllBytes(_path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private bool MatchesLoadedFile(byte[] current) => _loadedBytes is null
        ? current is null
        : current is not null && current.AsSpan().SequenceEqual(_loadedBytes);

    private static byte[] Serialize(List<string> groups, Dictionary<Guid, string> assignments)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteStartArray("groups");
            foreach (var group in groups)
                writer.WriteStringValue(group);
            writer.WriteEndArray();
            writer.WriteStartObject("presetGroups");
            foreach (var pair in assignments.OrderBy(pair => pair.Key))
                writer.WriteString(pair.Key.ToString("D"), pair.Value);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void Parse(byte[] bytes, out List<string> groups, out Dictionary<Guid, string> assignments)
    {
        ReadOnlyMemory<byte> json = bytes;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            json = json[3..];
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("分组文件内容必须是对象。");
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!properties.TryAdd(property.Name, property.Value))
                throw new JsonException("分组文件包含重复字段。");
        if (properties.Count != 3 || !properties.TryGetValue("schemaVersion", out var schema)
            || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != SchemaVersion
            || !properties.TryGetValue("groups", out var storedGroups) || storedGroups.ValueKind != JsonValueKind.Array
            || !properties.TryGetValue("presetGroups", out var storedAssignments) || storedAssignments.ValueKind != JsonValueKind.Object)
            throw new JsonException("分组文件版本或结构不受支持。");

        groups = [];
        assignments = [];
        var names = new HashSet<string>(NameComparer);
        foreach (var element in storedGroups.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String
                || !TryNormalizeName(element.GetString(), out var name, out _)
                || !string.Equals(name, element.GetString(), StringComparison.Ordinal) || !names.Add(name))
                throw new JsonException("分组文件包含无效或重复的分组名。");
            groups.Add(name);
        }
        foreach (var property in storedAssignments.EnumerateObject())
        {
            if (!Guid.TryParse(property.Name, out var id) || id == Guid.Empty
                || property.Value.ValueKind != JsonValueKind.String || assignments.ContainsKey(id))
                throw new JsonException("分组文件包含无效或重复的牌组标识。");
            var name = property.Value.GetString();
            if (name != string.Empty && !groups.Contains(name, StringComparer.Ordinal))
                throw new JsonException("分组文件引用了不存在的分组。");
            assignments.Add(id, name);
        }
    }

    private static bool IsStoreException(Exception exception) => exception is
        IOException or UnauthorizedAccessException or SecurityException or ArgumentException
        or NotSupportedException or JsonException or InvalidOperationException;
}
