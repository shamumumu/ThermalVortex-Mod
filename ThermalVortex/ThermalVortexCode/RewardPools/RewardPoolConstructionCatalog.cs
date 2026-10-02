using System.Collections.ObjectModel;
using System.Text.Json;
using Godot;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

public sealed record RewardPoolPackageAxis(string Id, string NameZh, string NameEn, IReadOnlyList<string> CardIds);

public sealed record RewardPoolPackage(
    string Id,
    RewardPoolCardOrigin Origin,
    string NameZh,
    string NameEn,
    bool IsSupport,
    IReadOnlyList<string> CardIds)
{
    public IReadOnlyList<RewardPoolPackageAxis> Axes { get; init; } = [];
    public IReadOnlyList<string> BridgeCardIds { get; init; } = [];
    public IReadOnlyList<string> CoreCardIds { get; init; } = [];
    public IReadOnlyList<string> AccessoryCardIds { get; init; } = [];
    public string SynergyZh { get; init; } = string.Empty;
    public string SynergyEn { get; init; } = string.Empty;
}

/// <summary>
/// Versioned construction data. Loading this catalog requires neither ModelDb
/// nor a running Godot scene; the caller checks compatibility with its live pool.
/// </summary>
public static class RewardPoolConstructionCatalog
{
    public const int GenesisBudget = 240;

    private static readonly Lazy<CatalogData> Data = new(Load);

    public static string PricingVersion => Data.Value.PricingVersion;
    public static string PackageVersion => Data.Value.PackageVersion;
    public static int PackageVariantVersion => Data.Value.PackageVariantVersion;

    public static IReadOnlyList<RewardPoolPackage> Packages => Data.Value.Packages;
    public static IReadOnlyList<string> GenericCardIds => Data.Value.GenericCardIds;

    public static bool TryGetPoints(string id, out int points)
    {
        points = 0;
        return !string.IsNullOrWhiteSpace(id)
            && Data.Value.Points.TryGetValue(id.Trim(), out points);
    }

    public static int TotalPoints(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var total = 0;
        foreach (var id in ids)
        {
            if (!TryGetPoints(id, out var points))
                throw new ArgumentException($"No construction price is defined for '{id ?? "<null>"}'.", nameof(ids));
            if (seen.Add(id.Trim()))
                total = checked(total + points);
        }
        return total;
    }

    public static string PackageName(RewardPoolPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? package.NameZh
            : package.NameEn;
    }

    public static string PackageSynergy(RewardPoolPackage package) =>
        TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? package.SynergyZh : package.SynergyEn;

    public static string AxisName(RewardPoolPackageAxis axis) =>
        TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? axis.NameZh : axis.NameEn;

