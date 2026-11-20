using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneUsersApi.Infrastructure.Persistence.Users;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t => t.HasCheckConstraint(
            "ck_users_address_country_code",
            "address_country_code ~ '^[A-Z]{2}$'"));

        builder.HasKey(u => u.Id);

        builder.Property(u => u.TenantId).HasMaxLength(100);
        builder.Property(u => u.TenantUserId).HasMaxLength(100);
        builder.Property(u => u.FirstName).HasMaxLength(100);
        builder.Property(u => u.LastName).HasMaxLength(100);

        builder.ComplexProperty(u => u.Address, address =>
        {
            address.Property(a => a.Street).HasMaxLength(200);
            address.Property(a => a.PostalCode).HasMaxLength(20);
            address.Property(a => a.City).HasMaxLength(100);
            address.Property(a => a.CountryCode).HasMaxLength(2).IsFixedLength();
        });

        builder.HasIndex(u => new { u.TenantId, u.TenantUserId })
            .IsUnique()
            .HasDatabaseName("ux_users_tenant_id_tenant_user_id");

        builder.HasIndex(u => u.OnboardedAt)
            .HasDatabaseName("ix_users_onboarded_at");
    }
}
