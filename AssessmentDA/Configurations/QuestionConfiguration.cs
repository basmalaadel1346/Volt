using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssessmentDA.Configurations;

public class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> entity)
    {
        entity.ToTable("Questions", "Assessment", tb =>
        {
            tb.HasCheckConstraint("CK_Questions_Difficulty",
                "[Difficulty] IN ('Easy', 'Medium', 'Hard', 'Advanced')");
            tb.HasCheckConstraint("CK_Questions_Points", "[Points] > 0");
            tb.HasCheckConstraint("CK_Questions_QuestionType",
                "[QuestionType] IN ('MultipleChoice', 'TrueFalse', 'Essay')");
        });

        entity.HasIndex(e => e.Difficulty, "IX_Questions_Difficulty");
        entity.HasIndex(e => e.QuestionType, "IX_Questions_QuestionType");
        entity.HasIndex(e => e.TopicId, "IX_Questions_TopicId");
        entity.HasIndex(e => new { e.QuizId, e.DisplayOrder }, "UQ_Questions_QuizId_DisplayOrder").IsUnique();

        entity.Property(e => e.Difficulty)
            .HasMaxLength(20)
            .HasDefaultValue("Medium");
        entity.Property(e => e.Points).HasDefaultValue((byte)1);
        entity.Property(e => e.QuestionType)
            .HasMaxLength(20)
            .HasDefaultValue("MultipleChoice");
        entity.Property(e => e.ImageUrl).HasMaxLength(500);
        entity.Property(e => e.ImageDescription).HasMaxLength(1000);
        entity.Property(e => e.IsActive).HasDefaultValue(false);
        entity.Property(e => e.CreatedAt)
            .HasPrecision(3)
            .HasDefaultValueSql("(sysutcdatetime())");

        // DB: ON DELETE CASCADE — deleting a Quiz removes its Questions.
        entity.HasOne(d => d.Quiz).WithMany(p => p.Questions)
            .HasForeignKey(d => d.QuizId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_Questions_Quizzes");

        // DB: no ON DELETE clause (NO ACTION) — a Topic with Questions
        // attached cannot be deleted.
        entity.HasOne(d => d.Topic).WithMany(p => p.Questions)
            .HasForeignKey(d => d.TopicId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Questions_Topics");
    }
}