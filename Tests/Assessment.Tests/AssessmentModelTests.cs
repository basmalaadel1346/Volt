using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Assessment.Tests;

/// <summary>
/// Guards the Database-First contract: the EF model must still build, and the
/// mappings the concurrency and historical-integrity fixes depend on must be
/// the ones the schema declares.
/// </summary>
public class AssessmentModelTests
{
    private static AssessmentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AssessmentDbContext>()
            .UseSqlServer("Server=none;Database=VoltDB;Trusted_Connection=True;")
            .Options;

        return new AssessmentDbContext(options);
    }

    [Fact]
    public void Model_BuildsWithoutErrors()
    {
        using var db = CreateContext();

        // Forces full model construction, including the composite principal-key
        // relationships on QuizAttemptMistake and QuizAttemptQuestion.
        Assert.NotNull(db.Model);
    }

    [Fact]
    public void QuizAttempt_RowVersion_IsAConcurrencyToken()
    {
        using var db = CreateContext();

        var rowVersion = db.Model
            .FindEntityType(typeof(QuizAttempt))!
            .FindProperty(nameof(QuizAttempt.RowVersion))!;

        // This is what makes EF append RowVersion to the UPDATE's WHERE clause,
        // so the losing concurrent submit affects 0 rows and throws.
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
    }

    [Fact]
    public void QuizAttemptQuestion_ClassificationSnapshotIsAlwaysRequired()
    {
        using var db = CreateContext();

        var entity = db.Model.FindEntityType(typeof(QuizAttemptQuestion))!;

        // Topic and difficulty are frozen for every question type.
        Assert.False(entity.FindProperty(nameof(QuizAttemptQuestion.TopicId))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(QuizAttemptQuestion.Difficulty))!.IsNullable);

        // CorrectOptionId became nullable when Essay support landed — an Essay has
        // no answer key. The database still forbids a NULL key on any other type
        // via CK_QuizAttemptQuestions_EssayHasNoKey, which nullability alone
        // cannot express. See QuestionTypeAndLocalizationModelTests.
        Assert.True(entity.FindProperty(nameof(QuizAttemptQuestion.CorrectOptionId))!.IsNullable);
    }

    [Fact]
    public void QuizAttemptQuestion_CorrectOption_UsesTheCompositeQuestionScopedFk()
    {
        using var db = CreateContext();

        var foreignKey = db.Model
            .FindEntityType(typeof(QuizAttemptQuestion))!
            .GetForeignKeys()
            .Single(fk => fk.GetConstraintName() == "FK_QuizAttemptQuestions_QuestionId_CorrectOptionId");

        // (QuestionId, CorrectOptionId) -> QuestionOptions(QuestionId, Id):
        // the answer key cannot point at another question's option.
        Assert.Equal(
            new[] { nameof(QuizAttemptQuestion.QuestionId), nameof(QuizAttemptQuestion.CorrectOptionId) },
            foreignKey.Properties.Select(p => p.Name).ToArray());
        Assert.Equal(
            new[] { nameof(QuestionOption.QuestionId), nameof(QuestionOption.Id) },
            foreignKey.PrincipalKey.Properties.Select(p => p.Name).ToArray());
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }
}

/// <summary>
/// Guards the question-type, media and localization additions: the EF model must
/// still build, and the mappings the new grading and fallback logic depends on
/// must match what the SQL migration declares.
/// </summary>
public class QuestionTypeAndLocalizationModelTests
{
    private static AssessmentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AssessmentDbContext>()
            .UseSqlServer("Server=none;Database=VoltDB;Trusted_Connection=True;")
            .Options;

        return new AssessmentDbContext(options);
    }

    [Fact]
    public void Model_StillBuildsWithTranslationsAndEssayAnswers()
    {
        using var db = CreateContext();
        Assert.NotNull(db.Model);
    }

    [Fact]
    public void QuizAttemptQuestion_CorrectOptionId_IsNullableForEssay()
    {
        using var db = CreateContext();

        var property = db.Model
            .FindEntityType(typeof(QuizAttemptQuestion))!
            .FindProperty(nameof(QuizAttemptQuestion.CorrectOptionId))!;

        // An Essay has no answer key. CK_QuizAttemptQuestions_EssayHasNoKey is what
        // stops a non-Essay question from exploiting the nullability.
        Assert.True(property.IsNullable);
    }

    [Fact]
    public void QuizAttemptQuestion_SnapshotsQuestionType()
    {
        using var db = CreateContext();

        var property = db.Model
            .FindEntityType(typeof(QuizAttemptQuestion))!
            .FindProperty(nameof(QuizAttemptQuestion.QuestionType))!;

        // Type decides how the answer is graded, so it is frozen like Difficulty.
        Assert.False(property.IsNullable);
    }

    [Fact]
    public void QuestionOption_TextIsNullable_ImageIsNullable()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(QuestionOption))!;

        Assert.True(entity.FindProperty(nameof(QuestionOption.OptionText))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(QuestionOption.ImageUrl))!.IsNullable);
    }

    [Fact]
    public void QuestionHint_SequenceUniquenessIsPerLanguage()
    {
        using var db = CreateContext();

        var index = db.Model
            .FindEntityType(typeof(QuestionHint))!
            .GetIndexes()
            .Single(i => i.IsUnique);

        // Without LanguageCode in the key, an Arabic and an English hint could not
        // both be sequence 1 for the same mistake.
        Assert.Equal(
            new[]
            {
                nameof(QuestionHint.QuizAttemptMistakeId),
                nameof(QuestionHint.LanguageCode),
                nameof(QuestionHint.HintSequence)
            },
            index.Properties.Select(p => p.Name).ToArray());
    }

    [Theory]
    [InlineData("QuestionTranslations")]
    [InlineData("QuestionOptionTranslations")]
    [InlineData("QuizTranslations")]
    [InlineData("TopicTranslations")]
    [InlineData("CategoryTranslations")]
    public void EveryTranslationTable_IsUniquePerParentAndLanguage(string tableName)
    {
        using var db = CreateContext();

        var entity = db.Model.GetEntityTypes()
            .Single(e => e.GetTableName() == tableName);

        var unique = entity.GetIndexes().Single(i => i.IsUnique);

        Assert.Contains("LanguageCode", unique.Properties.Select(p => p.Name));
        Assert.Equal(2, unique.Properties.Count);
    }
}
