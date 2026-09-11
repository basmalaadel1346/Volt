using AssessmentBL.Services.Constants;
using Xunit;

namespace Assessment.Tests;

public class ContentLanguageTests
{
    [Theory]
    [InlineData("en", "en")]
    [InlineData("ar", "ar")]
    [InlineData("EN", "en")]
    [InlineData("  Ar  ", "ar")]
    [InlineData("ar-EG", "ar")]      // Accept-Language regional subtag
    [InlineData("en-US", "en")]
    public void Normalize_AcceptsSupportedAndRegionalCodes(string input, string expected)
        => Assert.Equal(expected, ContentLanguages.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fr")]               // unsupported
    [InlineData("garbage")]
    public void Normalize_FallsBackToDefaultRatherThanThrowing(string? input)
    {
        // A bad Accept-Language header must never fail a quiz submission.
        Assert.Equal(ContentLanguages.Default, ContentLanguages.Normalize(input));
    }

    [Fact]
    public void EssayIsTheOnlyTypeThatIsNotAutoGraded()
    {
        Assert.False(QuestionTypes.IsAutoGraded(QuestionTypes.Essay));
        Assert.True(QuestionTypes.IsAutoGraded(QuestionTypes.MultipleChoice));
        Assert.True(QuestionTypes.IsAutoGraded(QuestionTypes.TrueFalse));
    }

    [Fact]
    public void TrueFalseUsesTheSameOptionsTableAsMultipleChoice()
    {
        // The whole point of the TrueFalse design: no new storage, no new grading
        // path — it is a MultipleChoice with exactly two options.
        Assert.True(QuestionTypes.UsesOptions(QuestionTypes.TrueFalse));
        Assert.False(QuestionTypes.UsesOptions(QuestionTypes.Essay));
    }
}
