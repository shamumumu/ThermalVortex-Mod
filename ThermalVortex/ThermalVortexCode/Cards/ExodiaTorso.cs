using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using System.Runtime.CompilerServices;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class ExodiaTorso : SealedMillenniumPieceCard
{
    public const int BaseMaxHp = 1;

    public override string CustomPortraitPath => "exodia_torso.png".BigCardImagePath();
    public override string PortraitPath => "exodia_torso.png".CardImagePath();
    public override string BetaPortraitPath => "exodia_torso.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;
    public override MillenniumPieceSummonEffectKind SummonEffectKind => MillenniumPieceSummonEffectKind.Heal;

    public ExodiaTorso() : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-SEALED_MILLENNIUM_PIECE"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_UNIQUE_HAND"),
            NativeKeywordExplanation(CardKeyword.Retain));
    }

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel source)
    {
        var destinationPileType = card?.Pile?.Type;
        if (destinationPileType is not null)
            QueueVictoryCheck(card, destinationPileType.Value);

        return Task.CompletedTask;
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);
        await TryResolveVictory(Owner);
    }

    internal static bool HasWinningSet(Player owner)
    {
        if (owner is null)
            return false;

        return owner.PlayerCombatState is not null
            && CanFillAllPieceSlots(CardPile.GetCards(owner, PileType.Hand), []);
    }

    internal static bool IsVictoryResolving(Player owner) =>
        owner?.Creature?.CombatState is CombatState combatState
        && VictoryStates.TryGetValue(combatState, out var state)
        && Volatile.Read(ref state.Resolving) != 0;

    internal static bool CanFillAllPieceSlots(
        IEnumerable<CardModel> handCards,
        IEnumerable<CardModel> fieldCards)
    {
        var hand = handCards?.Where(card => card is not null).ToList() ?? [];
        var distinctPieces = new[]
        {
            typeof(ExodiaLeftArm),
            typeof(ExodiaRightArm),
            typeof(ExodiaLeftLeg),
            typeof(ExodiaRightLeg),
            typeof(ExodiaTorso)
        }.Count(type => hand.Any(card => MillenniumSeries.IsCardType(card, type)));
        return distinctPieces == 5;
    }

    internal static async Task TryResolveVictoryForPileChange(CardModel card, PileType pileType)
    {
        if (!CanAffectVictory(card, pileType))
            return;

        var owner = card.Owner;
        if (owner?.Creature is null)
            return;

        await TryResolveVictory(owner);
    }

    internal static void QueueVictoryCheck(CardModel card, PileType pileType)
    {
        _ = TryResolveVictoryForPileChange(card, pileType);
    }

    internal static async Task TryResolveVictory(Player owner)
    {
        var manager = CombatManager.Instance;
        var combatState = owner?.Creature?.CombatState;
        if (combatState is null
            || manager is null
            || !manager.IsInProgress
            || manager.IsOverOrEnding
            || !HasWinningSet(owner))
        {
            return;
        }

        if (combatState is not CombatState concreteCombatState)
            return;

        var state = VictoryStates.GetValue(concreteCombatState, _ => new VictoryState());
        if (Interlocked.CompareExchange(ref state.Resolving, 1, 0) != 0)
            return;

        await ThermalVortexCombatVfx.PlaySafeAsync(
            nameof(ExodiaTorsoVfx),
            () => ExodiaTorsoVfx.PlayAsync(owner.Creature));
        if (!ReferenceEquals(owner?.Creature?.CombatState, combatState))
            return;

        await WinCombat(owner);
    }

    private static bool CanAffectVictory(CardModel card, PileType pileType) =>
        pileType == PileType.Hand && MillenniumSeries.IsSealedPiece(card);

    private static async Task WinCombat(Player owner)
    {
        var manager = CombatManager.Instance;
        if (!manager.IsInProgress || manager.IsOverOrEnding)
            return;

        var enemies = owner.Creature.CombatState.Enemies.ToList();
        foreach (var enemy in enemies)
        {
            enemy.RemoveAllPowersInternalExcept(null);
            await CreatureCmd.Kill(enemy, false);
        }

        await manager.CheckWinCondition();
    }

    private static readonly ConditionalWeakTable<CombatState, VictoryState> VictoryStates = new();

    private sealed class VictoryState
    {
        internal int Resolving;
    }
}
