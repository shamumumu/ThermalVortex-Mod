using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.MonsterField;

// Owns the temporary copy until one damage segment consumes or releases it.
// Actual removal and re-entry are observed separately from requested leaving.
internal sealed class PhoenixRevivalSnapshot : IDisposable
{
    private static readonly HashSet<PhoenixRevivalSnapshot> Live = [];
    private readonly Func<bool> _sameCombat;
    private readonly ChaosPhantom.RevivalCopyState _copy;
    private bool _departed;
    private bool _invalidated;
    private bool _restoring;
    private bool _entered;
    private bool _disposed;

    internal CardModel Card { get; }
    internal Player Player { get; }

    private PhoenixRevivalSnapshot(CardModel card, ChaosPhantom.RevivalCopyState copy)
    {
        Card = card;
        Player = card.Owner;
        _copy = copy;
        var creature = Player.Creature;
        var combat = creature.CombatState;
        var playerCombat = Player.PlayerCombatState;
        var manager = CombatManager.Instance;
        // The player can be at zero HP here; that is the reason to revive.
        _sameCombat = () => combat is not null && playerCombat is not null
            && ReferenceEquals(Card.Owner, Player)
            && ReferenceEquals(Player.Creature, creature)
            && ReferenceEquals(creature.CombatState, combat)
            && ReferenceEquals(Player.PlayerCombatState, playerCombat)
            && ReferenceEquals(CombatManager.Instance, manager)
            && manager is { IsInProgress: true, IsOverOrEnding: false };
        Live.Add(this);
        MonsterFieldEventService.MonsterEnteredVisual += OnMonsterEntered;
    }

    internal static PhoenixRevivalSnapshot Capture(CardModel card)
    {
        if (!MonsterFieldService.IsOnField(card) || card.Owner?.PlayerCombatState is null)
            return null;
        if (card is WingedDragonOfRaPhoenix)
            return new PhoenixRevivalSnapshot(card, null);
        if (card is not ChaosPhantom phantom)
            return null;
        var copy = phantom.CaptureRevivalCopy();
        return copy is null ? null : new PhoenixRevivalSnapshot(card, copy);
    }

    internal bool CanRevive => !_disposed && !_invalidated && _departed
        && !MonsterFieldService.IsOnField(Card) && _sameCombat()
        && (_copy is null || _copy.CanRestore);

    internal void ConfirmDeparture() => _departed = true;

    private void OnMonsterEntered(MonsterFieldEnterEvent entry)
    {
        if (!ReferenceEquals(entry.Card, Card))
            return;
        if (_restoring)
        {
            if (_entered)
                _invalidated = true;
            _entered = true;
        }
        else
            _invalidated = true;
    }

    internal async Task<bool> RestoreMonster(PlayerChoiceContext ctx)
    {
        if (!CanRevive)
            return false;

        _restoring = true;
        try
        {
            if (_copy is not null && !_copy.TryRestore())
                return false;
            await MonsterFieldService.RestoreDestroyedMonster(ctx, Card, Card);
            // Entry is the commit point even if an entry effect removes it.
            if (_entered && !_disposed && !_invalidated && _sameCombat()
                && (_copy is null || _copy.IsRestoredCopyCurrent)
                && MonsterFieldService.IsOnField(Card))
            {
                var maximum = MonsterFieldHealthService.GetInitialMaxHp(Card);
                MonsterFieldHealthService.SetHealth(Card, maximum, maximum);
            }
            return _entered;
        }
        finally
        {
            _restoring = false;
            if (!_entered)
                _copy?.RollbackUnenteredCopy();
        }
    }

    internal static void ResetAll()
    {
        foreach (var snapshot in Live.ToArray())
            snapshot.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Live.Remove(this);
        MonsterFieldEventService.MonsterEnteredVisual -= OnMonsterEntered;
        _copy?.Dispose();
    }
}
