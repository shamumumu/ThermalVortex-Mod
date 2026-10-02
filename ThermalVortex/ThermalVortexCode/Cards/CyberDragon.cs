using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberDragon : MonsterCard, ICyberMonster, ICyberDevourStats
{
    private const int NormalCost = 2;
    public const int BaseDamage = 5;
    public const int UpgradedDamage = 8;
    public const int BaseMaxHp = 5;
    private bool _displayedIntrinsicFreeCost;
    private bool _freeForCombat;

    public override string CustomPortraitPath => "cyber_dragon.png".BigCardImagePath();
    public override string PortraitPath => "cyber_dragon.png".CardImagePath();
    public override string BetaPortraitPath => "cyber_dragon.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;
    public int CyberAttackContribution => CurrentUpgradeLevel > 0 ? UpgradedDamage : BaseDamage;

    protected override bool IsPlayable
    {
        get
        {
            RefreshSpecialSummonCost();
            return base.IsPlayable;
        }
    }

    public CyberDragon() : base(NormalCost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
        WithDamage(BaseDamage, UpgradedDamage - BaseDamage);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    internal static bool CanSpecialSummonForFree(CardModel card)
    {
        try
        {
            return card is CyberDragon
                && card.Pile?.Type == PileType.Hand
                && card.Owner is not null
                && !MonsterFieldService.HasFieldOrPendingMonsterForPreview<CyberDragon>(card.Owner);
        }
        catch
        {
            return false;
        }
    }

    internal static void RefreshSpecialSummonCosts(Player player)
    {
        if (player?.PlayerCombatState is null)
            return;

        foreach (var cyberDragon in CardPile.GetCards(player, PileType.Hand).OfType<CyberDragon>())
            cyberDragon.RefreshSpecialSummonCost();
    }

    internal void RefreshSpecialSummonCost()
    {
        var free = HasIntrinsicFreeCost;
        if (_displayedIntrinsicFreeCost == free)
            return;

        // The rules power supplies this conditional modifier at calculation
        // time. Refreshing the UI must not overwrite another card's modifier.
        _displayedIntrinsicFreeCost = free;
        InvokeEnergyCostChanged();
    }

    internal bool HasIntrinsicFreeCost => _freeForCombat || CanSpecialSummonForFree(this);

    internal void SetFreeForCombat()
    {
        _freeForCombat = true;
        RefreshSpecialSummonCost();
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        RefreshSpecialSummonCost();
        await ResolveMonsterSummon(play);
        if (play.Target is not null)
        {
            var damage = DynamicVars.Damage.IntValue + CyberDevourState.GetInheritedAttackContribution(this);
            await ThermalVortexCombatVfx.CardAttack(this, play.Target, damage, 1)
                .WithHitFx(null, null, null)
                .WithHitVfxNode(CyberDragonHitVfx.Create)
                .Execute(ctx);
        }
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
