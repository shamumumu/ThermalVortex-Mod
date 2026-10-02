using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class DivineArsenalFurnaceGod : XyzMonsterCard
{
    public const int RequiredMaterials = 1;
    public const int BaseMaxHp = 6;
    private const int DamagePerCardPerMaterial = 5;
    private const int UpgradedDamagePerCardPerMaterial = 7;
    private int? _damageHandCountSnapshot;
    public override string CustomPortraitPath => "divine_arsenal_furnace_god.png".BigCardImagePath();
    public override string PortraitPath => "divine_arsenal_furnace_god.png".CardImagePath();
    public override string BetaPortraitPath => "divine_arsenal_furnace_god.png".CardImagePath();
    public override int MinimumMaterials => RequiredMaterials;
    public override int MaximumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => BaseMaxHp;
    protected override bool UsesVariableMaterialCostDisplay => true;

    public DivineArsenalFurnaceGod() : base(CardType.Attack, CardRarity.Basic, TargetType.AllEnemies)
    {
        WithFormulaDamage(
            DamagePerCardPerMaterial,
            UpgradedDamagePerCardPerMaterial,
            (card, _) => ((DivineArsenalFurnaceGod)card).GetDamageMultiplier());
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var materials = Math.Max(MinimumMaterials, Materials);
        if (materials <= 0)
            return;

        var handCards = CardPile.GetCards(Owner, PileType.Hand)
            .Where(card => card != this)
            .ToList();
        _damageHandCountSnapshot = handCards.Count;
        try
        {
            if (IsResolvingAuthorizedExtraDeckSummon)
                await TcgMonsterCutinVfx.PlaySummonAsync(this, play);

            foreach (var card in handCards)
                await CardCmd.Exhaust(ctx, card, false, false);

            if (FormulaDamage.Calculate(null) > 0)
            {
                var enemies = Owner.Creature.CombatState.HittableEnemies.ToList();
                if (enemies.Count > 0)
                    await ThermalVortexCombatVfx.CardAttackAllEnemies(this, FormulaDamage, 1).Execute(ctx);
            }
        }
        finally
        {
            _damageHandCountSnapshot = null;
        }

        EndTurn();
    }

    protected override void OnUpgrade() => ConstructedUpgrade();

    private void EndTurn()
    {
        var manager = CombatManager.Instance;
        if (manager.IsOverOrEnding || !manager.IsPartOfPlayerTurn(Owner))
            return;

        manager.SetReadyToEndTurn(Owner, true, () => Task.CompletedTask);
    }

    private int GetDamageMultiplier()
    {
        var owner = TryGetOwner();
        if (owner is null)
            return 0;

        return GetDamageHandCount() * Math.Max(MinimumMaterials, Materials);
    }

    internal int GetDamageHandCount() =>
        _damageHandCountSnapshot
        ?? (TryGetOwner() is { PlayerCombatState: not null } owner
            ? CardPile.GetCards(owner, PileType.Hand).Count(card => card != this)
            : 0);
}
