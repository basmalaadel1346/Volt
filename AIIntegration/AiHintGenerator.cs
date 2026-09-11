using Shared.Assessment.AI;

namespace AIIntegration;

internal sealed class AiHintGenerator : IAiHintGenerator
{
    private readonly IExternalAiProvider _provider;

    public AiHintGenerator(IExternalAiProvider provider) => _provider = provider;

    public Task<GenerateHintsResponse> GenerateHintsAsync(
        GenerateHintsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Questions.Count == 0)
            throw new ArgumentException("At least one question is required", nameof(request));

        return _provider.GenerateHintsAsync(request, cancellationToken);
    }
}
