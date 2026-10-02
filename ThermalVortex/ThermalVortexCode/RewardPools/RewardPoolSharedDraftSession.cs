using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

[JsonConverter(typeof(JsonStringEnumConverter<RewardPoolSharedDraftPhase>))]
public enum RewardPoolSharedDraftPhase { Packages, Cards, Finalized, Consumed }

public sealed class RewardPoolSharedDraftPlayer
{
    public List<string> PackageIds { get; set; } = [];
    public List<string> MainSingleIds { get; set; } = [];
    public List<string> ExtraIds { get; set; } = [];
    public bool Confirmed { get; set; }
}

public sealed class RewardPoolSharedDraftState
{
    public int SchemaVersion { get; set; } = 1;
    public string SessionId { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public long ContentRevision { get; set; }
    public int Seed { get; set; }
    public int RandomStep { get; set; }
    public RewardPoolSharedDraftPhase Phase { get; set; }
    public List<ulong> ParticipantIds { get; set; } = [];
    public List<string> PackageOfferIds { get; set; } = [];
    public Dictionary<string, int> CommonStock { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<ulong, RewardPoolSharedDraftPlayer> Players { get; set; } = [];
}

/// <summary>
/// Host-owned construction domain. Editing records intentions; conflicting requests never
/// win by arriving first. It uses no run RNG, networking, scene nodes, or local preset state.
/// </summary>
public sealed class RewardPoolSharedDraftSession
{
    public const int PackagesPerPlayer = 3;
    private const int MainTarget = RewardPoolSizePolicy.MultiplayerSelectableMain;
    private static readonly CardRarity[] Rarities = [CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare];
    private readonly Dictionary<string, RewardPoolPackage> _packages;
    private readonly Dictionary<string, CardRarity> _main;
    private readonly HashSet<string> _extra;
    private RewardPoolSharedDraftState _state;

    private RewardPoolSharedDraftSession(RewardPoolSharedDraftState state,
        IEnumerable<RewardPoolPackage> packages, IReadOnlyDictionary<string, CardRarity> main,
        IEnumerable<string> extra)
    {
        _state = state;
        _main = new(main, StringComparer.Ordinal);
        _extra = extra.ToHashSet(StringComparer.Ordinal);
        _packages = packages.Where(p => !p.IsSupport && p.CardIds.Count > 0
                && p.CardIds.All(_main.ContainsKey))
            .ToDictionary(p => p.Id, StringComparer.Ordinal);
    }

    public RewardPoolSharedDraftState State => Clone(_state);
    public static int PackageOfferCount(int players) => (players + 1) * PackagesPerPlayer;
    public static int MinimumSingleStock(int players) => (players + 1) * 20;

    public static bool TryCreate(IReadOnlyList<ulong> players,
        out RewardPoolSharedDraftSession session, out string error)
    {
        try
        {
            return TryCreateForCatalog(players, RewardPoolConstructionCatalog.Packages,
                GameMain(), GameExtra(), RewardPoolConstructionCatalog.PackageVersion,
                RandomNumberGenerator.GetInt32(int.MaxValue), out session, out error);
        }
        catch
        {
            session = null;
            error = "sharedDraftErrorCatalog";
            return false;
        }
    }

    internal static bool TryCreateForCatalog(IReadOnlyList<ulong> players,
        IEnumerable<RewardPoolPackage> packages, IReadOnlyDictionary<string, CardRarity> main,
        IEnumerable<string> extra, string packageVersion, int seed,
        out RewardPoolSharedDraftSession session, out string error)
    {
        session = null;
        error = "sharedDraftErrorParticipants";
        if (players is null || players.Count is < 2 or > 4 || players.Distinct().Count() != players.Count)
            return false;
        var state = new RewardPoolSharedDraftState
        {
            SessionId = Guid.NewGuid().ToString("D"), PackageVersion = packageVersion,
            ContentRevision = 1, Seed = seed, ParticipantIds = players.ToList(),
            Players = players.ToDictionary(id => id, _ => new RewardPoolSharedDraftPlayer())
        };
        var candidate = new RewardPoolSharedDraftSession(state, packages, main, extra);
        if (!candidate.GeneratePackages())
        {
            error = "sharedDraftErrorNoSupply";
            return false;
        }
        session = candidate;
        error = string.Empty;
        return true;
    }

    public static bool TryRestore(RewardPoolSharedDraftState state,
        out RewardPoolSharedDraftSession session, out string error)
    {
        session = null;
        error = "sharedDraftErrorSnapshot";
        try
        {
            if (state is null || state.PackageVersion != RewardPoolConstructionCatalog.PackageVersion)
                return false;
            var restored = new RewardPoolSharedDraftSession(Clone(state), RewardPoolConstructionCatalog.Packages,
                GameMain(), GameExtra());
            if (!restored.ValidStructure())
                return false;
            session = restored;
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
        {
            return false;
        }
    }

    public IReadOnlyList<string> GetSelectedMainIds(ulong actor) => _state.Players.TryGetValue(actor, out var player)
        ? PackageCards(player).Concat(player.MainSingleIds).Distinct(StringComparer.Ordinal).ToArray() : [];

    public bool TrySetPackages(ulong actor, IReadOnlyList<string> ids, out string error)
    {
        error = "sharedDraftErrorPhase";
        if (_state.Phase != RewardPoolSharedDraftPhase.Packages)
            return false;
        error = "sharedDraftErrorInvalidSelection";
        if (!_state.Players.TryGetValue(actor, out var player) || !Unique(ids, PackagesPerPlayer)
            || ids.Any(id => !_state.PackageOfferIds.Contains(id, StringComparer.Ordinal)))
            return false;
        var cards = ids.SelectMany(id => _packages[id].CardIds).ToHashSet(StringComparer.Ordinal);
        if (cards.Count > MainTarget)
        {
            error = "sharedDraftErrorMainCount";
            return false;
        }
        if (!player.PackageIds.SequenceEqual(ids))
        {
            player.PackageIds = ids.ToList();
            Edited();
        }
        error = string.Empty;
        return true;
    }

    public bool TrySetCards(ulong actor, IReadOnlyList<string> mainIds,
        IReadOnlyList<string> extraIds, out string error)
    {
        error = "sharedDraftErrorPhase";
        if (_state.Phase != RewardPoolSharedDraftPhase.Cards)
            return false;
        error = "sharedDraftErrorInvalidSelection";
        if (!_state.Players.TryGetValue(actor, out var player) || !Unique(mainIds, MainTarget)
            || !Unique(extraIds, RewardPoolCatalog.SelectableExtraCardCount)
            || mainIds.Any(id => !_state.CommonStock.ContainsKey(id)) || extraIds.Any(id => !_extra.Contains(id)))
            return false;
        if (PackageCards(player).Concat(mainIds).Distinct(StringComparer.Ordinal).Count() > MainTarget)
        {
            error = "sharedDraftErrorMainCount";
            return false;
        }
        if (!player.MainSingleIds.SequenceEqual(mainIds) || !player.ExtraIds.SequenceEqual(extraIds))
        {
            player.MainSingleIds = mainIds.ToList();
            player.ExtraIds = extraIds.ToList();
            Edited();
        }
        error = string.Empty;
        return true;
    }

    public string GetValidation(ulong actor)
    {
        if (!_state.Players.TryGetValue(actor, out var player))
            return "sharedDraftErrorParticipants";
        if (player.PackageIds.Count != PackagesPerPlayer)
            return "sharedDraftErrorPackageCount";
        if (HasPackageConflict())
            return "sharedDraftErrorPackageConflict";
        var main = GetSelectedMainIds(actor);
        if (_state.Phase == RewardPoolSharedDraftPhase.Packages)
            return CanFill(main.ToHashSet(StringComparer.Ordinal), LooseCards()) ? string.Empty : "sharedDraftErrorNoSupply";
        if (main.Count != MainTarget)
            return "sharedDraftErrorMainCount";
        if (player.ExtraIds.Count != RewardPoolCatalog.SelectableExtraCardCount)
            return "sharedDraftErrorExtraCount";
        if (Rarities.Any(rarity => main.Count(id => _main[id] == rarity) < RewardPoolCatalog.MinimumPerRewardRarity))
            return "sharedDraftErrorRarity";
        if (HasStockConflict())
            return "sharedDraftErrorStockConflict";
        return string.Empty;
    }

    public bool TryConfirm(ulong actor, long revision, out string error)
    {
        error = "sharedDraftErrorPhase";
        if (_state.Phase is not (RewardPoolSharedDraftPhase.Packages or RewardPoolSharedDraftPhase.Cards))
            return false;
        error = "sharedDraftErrorRevision";
        if (revision != _state.ContentRevision)
            return false;
        if (!_state.Players.TryGetValue(actor, out var player))
        {
            error = "sharedDraftErrorParticipants";
            return false;
        }
        error = ValidateAll();
        if (error.Length != 0)
            return false;
        // Acknowledging a revision does not create a new revision.
        player.Confirmed = true;
        if (_state.Players.Values.All(p => p.Confirmed))
        {
            if (_state.Phase == RewardPoolSharedDraftPhase.Packages)
            {
                if (!GenerateStock())
                {
                    foreach (var selection in _state.Players.Values)
                        selection.Confirmed = false;
                    error = "sharedDraftErrorNoSupply";
                    return false;
                }
                _state.Phase = RewardPoolSharedDraftPhase.Cards;
                Edited();
            }
            else
            {
                _state.Phase = RewardPoolSharedDraftPhase.Finalized;
            }
        }
        error = string.Empty;
        return true;
    }

    public bool TryGetDefinitions(out IReadOnlyDictionary<ulong, RewardPoolDefinition> definitions, out string error)
    {
        definitions = null;
        error = "sharedDraftErrorNotFinalized";
        if (_state.Phase != RewardPoolSharedDraftPhase.Finalized || !_state.Players.Values.All(p => p.Confirmed))
            return false;
        error = ValidateAll();
        if (error.Length != 0)
            return false;
        var result = new Dictionary<ulong, RewardPoolDefinition>();
        foreach (var actor in _state.ParticipantIds)
        {
            var metadata = new RewardPoolConstructionMetadata
            {
                Method = RewardPoolConstructionMethod.MultiplayerDraft, DraftSessionId = _state.SessionId
            };
            if (!RewardPoolCatalog.CreateConstructedDefinition(GetSelectedMainIds(actor),
                    _state.Players[actor].ExtraIds, metadata, out var definition, out _))
            {
                error = "sharedDraftErrorInvalidSelection";
                return false;
            }
            result.Add(actor, definition);
        }
        definitions = result;
        error = string.Empty;
        return true;
    }

    public bool TryMarkConsumed(string sessionId, out string error)
    {
        error = "sharedDraftErrorNotFinalized";
        if (sessionId != _state.SessionId || _state.Phase != RewardPoolSharedDraftPhase.Finalized)
            return false;
        _state.Phase = RewardPoolSharedDraftPhase.Consumed;
        error = string.Empty;
        return true;
    }

    private bool GeneratePackages()
    {
        var count = PackageOfferCount(_state.ParticipantIds.Count);
        if (_packages.Count < count || _extra.Count < RewardPoolCatalog.SelectableExtraCardCount)
            return false;
        var rng = NextRandom();
        // A bounded search rejects unsuitable tables before displaying anything. The
        // witness only allocates 3*N packages; the additional three remain available.
        for (var attempt = 0; attempt < 96; attempt++)
        {
            var offers = Shuffled(_packages.Values.ToList(), rng).Take(count).ToArray();
            _state.PackageOfferIds = offers.Select(p => p.Id).ToList();
            var loose = LooseCards();
            if (loose.Count * _state.ParticipantIds.Count >= MinimumSingleStock(_state.ParticipantIds.Count)
                && HasPackageAssignment(offers, loose, _state.ParticipantIds.Count))
                return true;
        }
        return false;
    }

    private bool HasPackageAssignment(RewardPoolPackage[] offers, IReadOnlyList<string> loose, int players)
    {
        var triples = new List<PackageTriple>();
        for (var a = 0; a < offers.Length - 2; a++)
        for (var b = a + 1; b < offers.Length - 1; b++)
        for (var c = b + 1; c < offers.Length; c++)
        {
            var cards = offers[a].CardIds.Concat(offers[b].CardIds).Concat(offers[c].CardIds)
                .ToHashSet(StringComparer.Ordinal);
            if (CanFill(cards, loose))
                triples.Add(new PackageTriple((1UL << a) | (1UL << b) | (1UL << c), cards,
                    new[] { offers[a].Origin, offers[b].Origin, offers[c].Origin }.Distinct().Count()));
        }
        triples = triples.OrderBy(t => t.Origins).ToList();
        var visited = 0;
        return Assign(0, 0UL, new HashSet<string>(StringComparer.Ordinal), players);

        bool Assign(int from, ulong occupied, HashSet<string> cards, int remaining)
        {
            if (remaining == 0)
                return true;
            if (++visited > 100000)
                return false;
            for (var i = from; i < triples.Count; i++)
            {
                var next = triples[i];
                if ((occupied & next.Mask) != 0 || cards.Overlaps(next.Cards))
                    continue;
                var combined = new HashSet<string>(cards, StringComparer.Ordinal);
                combined.UnionWith(next.Cards);
                if (Assign(i + 1, occupied | next.Mask, combined, remaining - 1))
                    return true;
            }
            return false;
        }
    }

    private bool CanFill(HashSet<string> selected, IReadOnlyList<string> loose)
    {
        var slots = MainTarget - selected.Count;
        if (slots < 0 || loose.Count < slots)
            return false;
        var deficit = 0;
        foreach (var rarity in Rarities)
        {
            var needed = Math.Max(0, RewardPoolCatalog.MinimumPerRewardRarity - selected.Count(id => _main[id] == rarity));
            if (loose.Count(id => _main[id] == rarity) < needed)
                return false;
            deficit += needed;
        }
        return deficit <= slots;
    }

    private bool GenerateStock()
    {
        var loose = Shuffled(LooseCards().ToList(), NextRandom());
        var stock = new Dictionary<string, int>(StringComparer.Ordinal);
        // Construct one complete allocation, preferring unused card kinds before
        // copies. Its inventory is a proof that every participant can finish.
        var allocations = _state.ParticipantIds.Select(actor => PackageCards(_state.Players[actor])).ToArray();
        // Reserve everyone's rarity requirements before spending flexible slots;
        // otherwise an earlier player's filler can force unnecessary rare copies.
        foreach (var selected in allocations)
        {
            if (!CanFill(selected, loose))
                return false;
            foreach (var rarity in Rarities)
            {
                while (selected.Count(id => _main[id] == rarity) < RewardPoolCatalog.MinimumPerRewardRarity)
                    if (!Take(rarity, selected)) return false;
            }
        }
        foreach (var selected in allocations)
            while (selected.Count < MainTarget)
                if (!Take(null, selected)) return false;
        var target = Math.Max(stock.Values.Sum(), MinimumSingleStock(_state.ParticipantIds.Count));
        while (stock.Values.Sum() < target)
        {
            var id = loose.Where(id => stock.GetValueOrDefault(id) < _state.ParticipantIds.Count)
                .OrderBy(id => stock.GetValueOrDefault(id)).FirstOrDefault();
            if (id is null)
                return false;
            stock[id] = stock.GetValueOrDefault(id) + 1;
        }
        _state.CommonStock = stock;
        return true;

        bool Take(CardRarity? rarity, HashSet<string> selected)
        {
            var id = loose.Where(id => !selected.Contains(id) && (!rarity.HasValue || _main[id] == rarity))
                .OrderBy(id => stock.GetValueOrDefault(id)).FirstOrDefault();
            if (id is null) return false;
            selected.Add(id);
            stock[id] = stock.GetValueOrDefault(id) + 1;
            return true;
        }
    }

    private bool HasPackageConflict()
    {
        var packages = new HashSet<string>(StringComparer.Ordinal);
        var cards = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in _state.Players.Values)
        {
            if (player.PackageIds.Any(id => !packages.Add(id))) return true;
            var owned = PackageCards(player);
            if (cards.Overlaps(owned)) return true;
            cards.UnionWith(owned);
        }
        return false;
    }

    private bool HasStockConflict() => _state.Players.Values.SelectMany(p => p.MainSingleIds)
        .GroupBy(id => id, StringComparer.Ordinal).Any(group => group.Count() > _state.CommonStock.GetValueOrDefault(group.Key));

    private string ValidateAll()
    {
        foreach (var actor in _state.ParticipantIds)
        {
            var error = GetValidation(actor);
            if (error.Length != 0) return error;
        }
        return string.Empty;
    }

    private bool ValidStructure()
    {
        if (_state.SchemaVersion != 1 || !Guid.TryParse(_state.SessionId, out _) || _state.ContentRevision < 1
            || !Enum.IsDefined(_state.Phase) || _state.ParticipantIds.Count is < 2 or > 4
            || _state.ParticipantIds.Distinct().Count() != _state.ParticipantIds.Count
            || !_state.ParticipantIds.ToHashSet().SetEquals(_state.Players.Keys)
            || !Unique(_state.PackageOfferIds, PackageOfferCount(_state.ParticipantIds.Count))
            || _state.PackageOfferIds.Count != PackageOfferCount(_state.ParticipantIds.Count)
            || _state.PackageOfferIds.Any(id => !_packages.ContainsKey(id))) return false;
        var loose = LooseCards().ToHashSet(StringComparer.Ordinal);
        if (_state.CommonStock.Any(p => !loose.Contains(p.Key) || p.Value < 1 || p.Value > _state.ParticipantIds.Count))
            return false;
        foreach (var player in _state.Players.Values)
        {
            if (player is null || !Unique(player.PackageIds, PackagesPerPlayer)
                || player.PackageIds.Any(id => !_state.PackageOfferIds.Contains(id, StringComparer.Ordinal))
                || !Unique(player.MainSingleIds, MainTarget) || player.MainSingleIds.Any(id => !_state.CommonStock.ContainsKey(id))
                || !Unique(player.ExtraIds, RewardPoolCatalog.SelectableExtraCardCount) || player.ExtraIds.Any(id => !_extra.Contains(id))
                || PackageCards(player).Concat(player.MainSingleIds).Distinct(StringComparer.Ordinal).Count() > MainTarget)
                return false;
        }
        if (_state.Phase == RewardPoolSharedDraftPhase.Packages)
            return _state.CommonStock.Count == 0 && _state.Players.Values.All(p => p.MainSingleIds.Count == 0 && p.ExtraIds.Count == 0);
        if (_state.Players.Values.Any(p => p.PackageIds.Count != PackagesPerPlayer) || HasPackageConflict()) return false;
        return _state.Phase == RewardPoolSharedDraftPhase.Cards
            ? CanCompleteFromStock()
            : _state.Players.Values.All(p => p.Confirmed) && ValidateAll().Length == 0;
    }

    private bool CanCompleteFromStock()
    {
        // Current singles are editable intentions, including temporarily conflicting
        // ones. Only the fixed packages constrain whether a completion exists.
        var packages = _state.ParticipantIds.Select(actor => PackageCards(_state.Players[actor])).ToArray();
        var graph = new List<List<StockFlowEdge>> { new(), new() };
        const int source = 0;
        const int sink = 1;
        var quotaNodes = new int[packages.Length, Rarities.Length];
        var flexibleNodes = new int[packages.Length];
        var slots = new int[packages.Length];
        var requiredFlow = 0;
        for (var player = 0; player < packages.Length; player++)
        {
            slots[player] = MainTarget - packages[player].Count;
            var flexible = slots[player];
            for (var rarity = 0; rarity < Rarities.Length; rarity++)
            {
                var needed = Math.Max(0, RewardPoolCatalog.MinimumPerRewardRarity
                    - packages[player].Count(id => _main[id] == Rarities[rarity]));
                flexible -= needed;
                quotaNodes[player, rarity] = AddNode();
                AddEdge(quotaNodes[player, rarity], sink, needed);
            }
            if (flexible < 0)
                return false;
            flexibleNodes[player] = AddNode();
            AddEdge(flexibleNodes[player], sink, flexible);
            requiredFlow += slots[player];
        }
        if (_state.CommonStock.Values.Sum() < requiredFlow)
            return false;
        foreach (var stock in _state.CommonStock)
        {
            var cardNode = AddNode();
            AddEdge(source, cardNode, stock.Value);
            var rarity = Array.IndexOf(Rarities, _main[stock.Key]);
            for (var player = 0; player < packages.Length; player++)
            {
                if (slots[player] == 0) continue;
                var playerCard = AddNode();
                // One copy per player, even when this ID can fill either quota.
                AddEdge(cardNode, playerCard, 1);
                if (rarity >= 0) AddEdge(playerCard, quotaNodes[player, rarity], 1);
                AddEdge(playerCard, flexibleNodes[player], 1);
            }
        }
        var flow = 0;
        while (flow < requiredFlow)
        {
            var previous = new int[graph.Count];
            Array.Fill(previous, -1);
            var previousEdge = new int[graph.Count];
            var pending = new Queue<int>();
            previous[source] = source;
            pending.Enqueue(source);
            while (pending.Count > 0 && previous[sink] < 0)
            {
                var node = pending.Dequeue();
                for (var index = 0; index < graph[node].Count; index++)
                {
                    var edge = graph[node][index];
                    if (edge.Capacity <= 0 || previous[edge.Target] >= 0) continue;
                    previous[edge.Target] = node;
                    previousEdge[edge.Target] = index;
                    pending.Enqueue(edge.Target);
                }
            }
            if (previous[sink] < 0)
                return false;
            var added = requiredFlow - flow;
            for (var node = sink; node != source; node = previous[node])
                added = Math.Min(added, graph[previous[node]][previousEdge[node]].Capacity);
            for (var node = sink; node != source; node = previous[node])
            {
                var edge = graph[previous[node]][previousEdge[node]];
                edge.Capacity -= added;
                graph[node][edge.Reverse].Capacity += added;
            }
            flow += added;
        }
        // All sink capacities sum to requiredFlow, so full flow satisfies every
        // player's size and rarity quotas while respecting shared copy counts.
        return true;

        int AddNode()
        {
            graph.Add(new());
            return graph.Count - 1;
        }

        void AddEdge(int from, int to, int capacity)
        {
            var forward = new StockFlowEdge(to, graph[to].Count, capacity);
            var reverse = new StockFlowEdge(from, graph[from].Count, 0);
            graph[from].Add(forward);
            graph[to].Add(reverse);
        }
    }

    private HashSet<string> PackageCards(RewardPoolSharedDraftPlayer player) => player.PackageIds
        .SelectMany(id => _packages[id].CardIds).ToHashSet(StringComparer.Ordinal);

    private IReadOnlyList<string> LooseCards()
    {
        var packaged = _state.PackageOfferIds.SelectMany(id => _packages[id].CardIds).ToHashSet(StringComparer.Ordinal);
        return _main.Keys.Where(id => !packaged.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }

    private void Edited()
    {
        _state.ContentRevision++;
        foreach (var player in _state.Players.Values) player.Confirmed = false;
    }

    private Random NextRandom() => new(unchecked(_state.Seed + 104729 * _state.RandomStep++));
    private static List<T> Shuffled<T>(List<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    private static bool Unique(IReadOnlyList<string> ids, int max) => ids is not null && ids.Count <= max
        && ids.All(id => !string.IsNullOrWhiteSpace(id)) && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count;
    private static RewardPoolSharedDraftState Clone(RewardPoolSharedDraftState state) =>
        JsonSerializer.Deserialize<RewardPoolSharedDraftState>(JsonSerializer.Serialize(state));
    private static Dictionary<string, CardRarity> GameMain() => RewardPoolCatalog.GetMainRewardCandidates(true)
        .ToDictionary(RewardPoolCatalog.GetId, card => card.Rarity, StringComparer.Ordinal);
    private static IEnumerable<string> GameExtra() => RewardPoolCatalog.GetSelectableExtraCandidates().Select(RewardPoolCatalog.GetId);
    private sealed record PackageTriple(ulong Mask, HashSet<string> Cards, int Origins);
    private sealed class StockFlowEdge(int target, int reverse, int capacity)
    {
        public int Target { get; } = target;
        public int Reverse { get; } = reverse;
        public int Capacity { get; set; } = capacity;
    }
}
