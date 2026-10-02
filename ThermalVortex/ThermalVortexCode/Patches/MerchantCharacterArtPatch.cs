using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using ThermalVortex.ThermalVortexCode.Character;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NMerchantRoom), "AfterRoomIsLoaded")]
internal static class MerchantCharacterArtPatch
{
    private const string SpriteNodeName = "ThermalVortexMerchantYugi";
    private const string UnfinishedTextureFileName = "merchant_yugi_unfinished.png";
    private const string CompleteTextureFileName = "merchant_yugi_complete.png";
    private const float MerchantVisualScale = 1.1f;
    // Both merchant textures reserve this transparent space below the character's feet.
    private const float MerchantVisualBottomPadding = 35f;
    private const float MerchantVisualGroundOffset = 50f;
    private static bool loggedApplyFailure;

    private static void Postfix(NMerchantRoom __instance, List<Player> ____players)
    {
        // Harmony's three-underscore field prefix plus the game's `_players` field.
        var players = ____players;
        if (__instance is null || players is null)
            return;

        var playerVisuals = __instance.PlayerVisuals;
        if (playerVisuals is null)
            return;

        var count = Math.Min(players.Count, playerVisuals.Count);
        for (var index = 0; index < count; index++)
        {
            var player = players[index];
            if (!MillenniumPuzzleCharacterArt.IsThermalVortex(player))
                continue;

            Apply(playerVisuals[index], player);
        }
    }

    private static void Apply(NMerchantCharacter merchantCharacter, Player player)
    {
        if (merchantCharacter is null)
            return;

        var texture = MillenniumPuzzleCharacterArt.LoadTexture(
            player,
            UnfinishedTextureFileName,
            CompleteTextureFileName);
        if (texture is null)
            return;

        try
        {
            var sprite = merchantCharacter.FindChild(SpriteNodeName, false, false) as Sprite2D;
            if (sprite is null)
            {
                sprite = new Sprite2D
                {
                    Name = SpriteNodeName,
                    Centered = true
                };

                merchantCharacter.AddChild(sprite);
                sprite.Owner = merchantCharacter;
            }

            sprite.Texture = texture;
            sprite.Centered = true;
            sprite.Scale = new Vector2(MerchantVisualScale, MerchantVisualScale);
            sprite.Position = new Vector2(
                0f,
                (-texture.GetHeight() / 2f + MerchantVisualBottomPadding) * MerchantVisualScale
                    + MerchantVisualGroundOffset);
            sprite.Visible = true;

            HideOriginalVisuals(merchantCharacter);
        }
        catch (Exception exception)
        {
            if (loggedApplyFailure)
                return;

            loggedApplyFailure = true;
            MainFile.Logger.Info($"Could not apply ThermalVortex merchant character art error={exception}");
        }
    }

    private static void HideOriginalVisuals(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child.Name == SpriteNodeName)
                continue;

            if (child is CanvasItem canvasItem)
            {
                canvasItem.Visible = false;
                continue;
            }

            HideOriginalVisuals(child);
        }
    }
}
