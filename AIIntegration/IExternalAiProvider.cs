using Shared.Assessment.AI;

namespace AIIntegration;

internal interface IExternalAiProvider
{
    Task<GenerateHintsResponse> GenerateHintsAsync(
        GenerateHintsRequest request,
        CancellationToken cancellationToken = default);
}
