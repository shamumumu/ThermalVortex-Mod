using System.Text.Json.Serialization;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

[JsonConverter(typeof(JsonStringEnumConverter<RewardPoolConstructionMethod>))]
public enum RewardPoolConstructionMethod
{
    Free,
    Genesis,
    Draft,
    MultiplayerDraft
}

/// <summary>Construction provenance is independent of the runtime reward whitelist mode.</summary>
public sealed record RewardPoolConstructionMetadata
{
    public int RulesVersion { get; init; } = RewardPoolSizePolicy.CurrentRulesVersion;
    public RewardPoolConstructionMethod Method { get; init; }
    public string PricingVersion { get; init; } = string.Empty;
    public int Budget { get; init; }
    public int TotalPoints { get; init; }
    public Dictionary<string, int> CardPoints { get; init; } = new(StringComparer.Ordinal);
    public string DraftSessionId { get; init; } = string.Empty;

    public RewardPoolConstructionMetadata Snapshot() => this with
    {
        CardPoints = new Dictionary<string, int>(CardPoints ?? [], StringComparer.Ordinal)
    };
}

public static class RewardPoolConstruction
{
    public static RewardPoolConstructionMetadata Price(IEnumerable<string> cardIds)
    {
        var prices = cardIds.Distinct(StringComparer.Ordinal).ToDictionary(
            id => id,
            id => RewardPoolConstructionCatalog.TryGetPoints(id, out var points)
                ? points
                : throw new InvalidOperationException($"No construction price for '{id}'."),
            StringComparer.Ordinal);
        return new RewardPoolConstructionMetadata
        {
            Method = RewardPoolConstructionMethod.Genesis,
            PricingVersion = RewardPoolConstructionCatalog.PricingVersion,
            Budget = RewardPoolConstructionCatalog.GenesisBudget,
            TotalPoints = prices.Values.Sum(),
            CardPoints = prices
        };
    }

    /// <summary>Validate saved provenance without repricing an existing run.</summary>
    public static RewardPoolValidationResult ValidateSnapshot(RewardPoolDefinition definition)
    {
        var metadata = definition.Construction;
        if (!Enum.IsDefined(metadata.Method))
            return Invalid("Unknown construction method.");
        if (metadata.RulesVersion != definition.RulesVersion)
            return Invalid("Construction rules do not match the saved definition.");
        if (metadata.Method == RewardPoolConstructionMethod.Free)
            return RewardPoolValidationResult.Valid;
        if (!definition.IsManual)
            return Invalid("Construction metadata requires a constructed reward pool.");
        if (metadata.Method is RewardPoolConstructionMethod.Draft or RewardPoolConstructionMethod.MultiplayerDraft)
            return Guid.TryParse(metadata.DraftSessionId, out _)
                ? RewardPoolValidationResult.Valid
                : Invalid("The draft session ID is invalid.");

        var selected = definition.MainCardIds.Where(id => !RewardPoolCatalog.IsRequiredMainCard(id))
            .Concat(definition.ExtraCardIds.Where(id => !RewardPoolCatalog.IsRequiredExtraCard(id)))
            .ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(metadata.PricingVersion)
            || metadata.Budget <= 0
            || metadata.CardPoints is null
            || !selected.SetEquals(metadata.CardPoints.Keys)
            || metadata.CardPoints.Values.Any(value => value is < 1 or > 10)
            || metadata.TotalPoints != metadata.CardPoints.Values.Sum())
            return Invalid("The saved Genesis price snapshot is invalid.");
        return metadata.TotalPoints <= metadata.Budget
            ? RewardPoolValidationResult.Valid
            : RewardPoolValidationResult.Invalid(RewardPoolValidationCode.ConstructionBudgetExceeded,
                $"Genesis: {metadata.TotalPoints}/{metadata.Budget}.");
    }

    /// <summary>New runs always use current authoritative prices and a live draft session.</summary>
    public static RewardPoolValidationResult ValidateForLaunch(RewardPoolDefinition definition)
    {
        if (definition is null)
            return Invalid("The constructed reward pool is missing.");
        var validation = definition.Validate();
        if (!validation.IsValid || !definition.IsManual)
            return validation;
        if (definition.RulesVersion != RewardPoolSizePolicy.CurrentRulesVersion)
            return Invalid("This build uses earlier construction rules. Reopen it and add cards before starting a new run.");
        if (definition.Construction.Method == RewardPoolConstructionMethod.Genesis)
        {
            try
            {
                var current = Price(definition.MainCardIds.Where(id => !RewardPoolCatalog.IsRequiredMainCard(id))
                    .Concat(definition.ExtraCardIds.Where(id => !RewardPoolCatalog.IsRequiredExtraCard(id))));
                if (current.TotalPoints > current.Budget)
                    return RewardPoolValidationResult.Invalid(RewardPoolValidationCode.ConstructionBudgetExceeded,
                        $"Genesis: {current.TotalPoints}/{current.Budget}.");
                if (definition.Construction.PricingVersion != current.PricingVersion
                    || definition.Construction.Budget != current.Budget
                    || definition.Construction.TotalPoints != current.TotalPoints
                    || current.CardPoints.Any(pair => !definition.Construction.CardPoints.TryGetValue(pair.Key, out var p) || p != pair.Value))
                    return Invalid("Genesis prices changed. Reopen the preset before starting a new run.");
            }
            catch (Exception exception)
            {
                return Invalid(exception.Message);
            }
        }
        else if (definition.Construction.Method == RewardPoolConstructionMethod.Draft)
        {
            if (!RewardPoolDraftService.IsSelected
                || !RewardPoolDraftService.TryGetReadyDefinition(out var ready, out validation))
                return Invalid("Complete and select a new draft before starting this run.");
            if (ready.Construction.DraftSessionId != definition.Construction.DraftSessionId
                || !ready.MainCardIds.SequenceEqual(definition.MainCardIds)
                || !ready.ExtraCardIds.SequenceEqual(definition.ExtraCardIds))
                return Invalid("The pending draft no longer matches the selected session.");
        }
        // Multiplayer session identity and authoritative snapshots are checked by
        // the lobby coordinator. They must never use the local solo draft state.
        return RewardPoolValidationResult.Valid;
    }

    private static RewardPoolValidationResult Invalid(string message) =>
        RewardPoolValidationResult.Invalid(RewardPoolValidationCode.InvalidConstruction, message);
}
