using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;
using ThermalVortex.ThermalVortexCode.RewardPools;

namespace ThermalVortex.ThermalVortexCode.Cards;

// Series membership follows canonical Chinese names containing "千年",
// independent of the current display language.
public interface IMillenniumCard;

public interface IMillenniumMonster;

public interface IMillenniumPower;

public abstract class SealedMillenniumPieceCard(int cost, CardType type, CardRarity rarity, TargetType target) :
    MonsterCard(cost, type, rarity, target),
    IMillenniumCard,
    IMillenniumMonster,
    ICyberCopyableEffectProvider
{
    public override int MaxUpgradeLevel => 0;
    public abstract MillenniumPieceSummonEffectKind SummonEffectKind { get; }

    public IEnumerable<ICyberCopyableEffect> CreateCyberCopyableEffects()
    {
        yield return new MillenniumPieceInheritedEffect(SummonEffectKind);
    }

    // Legacy upgraded pieces use the new single version, including Exhaust.
    protected override void AfterDeserialized()
    {
        base.AfterDeserialized();
        if (CurrentUpgradeLevel > 0)
            CardCmd.Downgrade(this);
        AddKeyword(CardKeyword.Exhaust);
    }

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel source)
    {
        var destinationPileType = card?.Pile?.Type;
        if (destinationPileType is not null)
            ExodiaTorso.QueueVictoryCheck(card, destinationPileType.Value);

        return Task.CompletedTask;
    }
}

internal static class MillenniumSeries
{
    private static readonly Type[] SealedPieceTypes =
    [
        typeof(ExodiaLeftArm),
        typeof(ExodiaRightArm),
        typeof(ExodiaLeftLeg),
        typeof(ExodiaRightLeg),
        typeof(ExodiaTorso)
    ];

    internal static int CountTotal(Player player) =>
        CountMillenniumCardsInHand(player)
        + CountMillenniumMonstersOnField(player)
        + CountMillenniumPowerInstances(player?.Creature);

    // Deck and smithing previews have an owner, but no combat hand pile.
    internal static int CountMillenniumCardsInHand(Player player) =>
        player?.PlayerCombatState is null
            ? 0
            : CardPile.GetCards(player, PileType.Hand).Count(IsMillenniumCard);

    internal static int CountMillenniumMonstersOnField(Player player) =>
        player is null
            ? 0
            : MonsterFieldService.GetMonsters(player).Count(IsMillenniumMonster);

    // Each active Power instance counts once; its Amount does not multiply it.
    internal static int CountMillenniumPowerInstances(MegaCrit.Sts2.Core.Entities.Creatures.Creature owner) =>
        owner?.Powers?.Count(power => power is IMillenniumPower) ?? 0;

    internal static bool IsMillenniumCard(CardModel card) =>
        MonsterIdentity.Matches<IMillenniumCard>(card)
        || IsSealedPieceId(card);

    internal static bool IsMillenniumMonster(CardModel card) =>
        MonsterIdentity.Matches<IMillenniumMonster>(card);

    internal static async Task<CardModel> AddRandomSealedPieceToHand(Player player, AbstractModel source)
    {
        var card = CreateRandomSealedPiece(player);
        if (card is null)
            return null;

        await ThermalVortexCommandCompat.AddGeneratedCardToCombat(card, PileType.Hand, player, CardPilePosition.Top);
        return card;
    }

    internal static CardModel CreateRandomSealedPiece(Player player)
    {
        if (player is null)
            return null;

        var type = SelectRandom(player, SealedPieceTypes);
        return CreateCard(player, type);
    }

    internal static async Task<CardModel> AddChosenSealedPieceToHand(
        PlayerChoiceContext ctx,
        Player player,
        AbstractModel source)
    {
        var isValid = MonsterFieldService.CaptureMaterialUseValidity(player);
        if (!isValid())
            return null;

        var choices = SealedPieceTypes
            .Select(type => CreateCard(player, type))
            .Where(card => card is not null)
            .ToArray();
        if (choices.Length != SealedPieceTypes.Length)
            return null;

        var chosen = await CardSelectionHelper.ChooseOne(
            ctx, player, choices,
            "THERMALVORTEX-MILLENNIUM_PIECE_CHOICE.selectionPrompt",
            cancelable: false,
            sourceCard: source as CardModel);
        if (!isValid()
            || chosen is null
            || !choices.Any(candidate => ReferenceEquals(candidate, chosen))
            || !IsSealedPiece(chosen))
            return null;

        // SimpleGrid combat choices must be mutable cards owned by this combat.
        // The selected display instance is already the exact generated card.
        await ThermalVortexCommandCompat.AddGeneratedCardToCombat(
            chosen,
            PileType.Hand,
            player,
            CardPilePosition.Top);
        return chosen;
    }

    internal static CardModel CreateRandomMainDeckMonster(Player player)
    {
        if (player is null)
            return null;

        var rng = player.RunState?.Rng?.CombatCardGeneration;
        if (rng is null)
            return null;

        var candidates = RewardPoolCatalog
            .GetMainRewardCandidates(RewardPoolCardOrigin.ThermalVortex)
            .OfType<MonsterCard>()
            .ToArray();
        if (candidates.Length == 0)
            return null;

        return player.Creature.CombatState.CreateCard(rng.NextItem(candidates), player);
    }

    private static Type SelectRandom(Player player, IReadOnlyList<Type> types)
    {
        var rng = player?.RunState?.Rng?.CombatCardGeneration;
        if (rng is null || types.Count == 0)
            return null;

        return rng.NextItem(types);
    }

    internal static bool IsSealedPiece(CardModel card) =>
        SealedPieceTypes.Any(type => IsCardType(card, type));

    internal static bool IsCardType(CardModel card, Type type)
    {
        if (card is null || type is null)
            return false;

        if (MonsterIdentity.Matches(card, type))
            return true;

        var expectedId = type == typeof(ExodiaLeftArm) ? ModelDb.Card<ExodiaLeftArm>().Id.Entry
            : type == typeof(ExodiaRightArm) ? ModelDb.Card<ExodiaRightArm>().Id.Entry
            : type == typeof(ExodiaLeftLeg) ? ModelDb.Card<ExodiaLeftLeg>().Id.Entry
            : type == typeof(ExodiaRightLeg) ? ModelDb.Card<ExodiaRightLeg>().Id.Entry
            : type == typeof(ExodiaTorso) ? ModelDb.Card<ExodiaTorso>().Id.Entry
            : null;
        return expectedId is not null && card.Id.Entry == expectedId;
    }

    private static CardModel CreateCard(Player player, Type type) =>
        type switch
        {
            _ when type == typeof(ExodiaLeftArm) => player.Creature.CombatState.CreateCard<ExodiaLeftArm>(player),
            _ when type == typeof(ExodiaRightArm) => player.Creature.CombatState.CreateCard<ExodiaRightArm>(player),
            _ when type == typeof(ExodiaLeftLeg) => player.Creature.CombatState.CreateCard<ExodiaLeftLeg>(player),
            _ when type == typeof(ExodiaRightLeg) => player.Creature.CombatState.CreateCard<ExodiaRightLeg>(player),
            _ when type == typeof(ExodiaTorso) => player.Creature.CombatState.CreateCard<ExodiaTorso>(player),
            _ => null
        };

    private static bool IsSealedPieceId(CardModel card)
    {
        return IsSealedPiece(card);
    }
}
