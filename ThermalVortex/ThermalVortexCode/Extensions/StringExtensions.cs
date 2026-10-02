using Godot;

using System.Collections.Concurrent;

namespace ThermalVortex.ThermalVortexCode.Extensions;

//Mostly utilities to get asset paths.
public static class StringExtensions
{
    private static readonly ConcurrentDictionary<string, string> CardImagePaths =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, string> BigCardImagePaths =
        new(StringComparer.Ordinal);

    private static string ResourcePath(params string[] parts)
    {
        return string.Join("/", parts);
    }

    public static string ImagePath(this string path)
    {
        return ResourcePath(MainFile.ResPath, "images", path);
    }

    public static string CardImagePath(this string path)
    {
        return CardImagePaths.GetOrAdd(path, ResolveCardImagePath);
    }

    public static string BigCardImagePath(this string path)
    {
        if (CardPortraitResolutionContext.UseSmallCardImages)
            return path.CardImagePath();

        return BigCardImagePaths.GetOrAdd(path, ResolveBigCardImagePath);
    }

    private static string ResolveCardImagePath(string fileName)
    {
        var path = ResourcePath(MainFile.ResPath, "images", "card_portraits", fileName);
        if (ResourceLoader.Exists(path))
            return path;

        MainFile.Logger.Info("Could not find card image path: " + path);
        return ResourcePath(MainFile.ResPath, "images", "card_portraits", "card.png");
    }

    private static string ResolveBigCardImagePath(string fileName)
    {
        var path = ResourcePath(MainFile.ResPath, "images", "card_portraits", "big", fileName);
        if (ResourceLoader.Exists(path))
            return path;

        MainFile.Logger.Info("Could not find big card image path: " + path);
        return ResourcePath(MainFile.ResPath, "images", "card_portraits", "big", "card.png");
    }

    public static string PowerImagePath(this string path)
    {
        path = ResourcePath(MainFile.ResPath, "images", "powers", path);
        if (ResourceLoader.Exists(path)) return path;

        MainFile.Logger.Info("Could not find power image path: " + path);
        return ResourcePath(MainFile.ResPath, "images", "powers", "power.png");
    }

    public static string BigPowerImagePath(this string path)
    {
        path = ResourcePath(MainFile.ResPath, "images", "powers", "big", path);
        if (ResourceLoader.Exists(path)) return path;

        MainFile.Logger.Info("Could not find big power image path: " + path);
        return ResourcePath(MainFile.ResPath, "images", "powers", "big", "power.png");
    }

    public static string RelicImagePath(this string path)
    {
        path = ResourcePath(MainFile.ResPath, "images", "relics", path);
        if (ResourceLoader.Exists(path)) return path;

        MainFile.Logger.Info("Could not find relic image path: " + path);
        return ResourcePath(MainFile.ResPath, "images", "relics", "relic.png");
    }

    public static string BigRelicImagePath(this string path)
    {
        path = ResourcePath(MainFile.ResPath, "images", "relics", "big", path);
        if (ResourceLoader.Exists(path)) return path;

        MainFile.Logger.Info("Could not find big relic image path: " + path);
        return ResourcePath(MainFile.ResPath, "images", "relics", "big", "relic.png");
    }

    public static string CharacterUiPath(this string path)
    {
        return ResourcePath(MainFile.ResPath, "images", "charui", path);
    }
}

internal static class CardPortraitResolutionContext
{
    [ThreadStatic]
    private static int _smallCardImageScopeDepth;

    internal static bool UseSmallCardImages => _smallCardImageScopeDepth > 0;

    internal static void EnterSmallCardImageScope() => _smallCardImageScopeDepth++;

    internal static void ExitSmallCardImageScope()
    {
        if (_smallCardImageScopeDepth > 0)
            _smallCardImageScopeDepth--;
    }
}
