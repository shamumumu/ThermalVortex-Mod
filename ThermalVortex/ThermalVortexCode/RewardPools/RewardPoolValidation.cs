namespace ThermalVortex.ThermalVortexCode.RewardPools;

public enum RewardPoolBuildMode
{
    Standard,
    Manual
}

public enum RewardPoolValidationCode
{
    None,
    MissingDefinition,
    SerializationError,
    UnsupportedSchemaVersion,
    UnsupportedBuildMode,
    StandardDefinitionContainsCards,
    IncorrectMainCardCount,
    IncorrectExtraCardCount,
    EmptyCardId,
    DuplicateMainCardId,
    DuplicateExtraCardId,
    MissingRequiredMainCard,
    MissingRequiredExtraCard,
    UnknownMainCardId,
    UnknownExtraCardId,
    MainCardInWrongCatalog,
    ExtraCardInWrongCatalog,
    InsufficientCommonCards,
    InsufficientUncommonCards,
    InsufficientRareCards,
    CatalogUnavailable,
    CatalogCountMismatch,
    CatalogDuplicateId,
    CatalogInvariantViolation,
    PendingContextMismatch,
    NoSavedManualDefinition,
    ConfigPersistenceError,
    InvalidConstruction,
    ConstructionBudgetExceeded
}

public readonly record struct RewardPoolValidationResult(
    bool IsValid,
    RewardPoolValidationCode Code,
    string Message)
{
    public static RewardPoolValidationResult Valid { get; } =
        new(true, RewardPoolValidationCode.None, string.Empty);

    public static RewardPoolValidationResult Invalid(
        RewardPoolValidationCode code,
        string message) =>
        new(false, code, message ?? string.Empty);

    public override string ToString() =>
        IsValid ? "Valid" : $"{Code}: {Message}";
}
