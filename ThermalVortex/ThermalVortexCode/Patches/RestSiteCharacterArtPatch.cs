using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using ThermalVortex.ThermalVortexCode.Character;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal static class RestSiteCharacterArtPatch
{
    private const string SpriteNodeName = "ThermalVortexRestSiteYugi";
    private const float RestSiteVisualScale = 0.489f;

    public static void Apply(NRestSiteCharacter restSiteCharacter, Player player)
    {
        if (restSiteCharacter is null || !MillenniumPuzzleCharacterArt.IsThermalVortex(player))
            return;

        var texture = MillenniumPuzzleCharacterArt.LoadTexture(
            player,
            "rest_site_yugi_unfinished.png",
            "rest_site_yugi.png");
        if (texture is null)
            return;

        var sprite = restSiteCharacter.FindChild(SpriteNodeName, false, false) as Sprite2D;
        if (sprite is null)
        {
            sprite = new Sprite2D
            {
                Name = SpriteNodeName,
                Centered = true
            };

            restSiteCharacter.AddChild(sprite);
            sprite.Owner = restSiteCharacter;
        }

        sprite.Texture = texture;
        sprite.Scale = new Vector2(RestSiteVisualScale, RestSiteVisualScale);
        sprite.Visible = true;

        HideVanillaVisuals(restSiteCharacter);
    }

    public static void FlipCustomSprite(NRestSiteCharacter restSiteCharacter)
    {
        if (restSiteCharacter?.FindChild(SpriteNodeName, false, false) is Sprite2D sprite)
            sprite.FlipH = !sprite.FlipH;
    }

    private static void HideVanillaVisuals(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child.Name == SpriteNodeName)
                continue;

            if (ShouldHideVanillaVisual(child) && child is CanvasItem canvasItem)
            {
                canvasItem.Visible = false;
                continue;
            }

            HideVanillaVisuals(child);
        }
    }

    private static bool ShouldHideVanillaVisual(Node node)
    {
        return node is CanvasItem
            && node is not Control
            && node is not Marker2D;
    }
}

[HarmonyPatch(typeof(NRestSiteCharacter), nameof(NRestSiteCharacter.Create))]
internal static class RestSiteCharacterCreateArtPatch
{
    private static void Postfix(Player player, NRestSiteCharacter __result)
    {
        RestSiteCharacterArtPatch.Apply(__result, player);
    }
}

[HarmonyPatch(typeof(NRestSiteCharacter), nameof(NRestSiteCharacter._Ready))]
internal static class RestSiteCharacterReadyArtPatch
{
    private static void Postfix(NRestSiteCharacter __instance)
    {
        RestSiteCharacterArtPatch.Apply(__instance, __instance.Player);
    }
}

[HarmonyPatch(typeof(NRestSiteCharacter), nameof(NRestSiteCharacter.FlipX))]
internal static class RestSiteCharacterFlipXArtPatch
{
    private static void Postfix(NRestSiteCharacter __instance)
    {
        RestSiteCharacterArtPatch.FlipCustomSprite(__instance);
    }
}
