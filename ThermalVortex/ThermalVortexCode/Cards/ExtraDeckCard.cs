using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using ThermalVortex.ThermalVortexCode.Character;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Cards;

public abstract class ExtraDeckCard(int cost, CardType type, CardRarity rarity, TargetType target) :
    ThermalVortexCard(cost, type, rarity, target)
{
    // Extra Deck cards only enter combat through the dedicated summon flow.
    // Generic combat generators (Calamity, card potions, Discovery-style effects,
    // and similar sources) must keep using ordinary card-pool cards.
    public override bool CanBeGeneratedInCombat => false;
}

[Pool(typeof(ThermalVortexCardPool))]
public abstract class XyzMonsterCard : ExtraDeckCard
{
    public XyzMonsterCard(CardType type, CardRarity rarity, TargetType target) :
        base(0, type, rarity, target)
    {
        WithExplanations(KeywordExplanation("THERMALVORTEX-FUSION_MATERIAL"));
    }

    public abstract int MinimumMaterials { get; }
    public abstract int MonsterMaxHp { get; }
    public virtual int MaximumMaterials => int.MaxValue;
    public int Materials { get; set; }
    public LocString ExtraDeckStatusLine { get; set; }
    public int ExtraDeckEntryIndex { get; set; } = -1;
    public int ExtraDeckUpgradeIndex { get; set; } = -1;
    public int ExtraDeckEnchantmentIndex { get; set; } = -1;
    public bool IsExtraDeckUpgradeProxy { get; set; }
    public bool IsExtraDeckEnchantmentProxy { get; set; }
    public bool WasExtraSummonedThisTurn { get; set; }
    // The owned source survives successful summoning and rollback cleanup so
    // a returned physical card restores the status of the same owned copy.
    internal int ExtraDeckOwnedEntryIndex { get; set; } = -1;
    internal int ExtraDeckViewOwnedEntryIndex { get; set; } = -1;
    internal int ExtraDeckReturnIndex { get; set; } = -1;
    internal bool DoesNotCountTowardCardPlayLimit => ExtraDeckReturnIndex >= 0;
    internal bool IsExtraDeckSummonPlayAuthorized { get; private set; }
    internal bool IsResolvingAuthorizedExtraDeckSummon { get; private set; }

    protected override int CanonicalEnergyCost => MinimumMaterials;
    protected override bool HasEnergyCostX => UsesVariableMaterialCostDisplay;
    protected virtual bool UsesVariableMaterialCostDisplay => MinimumMaterials != MaximumMaterials;

    public override Material CreateCustomFrameMaterial =>
        ExtraDeckCardMaterials.CreateColdWhiteMaterial();

    protected override bool IsPlayable =>
        IsExtraDeckSummonPlayAuthorized && base.IsPlayable && MonsterFieldService.CanPlaceOnField(this);

    internal void AuthorizeExtraDeckSummonPlay()
    {
        IsResolvingAuthorizedExtraDeckSummon = false;
        IsExtraDeckSummonPlayAuthorized = true;
    }

    internal void ClearExtraDeckSummonPlayAuthorization()
    {
        IsExtraDeckSummonPlayAuthorized = false;
        IsResolvingAuthorizedExtraDeckSummon = false;
    }

    internal void PrepareForExtraDeckPlayCost()
    {
        MockSetEnergyCost(new CardEnergyCost(this, 0, false));
        InvokeEnergyCostChanged();
    }

    protected override void AddExtraArgsToDescription(LocString locString)
    {
        base.AddExtraArgsToDescription(locString);
        locString.Add("Materials", Materials > 0 ? Materials : MinimumMaterials);
        AddMonsterHpArg(locString, MonsterMaxHp);
        if (ExtraDeckStatusLine is not null)
            locString.Add("ExtraDeckStatusLine", ExtraDeckStatusLine);
        else
            locString.Add("ExtraDeckStatusLine", "");
    }

    protected override PileType GetResultPileTypeForCardPlay()
    {
        // The native play wrapper determines the result pile before it invokes
        // OnPlay. Preserve this one-play authorization for card-specific summon
        // presentation, then consume the ordinary IsPlayable authorization.
        if (IsExtraDeckSummonPlayAuthorized)
            IsResolvingAuthorizedExtraDeckSummon = true;
        var canPlaceOnField = MonsterFieldService.CanPlaceOnField(this);
        MonsterFieldService.UnmarkPending(this, keepFieldOrderReservation: canPlaceOnField);
        IsExtraDeckSummonPlayAuthorized = false;
        return canPlaceOnField ? MonsterFieldPile.FieldPileType : PileType.Exhaust;
    }
}

internal static class ExtraDeckCardMaterials
{
    private const string ColdWhiteShaderCode = @"shader_type canvas_item;

uniform vec4 shadow_tint : source_color = vec4(0.42, 0.55, 0.64, 1.0);
uniform vec4 mid_tint : source_color = vec4(0.78, 0.93, 0.98, 1.0);
uniform vec4 highlight_tint : source_color = vec4(1.0, 1.0, 0.97, 1.0);
uniform float contrast : hint_range(0.5, 2.0) = 1.32;
uniform float highlight_strength : hint_range(0.0, 0.35) = 0.16;
uniform float cool_edge_strength : hint_range(0.0, 0.35) = 0.12;

void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    float luma = dot(tex.rgb, vec3(0.299, 0.587, 0.114));
    float shade = clamp((luma - 0.5) * contrast + 0.5, 0.0, 1.0);

    vec3 cold = mix(shadow_tint.rgb, mid_tint.rgb, smoothstep(0.05, 0.78, shade));
    cold = mix(cold, highlight_tint.rgb, smoothstep(0.70, 0.98, shade));

    float edge = smoothstep(0.22, 0.55, shade) * (1.0 - smoothstep(0.80, 1.0, shade));
    float high = smoothstep(0.74, 1.0, shade);
    cold += vec3(0.0, 0.05, 0.09) * edge * cool_edge_strength;
    cold += vec3(0.12, 0.16, 0.18) * high * highlight_strength;

    COLOR = vec4(clamp(cold, vec3(0.0), vec3(1.0)), tex.a) * COLOR;
}
";
    private static Shader ColdWhiteShader;

    public static Material CreateColdWhiteMaterial()
        => new ShaderMaterial { Shader = LoadColdWhiteShader() };

    private static Shader LoadColdWhiteShader()
    {
        if (ColdWhiteShader is not null)
            return ColdWhiteShader;

        ColdWhiteShader = new Shader { Code = ColdWhiteShaderCode };
        return ColdWhiteShader;
    }
}
