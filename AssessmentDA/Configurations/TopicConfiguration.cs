using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssessmentDA.Configurations;

public class TopicConfiguration : IEntityTypeConfiguration<Topic>
{
    public void Configure(EntityTypeBuilder<Topic> entity)
    {
        entity.ToTable("Topics", "Assessment", tb =>
            tb.HasCheckConstraint("CK_Topics_LearningLevel",
                "[LearningLevel] IN ('Beginner', 'Intermediate', 'Advanced')"));

        entity.HasIndex(e => e.CategoryId, "IX_Topics_CategoryId");
        entity.HasIndex(e => e.LearningLevel, "IX_Topics_LearningLevel");
        entity.HasIndex(e => e.Name, "UQ_Topics_Name").IsUnique();

        entity.Property(e => e.Name).HasMaxLength(200);
        entity.Property(e => e.LearningLevel).HasMaxLength(20);
        entity.Property(e => e.IsActive).HasDefaultValue(true);
        entity.Property(e => e.CreatedAt)
            .HasPrecision(3)
            .HasDefaultValueSql("(sysutcdatetime())");
        entity.Property(e => e.UpdatedAt).HasPrecision(3);

        // CategoryId is NOT NULL; the DB has no ON DELETE clause on this FK
        // (i.e. NO ACTION). Restrict is the correct EF equivalent — a
        // Category with Topics attached cannot be deleted.
        entity.HasOne(d => d.Category).WithMany(p => p.Topics)
            .HasForeignKey(d => d.CategoryId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Topics_Categories");
    }
}