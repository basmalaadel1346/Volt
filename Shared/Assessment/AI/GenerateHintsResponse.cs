namespace Shared.Assessment.AI;

public sealed class GenerateHintsResponse
{
    public IReadOnlyList<GeneratedHint> Hints { get; init; } = [];
}

public sealed class GeneratedHint
{
    public int QuestionId { get; init; }
    public string HintText { get; init; } = string.Empty;
}
