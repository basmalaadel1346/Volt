using Shared.Assessment.AI;
using Xunit;

namespace Assessment.Tests;

public class FakeAiHintGeneratorTests
{
    private static GenerateHintsRequest Request(string language) => new()
    {
        Language = language,
        Questions = [new GenerateHintQuestion { QuestionId = 101, QuestionText = "q", WrongOptionText = "w" }]
    };

    [Theory]
    [InlineData("ar", "تلميح للسؤال 101")]
    [InlineData("en", "Hint for question 101")]
    public async Task Succeeding_HonoursTheRequestedLanguage(string language, string expected)
    {
        var fake = FakeAiHintGenerator.Succeeding();

        var response = await fake.GenerateHintsAsync(Request(language));

        Assert.Equal(expected, Assert.Single(response.Hints).HintText);
        Assert.Equal(language, Assert.Single(fake.Received).Language);
    }

    [Fact]
    public async Task Unavailable_ReproducesTheOutageException()
        => await Assert.ThrowsAsync<HttpRequestException>(
            () => FakeAiHintGenerator.Unavailable().GenerateHintsAsync(Request("ar")));

    [Fact]
    public async Task NotConfigured_ReproducesTheCurrentProductionDefault()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => FakeAiHintGenerator.NotConfigured().GenerateHintsAsync(Request("ar")));

        Assert.Equal("AI hints endpoint is not configured", ex.Message);
    }

    [Fact]
    public async Task TimingOut_ReproducesTheTimeoutException()
        => await Assert.ThrowsAsync<TaskCanceledException>(
            () => FakeAiHintGenerator.TimingOut().GenerateHintsAsync(Request("ar")));

    [Fact]
    public async Task DegenerateResponses_AreDeliveredAsIs_ForTheServiceToReject()
    {
        Assert.Empty((await FakeAiHintGenerator.ReturningNothing().GenerateHintsAsync(Request("ar"))).Hints);

        Assert.True(string.IsNullOrWhiteSpace(
            Assert.Single((await FakeAiHintGenerator.ReturningBlankHints().GenerateHintsAsync(Request("ar"))).Hints).HintText));

        Assert.Equal(-999,
            Assert.Single((await FakeAiHintGenerator.ReturningUnrelatedQuestionIds().GenerateHintsAsync(Request("ar"))).Hints).QuestionId);
    }
}
