using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssessmentDA.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> entity)
    {
        entity.ToTable("Categories", "Assessment");

        // Categories.Id is a plain TINYINT PK, manually assigned via seed
        // data — NOT an IDENTITY column. Without this, EF Core's default
        // convention treats an integer "Id" PK as store-generated, and
        // every insert would omit Id, violating the NOT NULL column.
        entity.Property(e => e.Id).ValueGeneratedNever();

        entity.HasIndex(e => e.Name, "UQ_Categories_Name").IsUnique();

        entity.Property(e => e.Name).HasMaxLength(100);
        entity.Property(e => e.SortOrder).HasDefaultValue((short)0);
        entity.Property(e => e.IsActive).HasDefaultValue(true);
    }
}