using Shared.Assessment.AI;

namespace Assessment.Tests;

/// <summary>
/// Test double for <see cref="IAiHintGenerator"/>. Implements the same contract
/// as the real provider, so the whole submit flow can be exercised — including
/// every AI failure mode — without a network call or an API key.
/// </summary>
public sealed class FakeAiHintGenerator : IAiHintGenerator
{
    private readonly Func<GenerateHintsRequest, GenerateHintsResponse> _behaviour;

    /// <summary>Every request this fake was given, for assertion.</summary>
    public List<GenerateHintsRequest> Received { get; } = [];

    private FakeAiHintGenerator(Func<GenerateHintsRequest, GenerateHintsResponse> behaviour)
        => _behaviour = behaviour;

    /// <summary>One well-formed hint per requested question, tagged with the language.</summary>
    public static FakeAiHintGenerator Succeeding() => new(request => new GenerateHintsResponse
    {
        Hints = request.Questions
            .Select(q => new GeneratedHint
            {
                QuestionId = q.QuestionId,
                HintText = request.Language == "ar"
                    ? $"تلميح للسؤال {q.QuestionId}"
                    : $"Hint for question {q.QuestionId}"
            })
            .ToList()
    });

    /// <summary>Provider down, unreachable, or rate limited.</summary>
    public static FakeAiHintGenerator Unavailable() =>
        new(_ => throw new HttpRequestException("simulated AI outage"));

    /// <summary>Endpoint not configured — the current production default.</summary>
    public static FakeAiHintGenerator NotConfigured() =>
        new(_ => throw new InvalidOperationException("AI hints endpoint is not configured"));

    /// <summary>Timed out.</summary>
    public static FakeAiHintGenerator TimingOut() =>
        new(_ => throw new TaskCanceledException("simulated AI timeout"));

    /// <summary>Returns nothing, which fails the service's one-hint-per-question check.</summary>
    public static FakeAiHintGenerator ReturningNothing() =>
        new(_ => new GenerateHintsResponse { Hints = [] });

    /// <summary>Returns a blank hint, which the service also rejects.</summary>
    public static FakeAiHintGenerator ReturningBlankHints() =>
        new(request => new GenerateHintsResponse
        {
            Hints = request.Questions
                .Select(q => new GeneratedHint { QuestionId = q.QuestionId, HintText = "   " })
                .ToList()
        });

    /// <summary>Answers a question that was never asked.</summary>
    public static FakeAiHintGenerator ReturningUnrelatedQuestionIds() =>
        new(_ => new GenerateHintsResponse
        {
            Hints = [new GeneratedHint { QuestionId = -999, HintText = "unrelated" }]
        });

    public Task<GenerateHintsResponse> GenerateHintsAsync(
        GenerateHintsRequest request, CancellationToken cancellationToken = default)
    {
        Received.Add(request);
        return Task.FromResult(_behaviour(request));
    }
}
