using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using ThermalVortex.ThermalVortexCode.Character;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NCreature), nameof(NCreature.Create), [typeof(Creature)])]
internal static class BattleCharacterArtPatch
{
    private const string SpriteNodeName = "YugiSprite";
    private const string UnfinishedTextureName = "battle_visual_yugi_unfinished.png";
    private const string CompleteTextureName = "battle_visual_yugi.png";

    private static void Postfix(Creature __0, NCreature __result)
    {
        var player = __0?.Player;
        if (__result is null || !MillenniumPuzzleCharacterArt.IsThermalVortex(player))
        {
            return;
        }

        var sprite = __result.Visuals?.FindChild(SpriteNodeName, true, false) as Sprite2D;
        if (sprite is null)
        {
            return;
        }

        var texture = MillenniumPuzzleCharacterArt.LoadTexture(
            player,
            UnfinishedTextureName,
            CompleteTextureName);
        if (texture is not null)
        {
            sprite.Texture = texture;
        }
        YugiNativeActionController.Attach(sprite, __0);
    }
}
