using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Character;

internal static class MillenniumPuzzleCharacterArt
{
    private static readonly object LoggedUnavailablePathsLock = new();
    private static readonly HashSet<string> LoggedUnavailablePaths = new(StringComparer.Ordinal);

    public static bool IsThermalVortex(Player player)
    {
        var character = player?.Character;
        if (character is null)
            return false;

        return character is ThermalVortex
            || character.GetType().FullName == typeof(ThermalVortex).FullName
            || string.Equals(character.Id?.Entry, ThermalVortex.CharacterId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(character.Id?.ToString(), ThermalVortex.CharacterId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsComplete(Player player)
    {
        return player?.GetRelic<ThermalVortexCore>() is AncientThermalVortexCore;
    }

    public static Texture2D LoadTexture(
        Player player,
        string unfinishedFileName,
        string completeFileName)
    {
        var useCompleteTexture = IsComplete(player);
        var preferredPath = (useCompleteTexture ? completeFileName : unfinishedFileName).CharacterUiPath();
        var fallbackPath = (useCompleteTexture ? unfinishedFileName : completeFileName).CharacterUiPath();

        var texture = TryLoadTexture(preferredPath);
        if (texture is not null || string.Equals(preferredPath, fallbackPath, StringComparison.Ordinal))
            return texture;

        return TryLoadTexture(fallbackPath);
    }

    private static Texture2D TryLoadTexture(string path)
    {
        try
        {
            if (ResourceLoader.Exists(path))
            {
                var texture = ResourceLoader.Load<Texture2D>(path);
                if (texture is not null)
                    return texture;
            }

            LogUnavailablePath(path, "missing_or_invalid_texture");
        }
        catch (Exception exception)
        {
            LogUnavailablePath(path, exception.GetType().Name);
        }

        return null;
    }

    private static void LogUnavailablePath(string path, string reason)
    {
        lock (LoggedUnavailablePathsLock)
        {
            if (!LoggedUnavailablePaths.Add(path))
                return;
        }

        MainFile.Logger.Info($"Could not load ThermalVortex character art path={path} reason={reason}");
    }
}
