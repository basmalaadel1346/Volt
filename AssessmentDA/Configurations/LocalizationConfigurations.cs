using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssessmentDA.Configurations;

public class LanguageConfiguration : IEntityTypeConfiguration<Language>
{
    public void Configure(EntityTypeBuilder<Language> entity)
    {
        entity.ToTable("Languages", "Assessment");
        entity.HasKey(e => e.Code);

        entity.Property(e => e.Code).HasMaxLength(5);
        entity.Property(e => e.Name).HasMaxLength(50);
        entity.Property(e => e.IsActive).HasDefaultValue(true);

        entity.HasIndex(e => e.Name, "UQ_Languages_Name").IsUnique();
    }
}

public class QuizTranslationConfiguration : IEntityTypeConfiguration<QuizTranslation>
{
    public void Configure(EntityTypeBuilder<QuizTranslation> entity)
    {
        entity.ToTable("QuizTranslations", "Assessment");

        entity.HasIndex(e => new { e.QuizId, e.LanguageCode }, "UQ_QuizTranslations_QuizId_Language").IsUnique();

        entity.Property(e => e.LanguageCode).HasMaxLength(5);
        entity.Property(e => e.Title).HasMaxLength(300);

        entity.HasOne(d => d.Quiz).WithMany(p => p.QuizTranslations)
            .HasForeignKey(d => d.QuizId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_QuizTranslations_Quizzes");

        // Language is a lookup with no navigation back — the FK exists to stop
        // an unknown code entering, not to be traversed.
        entity.HasOne<Language>().WithMany()
            .HasForeignKey(d => d.LanguageCode)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuizTranslations_Languages");
    }
}

public class QuestionTranslationConfiguration : IEntityTypeConfiguration<QuestionTranslation>
{
    public void Configure(EntityTypeBuilder<QuestionTranslation> entity)
    {
        entity.ToTable("QuestionTranslations", "Assessment");

        entity.HasIndex(e => new { e.QuestionId, e.LanguageCode }, "UQ_QuestionTranslations_QuestionId_Language").IsUnique();

        entity.Property(e => e.LanguageCode).HasMaxLength(5);

        entity.HasOne(d => d.Question).WithMany(p => p.QuestionTranslations)
            .HasForeignKey(d => d.QuestionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_QuestionTranslations_Questions");

        entity.HasOne<Language>().WithMany()
            .HasForeignKey(d => d.LanguageCode)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuestionTranslations_Languages");
    }
}

public class QuestionOptionTranslationConfiguration : IEntityTypeConfiguration<QuestionOptionTranslation>
{
    public void Configure(EntityTypeBuilder<QuestionOptionTranslation> entity)
    {
        entity.ToTable("QuestionOptionTranslations", "Assessment");

        entity.HasIndex(e => new { e.QuestionOptionId, e.LanguageCode }, "UQ_QuestionOptionTranslations_OptionId_Language").IsUnique();

        entity.Property(e => e.LanguageCode).HasMaxLength(5);

        entity.HasOne(d => d.QuestionOption).WithMany(p => p.QuestionOptionTranslations)
            .HasForeignKey(d => d.QuestionOptionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_QuestionOptionTranslations_QuestionOptions");

        entity.HasOne<Language>().WithMany()
            .HasForeignKey(d => d.LanguageCode)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuestionOptionTranslations_Languages");
    }
}

public class TopicTranslationConfiguration : IEntityTypeConfiguration<TopicTranslation>
{
    public void Configure(EntityTypeBuilder<TopicTranslation> entity)
    {
        entity.ToTable("TopicTranslations", "Assessment");

        entity.HasIndex(e => new { e.TopicId, e.LanguageCode }, "UQ_TopicTranslations_TopicId_Language").IsUnique();

        entity.Property(e => e.LanguageCode).HasMaxLength(5);
        entity.Property(e => e.Name).HasMaxLength(200);

        entity.HasOne(d => d.Topic).WithMany(p => p.TopicTranslations)
            .HasForeignKey(d => d.TopicId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_TopicTranslations_Topics");

        entity.HasOne<Language>().WithMany()
            .HasForeignKey(d => d.LanguageCode)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TopicTranslations_Languages");
    }
}

public class CategoryTranslationConfiguration : IEntityTypeConfiguration<CategoryTranslation>
{
    public void Configure(EntityTypeBuilder<CategoryTranslation> entity)
    {
        entity.ToTable("CategoryTranslations", "Assessment");

        entity.HasIndex(e => new { e.CategoryId, e.LanguageCode }, "UQ_CategoryTranslations_CategoryId_Language").IsUnique();

        entity.Property(e => e.LanguageCode).HasMaxLength(5);
        entity.Property(e => e.Name).HasMaxLength(100);

        entity.HasOne(d => d.Category).WithMany(p => p.CategoryTranslations)
            .HasForeignKey(d => d.CategoryId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_CategoryTranslations_Categories");

        entity.HasOne<Language>().WithMany()
            .HasForeignKey(d => d.LanguageCode)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_CategoryTranslations_Languages");
    }
}
