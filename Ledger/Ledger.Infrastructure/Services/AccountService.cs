using Microsoft.EntityFrameworkCore;
using Ledger.Domain.Model;
using Ledger.Domain.Time;
using Ledger.Infrastructure.Persistence;

namespace Ledger.Infrastructure.Services;

// The application service the UI talks to, so pages never touch the DbContext directly (per the repo's
// architecture). Reads institutions for display and handles connect / remove / manual-refresh actions.
public sealed class AccountService
{
    private readonly IDbContextFactory<LedgerDbContext> dbContextFactory;
    private readonly IClock clock;

    public AccountService(IDbContextFactory<LedgerDbContext> dbContextFactory, IClock clock)
    {
        this.dbContextFactory = dbContextFactory;
        this.clock = clock;
    }

    // Institutions with their accounts, oldest first, for the account list.
    public async Task<IReadOnlyList<Institution>> GetInstitutionsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Institutions
            .Include(i => i.Accounts)
            .OrderBy(i => i.CreatedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasInstitutionsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Institutions.AnyAsync(cancellationToken);
    }

    // Records a newly connected Plaid Item and marks it due for an immediate first refresh.
    public async Task<Institution> AddInstitutionAsync(
        string itemId, string accessToken, string name, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var institution = new Institution
        {
            ItemId = itemId,
            AccessToken = accessToken,
            Name = name,
            RefreshStatus = WorkStatus.Pending,
            NextAttemptAtUtc = null, // due now
            CreatedAtUtc = clock.UtcNow
        };
        db.Institutions.Add(institution);
        await db.SaveChangesAsync(cancellationToken);
        return institution;
    }

    public async Task RemoveInstitutionAsync(Guid institutionId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var institution = await db.Institutions.FirstOrDefaultAsync(i => i.Id == institutionId, cancellationToken);
        if (institution is null)
            return;
        db.Institutions.Remove(institution); // cascades to accounts
        await db.SaveChangesAsync(cancellationToken);
    }

    // Marks every institution due right now (used by "Refresh now"), including previously failed ones so
    // a manual pull gives them another chance.
    public async Task MarkAllDueAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var institutions = await db.Institutions.ToListAsync(cancellationToken);
        foreach (var institution in institutions)
        {
            institution.RefreshStatus = WorkStatus.Pending;
            institution.Attempts = 0;
            institution.NextAttemptAtUtc = null;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
