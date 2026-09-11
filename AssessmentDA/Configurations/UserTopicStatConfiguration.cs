using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssessmentDA.Configurations;

public class UserTopicStatConfiguration : IEntityTypeConfiguration<UserTopicStat>
{
    public void Configure(EntityTypeBuilder<UserTopicStat> entity)
    {
        entity.ToTable("UserTopicStats", "Assessment", tb =>
        {
            tb.HasCheckConstraint("CK_UserTopicStats_Difficulty",
                "[Difficulty] IN ('Easy', 'Medium', 'Hard', 'Advanced')");
            tb.HasCheckConstraint("CK_UserTopicStats_CorrectVsAnswered",
                "[CorrectCount] <= [QuestionsAnsweredCount]");
            tb.HasCheckConstraint("CK_UserTopicStats_QuestionsAnsweredNonNegative",
                "[QuestionsAnsweredCount] >= 0");
            tb.HasCheckConstraint("CK_UserTopicStats_CorrectCountNonNegative",
                "[CorrectCount] >= 0");
            tb.HasCheckConstraint("CK_UserTopicStats_HintsUsedNonNegative",
                "[HintsUsedCount] >= 0");
        });

        entity.HasIndex(e => e.TopicId, "IX_UserTopicStats_TopicId");
        entity.HasIndex(e => new { e.UserId, e.TopicId, e.Difficulty }, "UQ_UserTopicStats_UserId_TopicId_Difficulty").IsUnique();

        entity.Property(e => e.Difficulty).HasMaxLength(20);
        entity.Property(e => e.LastPracticedAt).HasPrecision(3);
        entity.Property(e => e.UpdatedAt)
            .HasPrecision(3)
            .HasDefaultValueSql("(sysutcdatetime())");

        entity.Property(e => e.WrongCount)
            .HasComputedColumnSql("([QuestionsAnsweredCount]-[CorrectCount])", stored: true);

        // DB: no ON DELETE clause (NO ACTION) — a Topic with stats attached
        // cannot be deleted.
        entity.HasOne(d => d.Topic).WithMany(p => p.UserTopicStats)
            .HasForeignKey(d => d.TopicId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_UserTopicStats_Topics");

        // DB: ON DELETE SET NULL — if a referenced QuizAttempt is ever
        // deleted, the aggregate stats row survives; only the traceability
        // pointer is cleared. LastQuizAttemptId is nullable, so SetNull is
        // valid here (unlike the six Restrict cases above).
        entity.HasOne(d => d.LastQuizAttempt).WithMany(p => p.UserTopicStats)
            .HasForeignKey(d => d.LastQuizAttemptId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("FK_UserTopicStats_QuizAttempts");

        // UserId: loosely coupled reference to the Identity module (JWT
        // claim). No physical FK by design.
    }
}