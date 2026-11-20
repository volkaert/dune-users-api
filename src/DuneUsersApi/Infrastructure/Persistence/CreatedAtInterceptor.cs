using DuneUsersApi.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DuneUsersApi.Infrastructure.Persistence;

/// <summary>
/// Renseigne <see cref="User.CreatedAt"/> avec l'heure de l'application (en UTC) pour chaque user inséré.
/// </summary>
public sealed class CreatedAtInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        SetCreatedAt(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        SetCreatedAt(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void SetCreatedAt(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries<User>().Where(e => e.State == EntityState.Added))
        {
            entry.Property(u => u.CreatedAt).CurrentValue = now;
        }
    }
}
