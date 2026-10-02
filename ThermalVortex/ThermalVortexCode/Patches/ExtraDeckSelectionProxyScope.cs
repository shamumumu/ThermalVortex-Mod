using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Patches;

// Proxies are deck-index network operands until the complete native selection
// task has finished. A screen exit is deliberately not a lifetime boundary.
internal sealed class ExtraDeckSelectionProxyScope : IDisposable
{
    private static readonly Dictionary<Player, ProxySession> Sessions = new(new ReferenceComparer<Player>());
    private static readonly ConditionalWeakTable<CardModel, ProxyRegistration> Registrations = new();
    private static long _generation;

    private readonly ProxySession _session;
    private readonly SelectionPurpose _purpose;
    private bool _disposed;

    private enum SelectionPurpose { Availability, Upgrade, Enchantment, Removal }

    private ExtraDeckSelectionProxyScope(ProxySession session, SelectionPurpose purpose)
    {
        _session = session;
        _purpose = purpose;
        session.Leases++;
        if (purpose == SelectionPurpose.Removal)
            session.RemovalLeases++;
    }

    internal static ExtraDeckSelectionProxyScope BeginUpgrade(Player player) =>
        Begin(player, SelectionPurpose.Upgrade);

    internal static ExtraDeckSelectionProxyScope BeginAvailability(Player player) =>
        Begin(player, SelectionPurpose.Availability);

    internal static ExtraDeckSelectionProxyScope BeginRemoval(Player player) =>
        Begin(player, SelectionPurpose.Removal);

    internal static ExtraDeckSelectionProxyScope BeginEnchantment(
        Player player,
        EnchantmentModel enchantment) =>
        enchantment is null
            ? null
            : Begin(player, SelectionPurpose.Enchantment);

