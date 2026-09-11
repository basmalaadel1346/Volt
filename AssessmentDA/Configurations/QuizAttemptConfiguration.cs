using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssessmentDA.Configurations;

public class QuizAttemptConfiguration : IEntityTypeConfiguration<QuizAttempt>
{
    public void Configure(EntityTypeBuilder<QuizAttempt> entity)
    {
        entity.ToTable("QuizAttempts", "Assessment", tb =>
        {
            tb.HasCheckConstraint("CK_QuizAttempts_CorrectVsAnswered",
                "[CorrectAnswersCount] >= 0 AND [CorrectAnswersCount] <= [QuestionsAnsweredCount]");
            tb.HasCheckConstraint("CK_QuizAttempts_ScorePercentage",
                "[ScorePercentage] BETWEEN 0 AND 100");
            tb.HasCheckConstraint("CK_QuizAttempts_Status",
                "[Status] IN ('InProgress', 'Completed', 'Abandoned')");
            tb.HasCheckConstraint("CK_QuizAttempts_CompletedAfterStarted",
                "[CompletedAt] IS NULL OR [CompletedAt] >= [StartedAt]");
            tb.HasCheckConstraint("CK_QuizAttempts_TotalQuestionsPositive",
                "[TotalQuestionsAtAttempt] > 0");
            tb.HasCheckConstraint("CK_QuizAttempts_AnsweredVsTotal",
                "[QuestionsAnsweredCount] <= [TotalQuestionsAtAttempt]");
            tb.HasCheckConstraint("CK_QuizAttempts_CompletedRequiresAllAnswered",
                "[Status] <> 'Completed' OR ([QuestionsAnsweredCount] = [TotalQuestionsAtAttempt] AND [CompletedAt] IS NOT NULL)");
            tb.HasCheckConstraint("CK_QuizAttempts_NotSelfReferencing",
               "[PreviousAttemptId] IS NULL OR [PreviousAttemptId] <> [Id]");
        });

        entity.HasIndex(e => e.QuizId, "IX_QuizAttempts_QuizId");
        entity.HasIndex(e => new { e.UserId, e.QuizId, e.StartedAt }, "IX_QuizAttempts_UserId_QuizId_StartedAt");

        entity.Property(e => e.ScorePercentage).HasColumnType("decimal(5, 2)");
        entity.Property(e => e.Status)
            .HasMaxLength(20)
            .HasDefaultValue("InProgress");
        entity.Property(e => e.StartedAt)
            .HasPrecision(3)
            .HasDefaultValueSql("(sysutcdatetime())");
        entity.Property(e => e.CompletedAt).HasPrecision(3);

        // Computed, persisted columns — SQL Server owns these values.
        entity.Property(e => e.WrongAnswersCount)
            .HasComputedColumnSql("([QuestionsAnsweredCount]-[CorrectAnswersCount])", stored: true);
        entity.Property(e => e.DurationSeconds)
            .HasComputedColumnSql("(datediff(second,[StartedAt],[CompletedAt]))", stored: true);

        // DB: no ON DELETE clause (NO ACTION) — a Quiz with attempts
        // attached cannot be deleted. Protects historical attempt data.
        entity.HasOne(d => d.Quiz).WithMany(p => p.QuizAttempts)
            .HasForeignKey(d => d.QuizId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuizAttempts_Quizzes");

        // UserId: loosely coupled reference to the Identity module (JWT
        // claim). No physical FK by design.
        // Configurations/QuizAttemptConfiguration.cs — additions inside Configure(...)

        entity.HasIndex(e => e.PreviousAttemptId, "UQ_QuizAttempts_PreviousAttemptId").IsUnique();

        entity.HasOne(d => d.PreviousAttempt)
            .WithOne(d => d.NextAttempt)
            .HasForeignKey<QuizAttempt>(d => d.PreviousAttemptId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuizAttempts_PreviousAttempt");

        
    }
}