    private static CatalogData Load()
    {
        using var pricing = ReadResource("construction-pricing.v1.json");
        var pricingVersion = RequiredString(pricing.RootElement, "pricingVersion");
        var points = new Dictionary<string, int>(StringComparer.Ordinal);
        var cardProfiles = new Dictionary<string, (string Origin, string Kind, string Rarity)>(StringComparer.Ordinal);
        foreach (var entry in pricing.RootElement.GetProperty("cards").EnumerateArray())
        {
            var id = RequiredString(entry, "id");
            var value = entry.GetProperty("points").GetInt32();
            var required = RequiredString(entry, "kind") == "required";
            if (!id.StartsWith("CARD.", StringComparison.Ordinal)
                || (required ? value != 0 : value is < 1 or > 10)
                || !points.TryAdd(id, value))
                throw new InvalidDataException($"Invalid or duplicate construction price: {id}.");
            cardProfiles.Add(id, (entry.TryGetProperty("origin", out var cardOrigin) ? cardOrigin.GetString() : string.Empty,
                RequiredString(entry, "kind"), entry.TryGetProperty("rarity", out var rarity) ? rarity.GetString() : string.Empty));
        }

        using var packageDocument = ReadResource("construction-packages.v1.json");
        var packageVersion = RequiredString(packageDocument.RootElement, "packageVersion");
        var variantVersion = packageDocument.RootElement.TryGetProperty("packageVariantVersion", out var variant)
            ? variant.GetInt32() : 0;
        if (variantVersion < 0)
            throw new InvalidDataException("Invalid package-variant version.");
        var packages = new List<RewardPoolPackage>();
        var packageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in packageDocument.RootElement.GetProperty("packages").EnumerateArray())
        {
            var id = RequiredString(entry, "id");
            if (!packageIds.Add(id)
                || !Enum.TryParse<RewardPoolCardOrigin>(RequiredString(entry, "origin"), out var origin)
                || !Enum.IsDefined(origin)
                || origin == RewardPoolCardOrigin.ThermalVortex)
                throw new InvalidDataException($"Invalid or duplicate construction package: {id}.");

            var cardIds = entry.GetProperty("cardIds").EnumerateArray()
                .Select(card => card.GetString() ?? string.Empty).ToArray();
            if (cardIds.Length == 0
                || cardIds.Distinct(StringComparer.Ordinal).Count() != cardIds.Length
                || cardIds.Any(cardId => !points.TryGetValue(cardId, out var value) || value == 0))
                throw new InvalidDataException($"Invalid members in construction package: {id}.");

            var isSupport = entry.GetProperty("isSupport").GetBoolean();
            var axes = entry.GetProperty("axes").EnumerateArray().Select(axis => new RewardPoolPackageAxis(
                RequiredString(axis, "id"), RequiredString(axis, "nameZh"), RequiredString(axis, "nameEn"),
                Array.AsReadOnly(axis.GetProperty("cardIds").EnumerateArray().Select(card => card.GetString()).ToArray())))
                .ToArray();
            var bridges = entry.GetProperty("bridgeCardIds").EnumerateArray().Select(card => card.GetString()).ToArray();
            if ((!isSupport && (axes.Length != 2 || bridges.Length == 0))
                || axes.Any(axis => axis.CardIds.Count == 0 || axis.CardIds.Distinct(StringComparer.Ordinal).Count() != axis.CardIds.Count
                    || axis.CardIds.Any(cardId => !cardIds.Contains(cardId, StringComparer.Ordinal)))
                || bridges.Distinct(StringComparer.Ordinal).Count() != bridges.Length
                || bridges.Any(cardId => !cardIds.Contains(cardId, StringComparer.Ordinal))
                || (!isSupport && cardIds.Except(axes.SelectMany(axis => axis.CardIds), StringComparer.Ordinal).Any()))
                throw new InvalidDataException($"Invalid axes or bridges in construction package: {id}.");

            var core = variantVersion > 0 && !isSupport
                ? entry.GetProperty("coreCardIds").EnumerateArray().Select(card => card.GetString()).ToArray() : Array.Empty<string>();
            var accessories = variantVersion > 0 && !isSupport
                ? entry.GetProperty("accessoryCardIds").EnumerateArray().Select(card => card.GetString()).ToArray() : Array.Empty<string>();
            if (variantVersion > 0 && !isSupport
                && (core.Length == 0 || core.Length >= cardIds.Length
                    || core.Distinct(StringComparer.Ordinal).Count() != core.Length
                    || accessories.Distinct(StringComparer.Ordinal).Count() != accessories.Length
                    || core.Any(card => !cardIds.Contains(card, StringComparer.Ordinal))
                    || accessories.Any(card => core.Contains(card, StringComparer.Ordinal))
                    || accessories.Length < cardIds.Length - core.Length
                    || cardIds.Except(core.Concat(accessories), StringComparer.Ordinal).Any()
                    || bridges.Any(card => !core.Contains(card, StringComparer.Ordinal))
                    || axes.Any(axis => !axis.CardIds.Any(card => core.Contains(card, StringComparer.Ordinal)))
                    || core.Concat(accessories).Any(card => string.IsNullOrWhiteSpace(card)
                        || !cardProfiles.TryGetValue(card, out var profile) || profile.Kind != "main"
                        || profile.Origin != origin.ToString() || profile.Rarity is not ("Common" or "Uncommon" or "Rare")
                        || !points.TryGetValue(card, out var price) || price == 0)))
                throw new InvalidDataException($"Invalid fixed core or finite accessories in construction package: {id}.");

            packages.Add(new RewardPoolPackage(
                id,
                origin,
                RequiredString(entry, "nameZh"),
                RequiredString(entry, "nameEn"),
                isSupport,
                Array.AsReadOnly(cardIds))
            {
                Axes = Array.AsReadOnly(axes), BridgeCardIds = Array.AsReadOnly(bridges),
                CoreCardIds = Array.AsReadOnly(core), AccessoryCardIds = Array.AsReadOnly(accessories),
                SynergyZh = RequiredString(entry, "synergyZh"), SynergyEn = RequiredString(entry, "synergyEn")
            });
        }

        var genericIds = packageDocument.RootElement.GetProperty("genericCardIds").EnumerateArray()
            .Select(card => card.GetString()).ToArray();
        if (genericIds.Length < 20 || genericIds.Distinct(StringComparer.Ordinal).Count() != genericIds.Length
            || genericIds.Any(id => !points.TryGetValue(id, out var value) || value == 0))
            throw new InvalidDataException("Invalid generic-card whitelist.");
        return new CatalogData(pricingVersion, packageVersion, variantVersion,
            new ReadOnlyDictionary<string, int>(points), packages.AsReadOnly(), Array.AsReadOnly(genericIds));
    }

    private static JsonDocument ReadResource(string fileName)
    {
        var assembly = typeof(RewardPoolConstructionCatalog).Assembly;
        var suffix = ".RewardPools.Data." + fileName;
        var resource = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Missing construction catalog resource: {fileName}.");
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException($"Cannot open construction catalog resource: {fileName}.");
        return JsonDocument.Parse(stream);
    }

    private static string RequiredString(JsonElement element, string property)
    {
        var value = element.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Construction catalog property '{property}' is empty.");
    }

    private sealed record CatalogData(
        string PricingVersion,
        string PackageVersion,
        int PackageVariantVersion,
        IReadOnlyDictionary<string, int> Points,
        IReadOnlyList<RewardPoolPackage> Packages,
        IReadOnlyList<string> GenericCardIds);
}
