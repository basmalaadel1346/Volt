using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Shared.Assessment.AI;

namespace AIIntegration;

internal sealed class HttpExternalAiProvider : IExternalAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly AiSettings _settings;

    public HttpExternalAiProvider(HttpClient httpClient, IOptions<AiSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
    }

    public async Task<GenerateHintsResponse> GenerateHintsAsync(
        GenerateHintsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.HintsEndpoint))
            throw new InvalidOperationException("AI hints endpoint is not configured");

        using var response = await _httpClient.PostAsJsonAsync(
            _settings.HintsEndpoint,
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GenerateHintsResponse>(cancellationToken)
            ?? throw new InvalidOperationException("The AI provider returned an empty response");
    }
}
