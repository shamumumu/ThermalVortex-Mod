using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using System.Runtime.CompilerServices;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Commands;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class BrilliantRebootKnight : XyzMonsterCard
{
    public const int RequiredMaterials = 2;
    public const int BaseMaxHp = 1;
    private const int BaseDamage = 20;
    private const int UpgradedDamage = 28;
    public override string CustomPortraitPath => "brilliant_reboot_knight.png".BigCardImagePath();
    public override string PortraitPath => "brilliant_reboot_knight.png".CardImagePath();
    public override string BetaPortraitPath => "brilliant_reboot_knight.png".CardImagePath();
    public override int MinimumMaterials => RequiredMaterials;
    public override int MaximumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => BaseMaxHp;

    public BrilliantRebootKnight() : base(CardType.Attack, CardRarity.Basic, TargetType.AllEnemies)
    {
        WithUpgradeVar("FixedDamage", BaseDamage, UpgradedDamage);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            BlockExplanation());
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || IsRebootConditionMet(Owner));

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        if (!IsRebootConditionMet(Owner))
            return;

        var enemies = Owner.Creature.CombatState.Enemies
            .Where(enemy => enemy?.IsAlive == true)
            .ToList();
        foreach (var enemy in enemies.Where(enemy => enemy.Block > 0))
            await CreatureCmd.LoseBlock(enemy, enemy.Block);

        var damage = UpgradeVarValue("FixedDamage");
        await ThermalVortexCombatVfx.CardAttackAllEnemies(this, damage, 1).Execute(ctx);

        if (Owner.Creature.Block > 0)
            await CreatureCmd.LoseBlock(Owner.Creature, Owner.Creature.Block);

        await ThermalVortexCommandCompat.ApplyPower<NoBlockPower>(
            ctx, Owner.Creature, 1, Owner.Creature, this, false);

        // The card's rule is stronger than the native power and must not depend
        // on whether PowerCmd returns a newly applied or stacked instance.
        BrilliantRebootBlockLock.Register(Owner.Creature);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();

    public static bool IsRebootConditionMet(Player player)
    {
        if (player?.Creature?.CombatState is null)
            return false;

        var enemyTotalHp = player.Creature.CombatState.Enemies
            .Where(enemy => enemy?.IsAlive == true)
            .Sum(enemy => enemy.CurrentHp);
        return enemyTotalHp > player.Creature.CurrentHp;
    }
}

internal static class BrilliantRebootBlockLock
{
    private static readonly ConditionalWeakTable<Creature, LockState> Locks = new();

    internal static void Register(Creature owner)
    {
        var playerCombatState = owner?.Player?.PlayerCombatState;
        var combatState = owner?.CombatState;
        if (playerCombatState is null || combatState is null)
            return;

        Locks.Remove(owner);
        Locks.Add(owner, new LockState(combatState, playerCombatState, playerCombatState.TurnNumber));
    }

    internal static bool IsActive(Creature target)
    {
        if (target?.Player is not { } player
            || !Locks.TryGetValue(target, out var state)
            || CombatManager.Instance is not { IsInProgress: true, IsOverOrEnding: false } manager)
        {
            return false;
        }

        return ReferenceEquals(target.CombatState, state.CombatState)
            && ReferenceEquals(player.PlayerCombatState, state.PlayerCombatState)
            && player.PlayerCombatState?.TurnNumber == state.TurnNumber
            && manager.IsPartOfPlayerTurn(player);
    }

    private sealed record LockState(
        ICombatState CombatState,
        PlayerCombatState PlayerCombatState,
        int TurnNumber);
}
