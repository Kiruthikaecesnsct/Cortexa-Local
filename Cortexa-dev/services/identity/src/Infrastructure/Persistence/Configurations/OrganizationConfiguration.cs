using Cortexa.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cortexa.Identity.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasColumnName("id")
            .HasColumnType("uuid");

        builder.Property(o => o.Name)
            .HasColumnName("name")
            .HasColumnType("VARCHAR(200)")
            .IsRequired();

        builder.HasIndex(o => o.Name).IsUnique().HasFilter("deleted_at IS NULL");

        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("TIMESTAMPTZ")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(o => o.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("TIMESTAMPTZ")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(o => o.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("TIMESTAMPTZ")
            .IsRequired(false);

        builder.HasQueryFilter(o => o.DeletedAt == null);
    }
}
