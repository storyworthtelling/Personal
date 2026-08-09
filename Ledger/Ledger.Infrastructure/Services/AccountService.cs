using Microsoft.EntityFrameworkCore;
using Ledger.Domain.Model;
using Ledger.Domain.Time;
using Ledger.Infrastructure.Persistence;

namespace Ledger.Infrastructure.Services;

// One connected Plaid Item's durable credentials, moved between the database and the shared secrets vault.
public sealed record InstitutionCredential(string ItemId, string AccessToken, string Name);

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

    // The durable per-Item Plaid credentials, for backing up to the shared secrets vault. These tokens are
    // generated on-device by Hosted Link and are otherwise lost on a factory reset — without a backup every
    // bank would have to be re-linked by hand after the FocusPhone cutover.
    public async Task<IReadOnlyList<InstitutionCredential>> GetInstitutionCredentialsAsync(
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Institutions.AsNoTracking().OrderBy(i => i.CreatedAtUtc).ToListAsync(cancellationToken);
        return rows.Select(i => new InstitutionCredential(i.ItemId, i.AccessToken, i.Name)).ToList();
    }

    // Re-creates institutions from vault-restored credentials after a reinstall, skipping any Item already
    // present (matched by ItemId) and any with a blank id/token. Each restored Item is marked due now so the
    // next refresh repopulates its accounts and balances from Plaid using the restored access token. Returns
    // how many were added.
    public async Task<int> RestoreInstitutionsAsync(
        IEnumerable<InstitutionCredential> credentials, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = (await db.Institutions.Select(i => i.ItemId).ToListAsync(cancellationToken)).ToHashSet();

        var added = 0;
        foreach (var credential in credentials)
        {
            if (string.IsNullOrEmpty(credential.ItemId) || string.IsNullOrEmpty(credential.AccessToken)
                || !existing.Add(credential.ItemId))
                continue;

            db.Institutions.Add(new Institution
            {
                ItemId = credential.ItemId,
                AccessToken = credential.AccessToken,
                Name = credential.Name,
                RefreshStatus = WorkStatus.Pending,
                NextAttemptAtUtc = null, // due now — the next refresh refills accounts/balances
                CreatedAtUtc = clock.UtcNow
            });
            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(cancellationToken);
        return added;
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
