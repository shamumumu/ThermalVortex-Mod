using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberNextDragon : MonsterCard, ICyberMonster, ICyberDevourStats
{
    public const int BaseMaxHp = 5;
    private const int DamagePerMaterial = 7;
    private const int UpgradedDamagePerMaterial = 10;
    private int? _damageMaterialCountSnapshot;
    private int _resolvedMaterialCount;

    public override string CustomPortraitPath => "cyber_next_dragon.png".BigCardImagePath();
    public override string PortraitPath => "cyber_next_dragon.png".CardImagePath();
    public override string BetaPortraitPath => "cyber_next_dragon.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;
    public int CyberAttackContribution => FormulaDamageUnit * Math.Max(1, _resolvedMaterialCount);

    public CyberNextDragon() : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        WithFormulaDamage(
            DamagePerMaterial,
            UpgradedDamagePerMaterial,
            (card, _) => ((CyberNextDragon)card).GetDamageMaterialCount());
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-MATERIAL"),
            KeywordExplanation("THERMALVORTEX-CYBER_MONSTER"));
    }

    protected override bool IsPlayable
    {
        get
        {
            var owner = TryGetOwner();
            if (owner is null)
                return base.IsPlayable;

            var materials = NonCyberFieldMonsters();
            return materials.Count > 0
                && MonsterFieldService.CanPlaceOnFieldAfterRemoving(this, materials)
                && IsBasePlayableWithProjectedMaterials(owner, materials);
        }
    }

    internal bool CanPlaceOnFieldAfterUsingNonCyberMaterials()
    {
        var owner = TryGetOwner();
        if (owner is null)
            return MonsterFieldService.CanPlaceOnField(this);

        var materials = NonCyberFieldMonsters();
        if (materials.Count <= 0)
            return false;

        return MonsterFieldService.CanPlaceOnFieldAfterRemoving(this, materials);
    }

    protected override PileType GetResultPileTypeForCardPlay()
    {
        var owner = TryGetOwner();
        if (owner is null)
            return base.GetResultPileTypeForCardPlay();

        var materials = NonCyberFieldMonsters();
        if (!MonsterFieldService.CanPlaceOnFieldAfterRemoving(this, materials))
            return base.GetResultPileTypeForCardPlay();

        return GetBaseResultPileWithProjectedMaterials(owner, materials);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var materials = NonCyberFieldMonsters();
        if (materials.Count == 0
            || !MonsterFieldService.CanPlaceOnFieldAfterRemoving(this, materials))
            return;

        _damageMaterialCountSnapshot = materials.Count;
        _resolvedMaterialCount = materials.Count;
        try
        {
            using (MonsterFieldService.ReserveCapacitySlots(Owner))
                await MonsterFieldService.UseMaterialsToDiscard(ctx, materials, this);
            await ResolveMonsterSummon(play);

            if (play.Target is not null)
            {
                var inheritedAttack = CyberDevourState.GetInheritedAttackContribution(this);
                if (inheritedAttack > 0)
                {
                    var firstAttack = ThermalVortexCombatVfx.CardAttack(
                        this, play.Target, FormulaDamageUnit + inheritedAttack, 1);
                    var remainingAttack = _resolvedMaterialCount > 1
                        ? ThermalVortexCombatVfx.CardAttack(
                            this, play.Target, FormulaDamageUnit, _resolvedMaterialCount - 1)
                        : null;
                    using var mainOutput = remainingAttack is not null
                        ? CyberWeldingPower.BeginMainAttackOutput(this, firstAttack, remainingAttack)
                        : null;
                    await firstAttack.Execute(ctx);
                    if (remainingAttack is not null)
                        await remainingAttack.Execute(ctx);
                }
                else if (_resolvedMaterialCount > 0)
                {
                    await ThermalVortexCombatVfx.CardAttack(
                            this,
                            play.Target,
                            FormulaDamageUnit,
                            _resolvedMaterialCount)
                        .Execute(ctx);
                }
            }
        }
        finally
        {
            _damageMaterialCountSnapshot = null;
        }
    }

    protected override void OnUpgrade() => ConstructedUpgrade();

    private List<CardModel> NonCyberFieldMonsters() =>
        TryGetOwner() is not { } owner
            ? []
            : MonsterFieldService.GetMonsters(owner)
                .Where(card => !CyberSeries.IsCyberMonster(card))
                .ToList();

    private bool IsBasePlayableWithProjectedMaterials(
        Player owner,
        IReadOnlyCollection<CardModel> materials)
    {
        using var capacity = MonsterFieldService.PushTemporaryCapacityBonus(owner, materials.Count);
        return base.IsPlayable;
    }

    private PileType GetBaseResultPileWithProjectedMaterials(
        Player owner,
        IReadOnlyCollection<CardModel> materials)
    {
        using var capacity = MonsterFieldService.PushTemporaryCapacityBonus(owner, materials.Count);
        return base.GetResultPileTypeForCardPlay();
    }

    internal int GetDamageMaterialCount(int? previewMaterialCount = null) =>
        _damageMaterialCountSnapshot ?? previewMaterialCount ?? NonCyberFieldMonsters().Count;
}
