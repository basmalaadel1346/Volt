using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssessmentDA.Configurations;

public class QuizConfiguration : IEntityTypeConfiguration<Quiz>
{
    public void Configure(EntityTypeBuilder<Quiz> entity)
    {
        entity.ToTable("Quizzes", "Assessment", tb =>
        {
            tb.HasCheckConstraint("CK_Quizzes_QuizType",
                "[QuizType] IN ('LevelAssessment', 'LessonQuiz', 'LessonReview', 'Standalone')");
            tb.HasCheckConstraint("CK_Quizzes_TypeMatchesReference",
                "(([QuizType]='LevelAssessment' AND [LevelId] IS NOT NULL AND [LessonId] IS NULL) " +
                "OR ([QuizType] IN ('LessonQuiz','LessonReview') AND [LessonId] IS NOT NULL AND [LevelId] IS NULL) " +
                "OR ([QuizType]='Standalone' AND [LevelId] IS NULL AND [LessonId] IS NULL))");
        });

        entity.HasIndex(e => e.IsActive, "IX_Quizzes_IsActive");
        entity.HasIndex(e => e.LessonId, "IX_Quizzes_LessonId");
        entity.HasIndex(e => e.LevelId, "IX_Quizzes_LevelId");
        entity.HasIndex(e => e.QuizType, "IX_Quizzes_QuizType");

        entity.Property(e => e.Title).HasMaxLength(300);
        entity.Property(e => e.QuizType)
            .HasMaxLength(30)
            .HasDefaultValue("Standalone");
        entity.Property(e => e.IsActive).HasDefaultValue(true);
        entity.Property(e => e.CreatedAt)
            .HasPrecision(3)
            .HasDefaultValueSql("(sysutcdatetime())");
        entity.Property(e => e.UpdatedAt).HasPrecision(3);

        // LevelId / LessonId: intentionally no relationship configured.
        // Both are loosely-coupled references into the Content module —
        // no physical FK exists in the database by design; integrity is
        // enforced at the application layer.
    }
}