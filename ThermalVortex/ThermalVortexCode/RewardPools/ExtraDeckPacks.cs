using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

internal enum ExtraDeckPackSource
{
    Combat,
    Merchant,
    Pray
}

internal static class ExtraDeckPacks
{
    private const int ExtraDeckRewardOptionCount = 3;
    private const int NormalCombatRewardPercent = 20;
    private const int CombatDropRollSalt = 3;

    internal static bool ShouldOfferCombatPack(Player player, CombatRoom room) => room.RoomType switch
    {
        RoomType.Elite or RoomType.Boss => true,
        RoomType.Monster => CreateRng(player, ExtraDeckPackSource.Combat, CombatDropRollSalt).NextInt(100) < NormalCombatRewardPercent,
        _ => false
    };

    internal static IReadOnlyList<CardModel> Create(
        Player player, AbstractRoom room, ExtraDeckPackSource source, int packIndex = 0)
    {
        if (room is null || player?.RunState is not RunState runState
            || player.GetRelic<ThermalVortexCore>() is not { } core)
        {
            return [];
        }

        var candidates = RewardPoolCatalog.GetExtraCandidates().Where(core.IsExtraDeckRewardAllowed).ToList();
        if (candidates.Count < ExtraDeckRewardOptionCount)
            return [];

        var rng = CreateRng(player, source, packIndex);
        var cards = new CardModel[ExtraDeckRewardOptionCount];
        for (var i = 0; i < cards.Length; i++)
        {
            var index = rng.NextInt(candidates.Count);
            cards[i] = runState.CreateCard(candidates[index], player);
            candidates.RemoveAt(index);
        }

        return cards;
    }

    private static Rng CreateRng(Player player, ExtraDeckPackSource source, int packIndex)
    {
        var runState = player.RunState;
        var roomOrdinal = runState.CurrentMapPointHistoryEntry?.Rooms.Count ?? runState.CurrentRoomCount;
        // Room IDs are reassigned when a save resumes. The map history preserves the room's ordinal.
        var salt = ((ulong)(uint)runState.TotalFloor << 32)
            | ((ulong)(uint)roomOrdinal << 8)
            | ((ulong)source << 4)
            | (uint)packIndex;
        return new Rng(player, ModelDb.Relic<ThermalVortexCore>().Id, salt);
    }

    internal static async Task<CardModel> Choose(Player player, IReadOnlyList<CardModel> cards)
    {
        if (cards.Count != ExtraDeckRewardOptionCount)
            return null;

        var manager = RunManager.Instance;
        var synchronizer = manager.PlayerChoiceSynchronizer;
        var results = cards.Select(card => new CardCreationResult(card)).ToArray();
        CardRewardAlternative[] alternatives =
        [
            new("Skip", PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward)
        ];
        var choiceId = synchronizer.ReserveChoiceId(player);
        int? index;
        if (LocalContext.IsMe(player))
        {
            if (manager.CardSelectorStack.GetSelector(player) is { } selector)
            {
                var selection = selector.GetSelectedCardReward(results, alternatives);
                var match = Array.FindIndex(results, result => ReferenceEquals(result.Card, selection.card));
                index = match < 0 ? null : match;
            }
            else
            {
                var screen = NCardRewardSelectionScreen.ShowScreen(results, alternatives);
                try
                {
                    index = await screen.OptionSelected();
                }
                finally
                {
                    NOverlayStack.Instance?.Remove(screen);
                }
            }

            synchronizer.SyncLocalChoice(player, choiceId, PlayerChoiceResult.FromIndex(index));
        }
        else
        {
            index = (await synchronizer.WaitForRemoteChoice(player, choiceId)).AsIndexOrNull();
        }

        return index is int selected && selected >= 0 && selected < cards.Count ? cards[selected] : null;
    }
}

internal sealed class ExtraDeckCombatPackReward(
    Player player, AbstractRoom room, IReadOnlyList<CardModel> cards)
    : CardReward(cards, CardCreationSource.Encounter, player, CardCreationOptions.ForRoom(player, room.RoomType))
{
    protected override async Task<bool> OnSelect()
    {
        var card = await ExtraDeckPacks.Choose(Player, cards);
        if (card is null || Player.GetRelic<ThermalVortexCore>()?.TryGrantOwnedExtraDeckCard(card) != true)
            return false;

        RecordChoice(card);
        return true;
    }

    public override void OnSkipped() => RecordChoice(null);

    private void RecordChoice(CardModel picked)
    {
        var history = Player.RunState.CurrentMapPointHistoryEntry?.GetEntry(Player.NetId)?.CardChoices;
        if (history is null)
            return;

        foreach (var card in cards)
            history.Add(new CardChoiceHistoryEntry(card, ReferenceEquals(card, picked)));
    }
}
