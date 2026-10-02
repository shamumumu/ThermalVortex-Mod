using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Vfx;

// Presentation only: a delayed fusion must not retain paid cards, UI nodes or
// texture objects. Only portrait paths, labels and scalar styling survive payment.
// Array order and duplicate entries represent the actual materials.
internal sealed class FusionMaterialVisualSnapshot
{
    private readonly FusionCardVisualSnapshot[] _cards;

    internal int MaterialCount { get; }

    internal FusionMaterialVisualSnapshot(int materialCount, IEnumerable<string> portraitPaths = null)
    {
        MaterialCount = Math.Max(0, materialCount);
        _cards = portraitPaths?.Select(path => new FusionCardVisualSnapshot(path)).ToArray() ?? [];
    }

    private FusionMaterialVisualSnapshot(int materialCount, FusionCardVisualSnapshot[] cards)
    {
        MaterialCount = Math.Max(0, materialCount);
        _cards = cards.ToArray();
    }

    internal FusionCardVisualSnapshot GetCard(int index) =>
        index >= 0 && index < _cards.Length ? _cards[index] : null;

    internal string GetPortraitPath(int index) =>
        GetCard(index)?.PortraitPath;

    internal FusionMaterialVisualSnapshot DeepClone() => new(MaterialCount, _cards);

    internal static FusionMaterialVisualSnapshot Capture(IReadOnlyList<CardModel> materials)
    {
        var cards = new FusionCardVisualSnapshot[materials?.Count ?? 0];
        for (var i = 0; i < cards.Length; i++)
            cards[i] = CaptureCard(materials[i]);
        return new FusionMaterialVisualSnapshot(cards.Length, cards);
    }

    internal static FusionCardVisualSnapshot CaptureCard(CardModel card)
    {
        if (card is null)
            return null;

        var portraitPath = CapturePortraitPath(card);
        var frameType = "Skill";
        var hue = 1f;
        var saturation = 0f;
        var value = 1f;
        try
        {
            frameType = card.Type.ToString();
            if (card.VisualCardPool is CustomCardPoolModel pool)
            {
                hue = pool.H;
                saturation = pool.S;
                value = pool.V;
            }
            else if (card.FrameMaterial is ShaderMaterial material)
            {
                var h = material.GetShaderParameter("h");
                var s = material.GetShaderParameter("s");
                var v = material.GetShaderParameter("v");
                if (h.VariantType != Variant.Type.Nil) hue = (float)h.AsDouble();
                if (s.VariantType != Variant.Type.Nil) saturation = (float)s.AsDouble();
                if (v.VariantType != Variant.Type.Nil) value = (float)v.AsDouble();
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"Fusion frame unavailable card={card.GetType().Name}: {ex.Message}");
        }

        var typeLabel = frameType;
        var rarity = "Common";
        try
        {
            typeLabel = card.Type.ToLocString().GetFormattedText();
            rarity = card.Rarity.ToString();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"Fusion card labels unavailable card={card.GetType().Name}: {ex.Message}");
        }

        var (bannerHue, bannerSaturation, bannerValue) = NativeBannerStyle(rarity);
        try
        {
            // NCard applies this same material to its title, portrait border
            // and type plaque. Preserve instance/custom banner parameters.
            if (card.BannerMaterial is ShaderMaterial banner)
            {
                var h = banner.GetShaderParameter("h");
                var s = banner.GetShaderParameter("s");
                var v = banner.GetShaderParameter("v");
                if (h.VariantType != Variant.Type.Nil) bannerHue = (float)h.AsDouble();
                if (s.VariantType != Variant.Type.Nil) bannerSaturation = (float)s.AsDouble();
                if (v.VariantType != Variant.Type.Nil) bannerValue = (float)v.AsDouble();
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"Fusion banner unavailable card={card.GetType().Name}: {ex.Message}");
        }

        return new FusionCardVisualSnapshot(portraitPath, frameType, hue, saturation, value,
            card is XyzMonsterCard, typeLabel, rarity, bannerHue, bannerSaturation, bannerValue);
    }

    // Current native banner material defaults, used if an instance cannot load its material.
    private static (float Hue, float Saturation, float Value) NativeBannerStyle(string rarity) =>
        rarity switch
        {
            "Uncommon" => (1f, 1f, 1f),
            "Rare" => (0.563f, 1.198f, 1.14f),
            "Curse" => (0.27f, 1.1f, 0.9f),
            "Status" => (0.634f, 0.35f, 0.8f),
            "Quest" => (0.515f, 1.727f, 0.9f),
            "Event" or "Special" => (0.875f, 0.85f, 0.9f),
            "Ancient" => (0f, 0.2f, 0.9f),
            _ => (1f, 0f, 0.85f)
        };

    internal static string CapturePortraitPath(CardModel card)
    {
        if (card is null)
            return null;

        // A custom large portrait can be missing even when the normal card art
        // exists. Read each property independently so a broken override is visual only.
        try
        {
            if (card is CustomCardModel custom)
            {
                var path = custom.CustomPortraitPath;
                if (IsAvailable(path))
                    return path;
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"Fusion large portrait unavailable card={card.GetType().Name}: {ex.Message}");
        }

        try
        {
            var path = card.PortraitPath;
            return IsAvailable(path) ? path : null;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"Fusion portrait unavailable card={card.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static bool IsAvailable(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && (ResourceLoader.Exists(path) || Godot.FileAccess.FileExists(path));
}

// An immutable visual value; title text, costs and rules text are deliberately omitted.
internal sealed record FusionCardVisualSnapshot(
    string PortraitPath,
    string FrameType = "Skill",
    float FrameHue = 0.756f,
    float FrameSaturation = 0.77f,
    float FrameValue = 0.75f,
    bool IsExtraDeck = false,
    string TypeLabel = "",
    string Rarity = "Common",
    float BannerHue = 1f,
    float BannerSaturation = 0f,
    float BannerValue = 0.85f)
{
    internal Godot.Collections.Dictionary ToSceneData(Texture2D portrait) => new()
    {
        ["portrait"] = Variant.From(portrait),
        ["type"] = FrameType,
        ["frame_h"] = FrameHue,
        ["frame_s"] = FrameSaturation,
        ["frame_v"] = FrameValue,
        ["extra_deck"] = IsExtraDeck,
        ["type_label"] = TypeLabel ?? string.Empty,
        ["rarity"] = Rarity,
        ["banner_h"] = BannerHue,
        ["banner_s"] = BannerSaturation,
        ["banner_v"] = BannerValue
    };
}
