using System.Net;
using System.Text;
using System.Text.Json;
using AIIntegration;
using Microsoft.Extensions.Options;
using Shared.Assessment.AI;
using Xunit;
using Xunit.Abstractions;

namespace Assessment.Tests;

/// <summary>
/// Captures the ACTUAL wire contract of the current AI integration
/// (HttpExternalAiProvider) without contacting any external service, and pins
/// each documented failure mode to the exception the code really throws.
/// </summary>
public class AiProviderContractTests
{
    private readonly ITestOutputHelper _output;
    public AiProviderContractTests(ITestOutputHelper output) => _output = output;

    /// <summary>Intercepts the outgoing request and returns a canned reply.</summary
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (HttpExternalAiProvider Provider, CapturingHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string endpoint = "https://ai.example.internal/v1/hints")
    {
        var handler = new CapturingHandler(respond);
        var client = new HttpClient(handler);
        var settings = Options.Create(new AiSettings { HintsEndpoint = endpoint });
        return (new HttpExternalAiProvider(client, settings), handler);
    }

    private static GenerateHintsRequest SampleRequest(string language) => new()
    {
        Language = language,
        Questions =
        [
            new GenerateHintQuestion
            {
                QuestionId = 101,
                QuestionText = "ما وحدة قياس المقاومة الكهربية؟",
                WrongOptionText = "الفولت",
                PreviousHints = ["فكّر في العالم الألماني."]
            }
        ]
    };

    // ---------------------------------------------------------------- happy path

    [Fact]
    public async Task ActualRequest_IsPlainCamelCaseJson_WithNoAuthHeader()
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK,
            """{"hints":[{"questionId":101,"hintText":"تلميح"}]}"""));

        await provider.GenerateHintsAsync(SampleRequest("ar"));

        _output.WriteLine("METHOD : " + handler.Request!.Method);
        _output.WriteLine("URL    : " + handler.Request!.RequestUri);
        _output.WriteLine("CTYPE  : " + handler.Request!.Content!.Headers.ContentType);
        _output.WriteLine("AUTH   : " + (handler.Request!.Headers.Authorization?.ToString() ?? "<none>"));
        _output.WriteLine("BODY   : " + handler.Body);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://ai.example.internal/v1/hints", handler.Request!.RequestUri!.ToString());

        // PostAsJsonAsync uses JsonSerializerDefaults.Web → camelCase.
        var body = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal("ar", body.GetProperty("language").GetString());
        Assert.Equal(101, body.GetProperty("questions")[0].GetProperty("questionId").GetInt32());
        Assert.Equal("الفولت", body.GetProperty("questions")[0].GetProperty("wrongOptionText").GetString());

        // No credential of any kind is attached by the current implementation.
        Assert.Null(handler.Request!.Headers.Authorization);
        Assert.DoesNotContain("x-api-key", handler.Request!.Headers.Select(h => h.Key.ToLowerInvariant()));
    }

    [Fact]
    public async Task ActualRequest_CarriesNoUserIdentifierOfAnyKind()
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK, """{"hints":[]}"""));

        await provider.GenerateHintsAsync(SampleRequest("en"));

        // The request object has no field for these, so they cannot leak.
        foreach (var forbidden in new[] { "userId", "attemptId", "email", "token", "password", "jwt" })
            Assert.DoesNotContain(forbidden, handler.Body!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Language_ReachesTheProviderVerbatim(string language)
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK, """{"hints":[]}"""));

        await provider.GenerateHintsAsync(SampleRequest(language));

        Assert.Equal(language,
            JsonDocument.Parse(handler.Body!).RootElement.GetProperty("language").GetString());
    }

    // ---------------------------------------------------------------- failures

    [Fact]
    public async Task UnconfiguredEndpoint_ThrowsInvalidOperation_BeforeAnyHttpCall()
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK, "{}"), endpoint: "");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GenerateHintsAsync(SampleRequest("ar")));

        Assert.Equal("AI hints endpoint is not configured", ex.Message);
        Assert.Null(handler.Request);   // never left the process
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]      // invalid key
    [InlineData(HttpStatusCode.TooManyRequests)]   // rate limited
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task NonSuccessStatus_ThrowsHttpRequestException(HttpStatusCode code)
    {
        var (provider, _) = Build(_ => Json(code, """{"error":"nope"}"""));

        // EnsureSuccessStatusCode() — the code does not distinguish 401 from 429 from 503.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GenerateHintsAsync(SampleRequest("ar")));
    }

    [Fact]
    public async Task MalformedJson_ThrowsJsonException()
    {
        var (provider, _) = Build(_ => Json(HttpStatusCode.OK, "{ this is not json "));

        await Assert.ThrowsAsync<JsonException>(
            () => provider.GenerateHintsAsync(SampleRequest("ar")));
    }

    [Fact]
    public async Task LiteralJsonNullBody_ThrowsInvalidOperation()
    {
        var (provider, _) = Build(_ => Json(HttpStatusCode.OK, "null"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GenerateHintsAsync(SampleRequest("ar")));

        Assert.Equal("The AI provider returned an empty response", ex.Message);
    }

    [Fact]
    public async Task MissingHintsProperty_DoesNotThrow_ItYieldsAnEmptyList()
    {
        // The provider does NOT validate shape. An unrelated JSON object
        // deserializes to Hints = [] and the failure surfaces later, in
        // QuizAttemptService's own validation.
        var (provider, _) = Build(_ => Json(HttpStatusCode.OK, """{"somethingElse":true}"""));

        var response = await provider.GenerateHintsAsync(SampleRequest("ar"));

        Assert.Empty(response.Hints);
    }

    [Fact]
    public async Task NetworkFailure_ThrowsHttpRequestException()
    {
        var handler = new CapturingHandler(_ => throw new HttpRequestException("no route to host"));
        var provider = new HttpExternalAiProvider(
            new HttpClient(handler), Options.Create(new AiSettings { HintsEndpoint = "https://x/y" }));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GenerateHintsAsync(SampleRequest("ar")));
    }

    [Fact]
    public async Task CallerCancellation_ThrowsOperationCanceled()
    {
        var (provider, _) = Build(_ => Json(HttpStatusCode.OK, """{"hints":[]}"""));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GenerateHintsAsync(SampleRequest("ar"), cts.Token));
    }

    // ------------------------------------------------- AiHintGenerator wrapper

    [Fact]
    public async Task Generator_RejectsAnEmptyQuestionList_WithoutCallingTheProvider()
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK, """{"hints":[]}"""));
        var generator = new AiHintGenerator(provider);

        await Assert.ThrowsAsync<ArgumentException>(
            () => generator.GenerateHintsAsync(new GenerateHintsRequest { Language = "ar" }));

        Assert.Null(handler.Request);
    }
}