    private static ExtraDeckSelectionProxyScope Begin(
        Player player,
        SelectionPurpose purpose)
    {
        if (player?.RunState is not RunState run || player.GetRelic<ThermalVortexCore>() is not { } core)
            return null;

        // Eligibility is evaluated exactly once by the native Player command,
        // including any caller-supplied additional filter.
        var cards = core.CreateSelectionProxyCards(run);
        if (cards.Count == 0)
            return null;

        if (!Sessions.TryGetValue(player, out var session)
            || !ReferenceEquals(session.Core, core)
            || !ReferenceEquals(session.Run, run))
        {
            session?.Detach();
            session = new ProxySession(player, core, run, _generation);
            Sessions[player] = session;
        }

        var scope = new ExtraDeckSelectionProxyScope(session, purpose);
        try
        {
            foreach (var candidate in cards.OfType<XyzMonsterCard>())
            {
                var index = candidate.ExtraDeckUpgradeIndex;
                var identity = core.GetOwnedExtraDeckEntryIdentity(index);
                if (identity is null)
                {
                    candidate.RemoveFromState();
                    continue;
                }

                // Reentrant selections for one player share a single original
                // for each owned entry, keeping every outstanding deck index stable.
                if (session.Cards.ContainsKey(identity))
                {
                    candidate.RemoveFromState();
                    continue;
                }

                candidate.ExtraDeckUpgradeIndex = index;
                candidate.ExtraDeckEnchantmentIndex = index;
                candidate.IsExtraDeckUpgradeProxy = true;
                candidate.IsExtraDeckEnchantmentProxy = true;
                session.Cards.Add(identity, candidate);
                Registrations.Add(candidate, new ProxyRegistration(session, identity, candidate.CurrentUpgradeLevel));
                if (!player.Deck.Cards.Any(card => ReferenceEquals(card, candidate)))
                    player.Deck.AddInternal(candidate, player.Deck.Cards.Count, false);
            }
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    internal async Task<IEnumerable<CardModel>> AwaitSelection(Task<IEnumerable<CardModel>> nativeTask)
    {
        try
        {
            if (nativeTask is null)
                return [];

            var selected = (await nativeTask)?.ToList() ?? [];
            if (_session.IsCurrent)
            {
                foreach (var card in selected)
                {
                    if (!Registrations.TryGetValue(card, out var registration)
                        || !ReferenceEquals(registration.Session, _session))
                        continue;
                    if (_purpose == SelectionPurpose.Upgrade)
                        registration.SelectedForUpgrade = true;
                    else if (_purpose == SelectionPurpose.Enchantment)
                        registration.SelectedForEnchantment = true;
                    else if (_purpose == SelectionPurpose.Removal)
                        registration.SelectedForRemoval = true;
                }
            }
            return selected;
        }
        finally
        {
            Dispose();
        }
    }

    internal static bool TryGetUpgradeTarget(CardModel card, out ThermalVortexCore core, out int index)
    {
        if (TryGetRegistration(card, out var registration)
            && registration.SelectedForUpgrade
            && card.CurrentUpgradeLevel > registration.OriginalUpgradeLevel)
        {
            core = registration.Session.Core;
            index = registration.OwnedIndex;
            return index >= 0;
        }
        core = null;
        index = -1;
        return false;
    }

    internal static bool TryGetEnchantmentTarget(CardModel card, out ThermalVortexCore core, out int index)
    {
        if (TryGetRegistration(card, out var registration) && registration.SelectedForEnchantment)
        {
            core = registration.Session.Core;
            index = registration.OwnedIndex;
            return index >= 0;
        }
        core = null;
        index = -1;
        return false;
    }

    internal static bool IsRemovalProxy(CardModel card) =>
        TryGetRegistration(card, out var registration) && registration.SelectedForRemoval;

    internal static bool IsRemovalChoice(CardModel card) =>
        TryGetRegistration(card, out var registration)
        && registration.Session.IsCurrent
        && registration.Session.RemovalLeases > 0;

    internal static bool TryGetRemovalTarget(CardModel card, out ThermalVortexCore core, out object identity)
    {
        if (TryGetRegistration(card, out var registration)
            && registration.SelectedForRemoval
            && registration.OwnedIndex >= 0)
        {
            core = registration.Session.Core;
            identity = registration.OwnedIdentity;
            return true;
        }
        core = null;
        identity = null;
        return false;
    }

    private static bool TryGetRegistration(CardModel card, out ProxyRegistration registration)
    {
        registration = null;
        return card is not null
            && Registrations.TryGetValue(card, out registration)
            && registration.Session.Generation == _generation
            && ReferenceEquals(card.Owner, registration.Session.Player)
            && ReferenceEquals(card.Owner.RunState, registration.Session.Run)
            && ReferenceEquals(card.Owner.GetRelic<ThermalVortexCore>(), registration.Session.Core);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _session.Leases--;
        if (_purpose == SelectionPurpose.Removal)
            _session.RemovalLeases--;
        if (_session.Leases != 0)
            return;

        _session.Detach();
        if (Sessions.TryGetValue(_session.Player, out var active) && ReferenceEquals(active, _session))
            Sessions.Remove(_session.Player);
    }

    internal static void CleanupAll()
    {
        ExtraDeckRemovalApplication.CleanupAll();
        _generation++;
        foreach (var session in Sessions.Values.ToList())
            session.Detach();
        Sessions.Clear();
        Registrations.Clear();
    }

    private sealed class ProxySession(Player player, ThermalVortexCore core, RunState run, long generation)
    {
        internal Player Player { get; } = player;
        internal ThermalVortexCore Core { get; } = core;
        internal RunState Run { get; } = run;
        internal long Generation { get; } = generation;
        internal Dictionary<object, XyzMonsterCard> Cards { get; } = new(new ReferenceComparer<object>());
        internal int Leases { get; set; }
        internal int RemovalLeases { get; set; }
        private bool _detached;
        internal bool IsCurrent => !_detached && Generation == _generation;

        internal void Detach()
        {
            if (_detached)
                return;
            _detached = true;
            foreach (var card in Cards.Values)
            {
                try
                {
                    if (Player.Deck.Cards.Any(candidate => ReferenceEquals(candidate, card)))
                        Player.Deck.RemoveInternal(card, false);
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Info($"ExtraDeck selection proxy cleanup failed card={card.GetType().Name} error={ex}");
                }
            }
        }
    }

    private sealed class ProxyRegistration(ProxySession session, object ownedIdentity, int originalUpgradeLevel)
    {
        internal ProxySession Session { get; } = session;
        internal object OwnedIdentity { get; } = ownedIdentity;
        internal int OwnedIndex => Session.Core.GetOwnedExtraDeckEntryIndex(OwnedIdentity);
        internal int OriginalUpgradeLevel { get; } = originalUpgradeLevel;
        internal bool SelectedForUpgrade { get; set; }
        internal bool SelectedForEnchantment { get; set; }
        internal bool SelectedForRemoval { get; set; }
    }
}
