using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssessmentDA.Configurations;

public class QuizAttemptMistakeConfiguration : IEntityTypeConfiguration<QuizAttemptMistake>
{
    public void Configure(EntityTypeBuilder<QuizAttemptMistake> entity)
    {
        entity.ToTable("QuizAttemptMistakes", "Assessment");

        entity.HasIndex(e => e.QuestionId, "IX_QuizAttemptMistakes_QuestionId");
        entity.HasIndex(e => new { e.QuizAttemptId, e.QuestionId }, "UQ_QuizAttemptMistakes_AttemptId_QuestionId").IsUnique();

        entity.Property(e => e.CreatedAt)
            .HasPrecision(3)
            .HasDefaultValueSql("(sysutcdatetime())");

        // DB: ON DELETE CASCADE
        entity.HasOne(d => d.QuizAttempt).WithMany(p => p.QuizAttemptMistakes)
            .HasForeignKey(d => d.QuizAttemptId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_QuizAttemptMistakes_QuizAttempts");

        // Kept for direct EF Core navigation (e.g., .Include(x => x.Question))
        entity.HasOne(d => d.Question).WithMany(p => p.QuizAttemptMistakes)
            .HasForeignKey(d => d.QuestionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuizAttemptMistakes_Questions");

        // Composite FK guaranteeing SelectedOptionId actually belongs to QuestionId.
        entity.HasOne(d => d.QuestionOption).WithMany(p => p.QuizAttemptMistakes)
            .HasPrincipalKey(p => new { p.QuestionId, p.Id })
            .HasForeignKey(d => new { d.QuestionId, d.SelectedOptionId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuizAttemptMistakes_QuestionId_SelectedOptionId");

        // 1-to-0..1 relationship linking mistake to the exact attempt question business rule
        entity.HasOne(d => d.QuizAttemptQuestion)
            .WithOne(d => d.QuizAttemptMistake)
            .HasPrincipalKey<QuizAttemptQuestion>(p => new { p.QuizAttemptId, p.QuestionId })
            .HasForeignKey<QuizAttemptMistake>(d => new { d.QuizAttemptId, d.QuestionId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuizAttemptMistakes_QuizAttemptQuestions");
    }
}