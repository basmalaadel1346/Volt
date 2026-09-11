using System.Reflection;
using AssessmentBL.DTOs.QuizAttempt;
using AssessmentBL.Services;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Assessment.Tests;

/// <summary>
/// Covers the image-option bug: an image-only answer must reach the AI as its
/// admin-authored description, never as an empty string. The two resolvers are
/// private, so they are reached by reflection — the point is to pin the exact
/// behaviour of the code that runs, not to widen its visibility.
/// </summary>
public class ImageSemanticTextTests
{
    private static string? ResolveQuestion(string? text, string? description) =>
        (string?)typeof(QuizAttemptService)
            .GetMethod("ResolveQuestionSemanticText", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [text, description]);

    private static string? ResolveOption(string? text, string? description) =>
        (string?)typeof(QuizAttemptService)
            .GetMethod("ResolveOptionSemanticText", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [text, description]);

    // ---------------- Case 1: text question + text options (UNCHANGED) -------

    [Fact]
    public void Case1_TextQuestion_TextOption_IsPassedThroughUnchanged()
    {
        Assert.Equal("What is the output of this code?",
            ResolveQuestion("What is the output of this code?", null));
        Assert.Equal("20", ResolveOption("20", null));
    }

    [Fact]
    public void Case1_TextOption_IgnoresADescriptionIfOneSomehowExists()
    {
        // Text is what the child reads, so text wins. This is what keeps the
        // normal flow byte-identical.
        Assert.Equal("20", ResolveOption("20", "a description that must not win"));
    }

    // ---------------- Case 2: image question + text options ------------------

    [Fact]
    public void Case2_ImageQuestion_UsesTheImageDescriptionAsTheQuestion()
    {
        Assert.Equal("A circuit with an LED wired directly across a 9V battery.",
            ResolveQuestion(null, "A circuit with an LED wired directly across a 9V battery."));
    }

    [Fact]
    public void Case2_QuestionWithBothTextAndImage_SendsBoth()
    {
        // The text usually refers to the image ("which circuit below…"), so the
        // description is appended rather than discarded.
        var result = ResolveQuestion("Which circuit is connected correctly?",
                                     "Four circuits labelled A-D; only C has a series resistor.");

        Assert.Equal(
            "Which circuit is connected correctly?\nFour circuits labelled A-D; only C has a series resistor.",
            result);
    }

    // ---------------- Case 3: text question + image options ------------------

    [Fact]
    public void Case3_ImageOption_UsesTheImageDescriptionAsTheStudentAnswer()
    {
        // THE BUG: this previously resolved to "".
        var answer = ResolveOption(null,
            "The LED is connected directly to the battery without a resistor.");

        Assert.Equal("The LED is connected directly to the battery without a resistor.", answer);
        Assert.False(string.IsNullOrWhiteSpace(answer));
    }

    [Fact]
    public void Case4_ImageQuestion_AndImageOption_BothResolve()
    {
        Assert.Equal("Four wiring diagrams of a doorbell.",
            ResolveQuestion("   ", "Four wiring diagrams of a doorbell."));
        Assert.Equal("Bell wired in parallel with the transformer.",
            ResolveOption(null, "Bell wired in parallel with the transformer."));
    }

    // ---------------- Missing descriptions: null, never invented -------------

    [Fact]
    public void ImageOptionWithNoDescription_ResolvesToNull_NotEmptyString()
    {
        // null is the signal to SKIP and log. It must never become "".
        Assert.Null(ResolveOption(null, null));
        Assert.Null(ResolveOption("", "   "));
    }

    [Fact]
    public void QuestionWithNeitherTextNorDescription_ResolvesToNull()
        => Assert.Null(ResolveQuestion("   ", null));

    [Theory]
    [InlineData("  padded text  ", "padded text")]
    [InlineData("\ttabbed\n", "tabbed")]
    public void ResolvedText_IsTrimmed(string input, string expected)
    {
        Assert.Equal(expected, ResolveOption(input, null));
        Assert.Equal(expected, ResolveQuestion(input, null));
    }

    [Fact]
    public void ADescriptionIsAlsoTrimmed()
        => Assert.Equal("desc", ResolveOption(null, "  desc  "));

    // ---------------- The child must never see the description --------------

    [Fact]
    public void ChildFacingOptionDto_HasNoImageDescriptionProperty()
    {
        Assert.Null(typeof(QuizAnswerOptionDto).GetProperty("ImageDescription"));
        Assert.NotNull(typeof(QuizAnswerOptionDto).GetProperty("ImageUrl"));
    }

    [Fact]
    public void ChildFacingQuestionDto_HasNoImageDescriptionProperty()
    {
        Assert.Null(typeof(QuizQuestionForAttemptDto).GetProperty("ImageDescription"));
        Assert.NotNull(typeof(QuizQuestionForAttemptDto).GetProperty("ImageUrl"));
    }

    [Fact]
    public void AdminDtos_DoExposeImageDescription_SoAnAdminCanAuthorIt()
    {
        Assert.NotNull(typeof(AssessmentBL.DTOs.QuestionOption.AdminQuestionOptionResponseDto)
            .GetProperty("ImageDescription"));
        Assert.NotNull(typeof(AssessmentBL.DTOs.QuestionOption.CreateQuestionOptionDto)
            .GetProperty("ImageDescription"));
        Assert.NotNull(typeof(AssessmentBL.DTOs.Question.AdminQuestionResponseDto)
            .GetProperty("ImageDescription"));
    }

    // ---------------- The description reaches the AI-request layer -----------

    [Fact]
    public void ImageDescription_IsMappedAndAvailableToTheAiRequest()
    {
        var options = new DbContextOptionsBuilder<AssessmentDbContext>()
            .UseSqlServer("Server=none;Database=VoltDB;").Options;
        using var db = new AssessmentDbContext(options);

        // If this were unmapped, the AI-request projection could not read it and
        // the "" bug would be unfixable without hardcoding.
        var option = db.Model.FindEntityType(typeof(QuestionOption))!;
        var optionDescription = option.FindProperty(nameof(QuestionOption.ImageDescription));
        Assert.NotNull(optionDescription);
        Assert.True(optionDescription!.IsNullable);
        Assert.Equal(1000, optionDescription.GetMaxLength());

        var question = db.Model.FindEntityType(typeof(Question))!;
        var questionDescription = question.FindProperty(nameof(Question.ImageDescription));
        Assert.NotNull(questionDescription);
        Assert.True(questionDescription!.IsNullable);
    }
}
