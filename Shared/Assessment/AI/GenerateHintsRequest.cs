namespace Shared.Assessment.AI;

public sealed class GenerateHintsRequest
{
    /// <summary>
    /// The language the hints must be written in ("en" | "ar"). The provider is
    /// responsible for producing text in this language — hints are generated per
    /// language, never translated after the fact.
    /// </summary>
    public string Language { get; init; } = "ar";

    public IReadOnlyList<GenerateHintQuestion> Questions { get; init; } = [];
}

public sealed class GenerateHintQuestion
{
    public int QuestionId { get; init; }
    public string QuestionText { get; init; } = string.Empty;
    public string WrongOptionText { get; init; } = string.Empty;

    /// <summary>Earlier hints for this question, in the same language, oldest first.</summary>
    public IReadOnlyList<string> PreviousHints { get; init; } = [];
}
