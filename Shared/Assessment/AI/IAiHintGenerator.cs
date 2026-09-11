namespace Shared.Assessment.AI;

public interface IAiHintGenerator
{
    Task<GenerateHintsResponse> GenerateHintsAsync(
        GenerateHintsRequest request,
        CancellationToken cancellationToken = default);
}
