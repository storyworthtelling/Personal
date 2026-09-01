using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ledger.Domain.Model;

namespace Ledger.Infrastructure.Persistence;

// The data-access layer (per the repo's architecture: EF Core DbContext directly, no repository
// wrapper). All refresh state lives here so the background worker resumes purely from the database.
public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options)
    : DbContext(options)
{
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<Account> Accounts => Set<Account>();

    // SQLite has no native DateTimeOffset, and the provider cannot translate comparisons on the
    // default TEXT mapping. Storing UTC ticks as integers makes the worker's "due institution" time
    // comparisons translate to plain integer comparisons.
    private static readonly ValueConverter<DateTimeOffset, long> TicksConverter =
        new(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));

    private static readonly ValueConverter<DateTimeOffset?, long?> NullableTicksConverter =
        new(value => value.HasValue ? value.Value.UtcTicks : null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Institution>(entity =>
        {
            entity.HasKey(institution => institution.Id);
            entity.Property(institution => institution.RefreshStatus).HasConversion<int>();
            entity.Property(institution => institution.NextAttemptAtUtc).HasConversion(NullableTicksConverter);
            entity.Property(institution => institution.LastRefreshedUtc).HasConversion(NullableTicksConverter);
            entity.Property(institution => institution.CreatedAtUtc).HasConversion(TicksConverter);
            entity.HasMany(institution => institution.Accounts)
                .WithOne()
                .HasForeignKey(account => account.InstitutionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(institution => new { institution.RefreshStatus, institution.NextAttemptAtUtc });
        });

        model.Entity<Account>(entity =>
        {
            entity.HasKey(account => account.Id);
            entity.Property(account => account.Kind).HasConversion<int>();
            entity.Property(account => account.BalanceAsOfUtc).HasConversion(NullableTicksConverter);
            entity.Property(account => account.NextPaymentDueDate).HasConversion(NullableTicksConverter);
            entity.HasIndex(account => new { account.InstitutionId, account.AccountId });
        });
    }
}
