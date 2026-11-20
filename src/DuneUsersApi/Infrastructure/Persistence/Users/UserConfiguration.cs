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

        // Sert la réconciliation des onboardings : filtre par tenant et plage de dates, tri par date puis
        // tenant_user_id (départage des ex æquo), sans étape de tri et sans lecture de la table.
        builder.HasIndex(u => new { u.TenantId, u.OnboardedAt, u.TenantUserId })
            .HasDatabaseName("ix_users_tenant_id_onboarded_at_tenant_user_id");
    }
}
