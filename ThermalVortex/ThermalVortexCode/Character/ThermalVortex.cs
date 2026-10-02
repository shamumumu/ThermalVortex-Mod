using BaseLib.Abstracts;
using ThermalVortex.ThermalVortexCode.Extensions;
using Godot;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Combat;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Character;

// STS001 assumes the legacy Architect opening is character -> ancient. Our four
// validated dialogue groups deliberately open ancient -> character instead.
#pragma warning disable STS001
public class ThermalVortex : PlaceholderCharacterModel
#pragma warning restore STS001
{
    public const string CharacterId = "ThermalVortex";
    private const float BattleVisualScale = 0.6f;

    public static readonly Color Color = new("7b2cbf");
    private static readonly Color EnergyCounterOutlineColor = new("4a4a4a");
    private static readonly Color EnergyCounterBurstColor = new("969696");

    public override Color NameColor => Color;
    public override CharacterGender Gender => CharacterGender.Neutral;
    public override int StartingHp => 70;

    public override IEnumerable<CardModel> StartingDeck => [
        ModelDb.Card<Strike>(),
        ModelDb.Card<Strike>(),
        ModelDb.Card<Strike>(),
        ModelDb.Card<Strike>(),
        ModelDb.Card<Defend>(),
        ModelDb.Card<Defend>(),
        ModelDb.Card<Defend>(),
        ModelDb.Card<Defend>(),
        ModelDb.Card<InternalCombustion>(),
        ModelDb.Card<SectionPole>(),
        ModelDb.Card<XyzSummon>()
    ];

    public override IReadOnlyList<RelicModel> StartingRelics =>
    [
        ModelDb.Relic<ThermalVortexCore>()
    ];

    public override CardPoolModel CardPool => ModelDb.CardPool<ThermalVortexCardPool>();
    public override RelicPoolModel RelicPool => ModelDb.RelicPool<ThermalVortexRelicPool>();
    public override PotionPoolModel PotionPool => ModelDb.PotionPool<ThermalVortexPotionPool>();

    /*  PlaceholderCharacterModel will utilize placeholder basegame assets for most of your character assets until you
        override all the other methods that define those assets.
        These are just some of the simplest assets, given some placeholders to differentiate your character with.
        You don't have to, but you're suggested to rename these images. */
    public override Control CustomIcon
    {
        get
        {
            var icon = new TextureRect
            {
                Texture = ResourceLoader.Load<Texture2D>(CustomIconTexturePath),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            return icon;
        }
    }
    public override string CustomIconTexturePath => "character_icon_char_name.png".CharacterUiPath();
    public override string CustomCharacterSelectIconPath => "char_select_char_name.png".CharacterUiPath();
    public override string CustomCharacterSelectLockedIconPath => "char_select_char_name_locked.png".CharacterUiPath();
    public override string CustomMapMarkerPath => "map_marker_char_name.png".CharacterUiPath();
    public override CustomEnergyCounter? CustomEnergyCounter => new(
        layer => $"charui/energy_counter/layer_{layer}.png".ImagePath(),
        EnergyCounterOutlineColor,
        EnergyCounterBurstColor);

    public override NCreatureVisuals CreateCustomVisuals()
    {
        var visuals = new NCreatureVisuals
        {
            Name = "ThermalVortexVisuals",
            DefaultScale = 1f
        };

        var visualRoot = new Node2D
        {
            Name = "Visuals",
            UniqueNameInOwner = true,
            Position = new Vector2(0f, -165f),
            Scale = new Vector2(BattleVisualScale, BattleVisualScale)
        };
        AddOwnedChild(visuals, visuals, visualRoot);
        AddOwnedChild(visualRoot, visuals, new Sprite2D
        {
            Name = "YugiSprite",
            Texture = ResourceLoader.Load<Texture2D>("battle_visual_yugi.png".CharacterUiPath()),
            Centered = true
        });

        AddOwnedChild(visuals, visuals, new Control
        {
            Name = "Bounds",
            UniqueNameInOwner = true,
            OffsetLeft = -145f,
            OffsetTop = -330f,
            OffsetRight = 145f,
            OffsetBottom = 0f,
            MouseFilter = Control.MouseFilterEnum.Ignore
        });
        AddOwnedChild(visuals, visuals, new Marker2D
        {
            Name = "CenterPos",
            UniqueNameInOwner = true,
            Position = new Vector2(0f, -160f)
        });
        AddOwnedChild(visuals, visuals, new Marker2D
        {
            Name = "IntentPos",
            UniqueNameInOwner = true,
            Position = new Vector2(20f, -345f)
        });

        return visuals;
    }

    private static T AddOwnedChild<T>(Node parent, Node owner, T child) where T : Node
    {
        var uniqueNameInOwner = child.UniqueNameInOwner;
        parent.AddChild(child);
        child.Owner = owner;
        child.UniqueNameInOwner = uniqueNameInOwner;
        return child;
    }
}
