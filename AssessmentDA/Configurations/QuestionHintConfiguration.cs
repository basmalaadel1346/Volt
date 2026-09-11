using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssessmentDA.Configurations;

public class QuestionHintConfiguration : IEntityTypeConfiguration<QuestionHint>
{
    public void Configure(EntityTypeBuilder<QuestionHint> entity)
    {
        entity.ToTable("QuestionHints", "Assessment", tb =>
            tb.HasCheckConstraint("CK_QuestionHints_HintSequence", "[HintSequence] > 0"));

        entity.HasIndex(e => new { e.QuizAttemptMistakeId, e.LanguageCode, e.HintSequence },
            "UQ_QuestionHints_MistakeId_Language_Sequence").IsUnique();

        entity.Property(e => e.LanguageCode)
            .HasMaxLength(5)
            .HasDefaultValue("ar");

        entity.HasOne<Language>().WithMany()
            .HasForeignKey(e => e.LanguageCode)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuestionHints_Languages");

        entity.Property(e => e.GeneratedAt)
            .HasPrecision(3)
            .HasDefaultValueSql("(sysutcdatetime())");

        // DB: ON DELETE CASCADE — a hint has no meaning outside its
        // mistake, so it's removed with it.
        entity.HasOne(d => d.QuizAttemptMistake).WithMany(p => p.QuestionHints)
            .HasForeignKey(d => d.QuizAttemptMistakeId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_QuestionHints_QuizAttemptMistakes");
    }
}