using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Index.Domain.External;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Infrastructure.Persistence;

namespace Index.Infrastructure.Services;

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

    // Institutions with their accounts, in the OWNER'S OWN ORDER (D123) — SortOrder, not CreatedAtUtc.
    // A brand-new institution lands at the bottom of whatever order already exists (AddInstitutionAsync/
    // RestoreInstitutionsAsync); the owner then rearranges from there with the long-press action card
    // (MoveInstitutionUpAsync/Down/ToBottomAsync), which is the only thing that changes this order again.
    public async Task<IReadOnlyList<Institution>> GetInstitutionsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Institutions
            .Include(i => i.Accounts)
            .OrderBy(i => i.SortOrder)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasInstitutionsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Institutions.AnyAsync(cancellationToken);
    }

    // Records a newly connected Plaid Item and marks it due for an immediate first refresh. Joins at
    // the bottom of the owner's existing order (D123) — never re-alphabetized in with the rest, since
    // that would silently move banks the owner had already arranged.
    public async Task<Institution> AddInstitutionAsync(
        string itemId, string accessToken, string name, CancellationToken cancellationToken,
        string? plaidInstitutionId = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existingCount = await db.Institutions.CountAsync(cancellationToken);
        var institution = new Institution
        {
            ItemId = itemId,
            AccessToken = accessToken,
            Name = name,
            PlaidInstitutionId = plaidInstitutionId,
            RefreshStatus = WorkStatus.Pending,
            NextAttemptAtUtc = null, // due now
            CreatedAtUtc = clock.UtcNow,
            SortOrder = existingCount
        };
        db.Institutions.Add(institution);
        await db.SaveChangesAsync(cancellationToken);
        return institution;
    }

    // The three moves the long-press action card offers (mockup 2), delegated to the pure
    // InstitutionOrder so the arithmetic is tested away from the database; this only reads the
    // current order, asks for the new one, and writes the returned SortOrder values back.
    public Task MoveInstitutionUpAsync(Guid institutionId, CancellationToken cancellationToken) =>
        ApplyOrderAsync(institutionId, InstitutionOrder.MoveUp, cancellationToken);

    public Task MoveInstitutionDownAsync(Guid institutionId, CancellationToken cancellationToken) =>
        ApplyOrderAsync(institutionId, InstitutionOrder.MoveDown, cancellationToken);

    public Task MoveInstitutionToBottomAsync(Guid institutionId, CancellationToken cancellationToken) =>
        ApplyOrderAsync(institutionId, InstitutionOrder.MoveToBottom, cancellationToken);

    private async Task ApplyOrderAsync(
        Guid institutionId,
        Func<IReadOnlyList<Guid>, Guid, IReadOnlyList<(Guid Id, int SortOrder)>> move,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var institutions = await db.Institutions.OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        var newOrder = move(institutions.Select(i => i.Id).ToList(), institutionId);

        var bySortOrder = newOrder.ToDictionary(entry => entry.Id, entry => entry.SortOrder);
        foreach (var institution in institutions)
        {
            institution.SortOrder = bySortOrder[institution.Id];
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // The durable per-Item Plaid credentials, for backing up to the shared secrets vault. These tokens are
    // generated on-device by Hosted Link and are otherwise lost on a factory reset — without a backup every
    // bank would have to be re-linked by hand after the FocusPhone cutover.
    public async Task<IReadOnlyList<InstitutionCredential>> GetInstitutionCredentialsAsync(
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // Backed up in the owner's own order (D123), not creation order, so a restore onto a fresh
        // install (below) reproduces the arrangement the owner actually had rather than the order the
        // banks happened to be connected in.
        var rows = await db.Institutions.AsNoTracking().OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
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
        // credentials arrives in the owner's own order (GetInstitutionCredentialsAsync backs it up
        // that way), so appending in that same order onto whatever this fresh install already has
        // reproduces the owner's arrangement (D123) rather than the order the vault's dictionary keys
        // happen to enumerate in.
        var nextSortOrder = await db.Institutions.CountAsync(cancellationToken);

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
                CreatedAtUtc = clock.UtcNow,
                SortOrder = nextSortOrder++
            });
            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    // Fetches and caches a just-linked bank's Plaid primary_color (D127), so the post-it is tinted from
    // the first time it is drawn rather than after the next refresh's backfill. The shared once-only
    // rules live in InstitutionColorLookup; a failed lookup is logged there and never thrown, so a bank
    // that linked successfully is never reported as failed over its colour — the refresh backfills it.
    public async Task LookUpInstitutionColorAsync(
        Guid institutionId, IPlaidClient plaid, ILogger logger, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var institution = await db.Institutions.FirstOrDefaultAsync(i => i.Id == institutionId, cancellationToken);
        if (institution is null)
            return;
        await InstitutionColorLookup.ResolveAsync(institution, plaid, clock.UtcNow, logger, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- Release C: adding an account by hand (D108, D118, D130) ----

    // D118: "Bank or lender" decides which heading the card sits under — matched case-insensitively
    // against every existing institution's Name (Plaid-connected or hand-entered), so an account typed
    // under a bank Index already knows about joins that bank's heading rather than starting a second
    // one. Left blank, the account is its own heading (a brand-new institution bearing the account's
    // own name). D108: only name and balance are required; everything else stays null until the owner
    // fills it in from the post-it (D111/D159), and an account with no Apr/NextPaymentAmount simply
    // does not factor into the Snowball yet.
    public async Task<Account> AddManualAccountAsync(
        string name, string? bankOrLender, AccountKind kind, decimal balance,
        decimal? apr, decimal? payment, PaymentCadence? cadence, DateTimeOffset? nextDueUtc,
        CancellationToken cancellationToken, decimal? creditLimit = null)
    {
        // Kind decides the form (mockup 4), so it decides what is kept: a bank account has no payment to
        // store and only a card has a limit. A caller that hands over a hidden field's value cannot save it.
        var kept = ManualAccountForm.Resolve(kind, apr, payment, creditLimit, cadence, nextDueUtc);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var headingName = string.IsNullOrWhiteSpace(bankOrLender) ? name.Trim() : bankOrLender.Trim();
        var institutions = await db.Institutions.ToListAsync(cancellationToken);
        var institution = institutions.FirstOrDefault(
            i => string.Equals(i.Name, headingName, StringComparison.OrdinalIgnoreCase));

        if (institution is null)
        {
            var now = clock.UtcNow;
            institution = new Institution
            {
                Name = headingName,
                ItemId = string.Empty,
                AccessToken = string.Empty, // empty AccessToken IS "hand-entered" (MainPage's own convention)
                // Completed, not Pending: a hand-entered institution is never selected by the refresh
                // worker (BalanceRefreshProcessor filters on a non-empty AccessToken), so it must not
                // read as "still settling" on the first-run/"Bringing back N banks" status line either.
                RefreshStatus = WorkStatus.Completed,
                CreatedAtUtc = now,
                LastRefreshedUtc = now,
                SortOrder = institutions.Count,
            };
            db.Institutions.Add(institution);
        }

        var account = new Account
        {
            InstitutionId = institution.Id,
            // Plaid's AccountId is how a refresh matches a row back; a manual account has no Plaid
            // side to match against, so it gets its own locally-unique id rather than leaving the
            // field empty (which every OTHER manual account under the same institution would then
            // share, colliding on the (InstitutionId, AccountId) index).
            AccountId = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            Kind = kind,
            Source = AccountSource.Manual,
            CurrentBalance = balance,
            BalanceAsOfUtc = clock.UtcNow,
            BalanceSource = BalanceSource.ByHand,
            Apr = kept.Apr,
            NextPaymentAmount = kept.Payment,
            CreditLimit = kept.CreditLimit,
            PaymentCadence = kept.Cadence,
            NextPaymentDueDate = kept.NextDue,
        };
        db.Accounts.Add(account);

        await db.SaveChangesAsync(cancellationToken);
        return account;
    }

    // ---- Release C: editing a manual account's fields, on its own post-it (D111, D159) ----

    // Writes ONLY the fields the patch names (R-G15). The post-it used to hand over all five on every blur,
    // on the reasoning that they were all on screen together — but "on screen" included a Next due picker that
    // defaulted to today and an Every control that defaulted to Month, so editing Balance alone stored a due
    // date and a cadence the owner never chose, and a garbled APR was read as null and cleared a stored one. A
    // field left null in the patch is left exactly as it is; nothing here can clear a figure. The balance's
    // "as of" moves only when the balance itself does. Returns the account as stored now (so the page can
    // update the card in place), or null if it no longer exists.
    public async Task<Account?> UpdateManualAccountAsync(
        Guid accountId, ManualAccountPatch patch, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        if (patch.IsEmpty)
        {
            return account;
        }

        if (patch.Balance is { } balance)
        {
            account.CurrentBalance = balance;
            account.BalanceAsOfUtc = clock.UtcNow;
            account.BalanceSource = BalanceSource.ByHand; // the owner's figure now, whatever it replaced
        }

        if (patch.Apr is { } apr)
        {
            account.Apr = apr;
        }

        if (patch.Payment is { } payment)
        {
            account.NextPaymentAmount = payment;
        }

        if (patch.CreditLimit is { } creditLimit)
        {
            account.CreditLimit = creditLimit;
        }

        if (patch.Cadence is { } cadence)
        {
            account.PaymentCadence = cadence;
        }

        if (patch.NextDue is { } nextDue)
        {
            account.NextPaymentDueDate = nextDue;
        }

        await db.SaveChangesAsync(cancellationToken);
        return account;
    }

    // D56/D159: the owner's Savings override, for a CONNECTED account's one editable field.
    public async Task SetSavingsOverrideAsync(Guid accountId, bool isSavings, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return;
        }

        account.IsSavingsOverride = isSavings;
        await db.SaveChangesAsync(cancellationToken);
    }

    // D112: undo-first Remove for a hand-entered account. FindAccountAsync captures the row (detached)
    // before RemoveAccountAsync deletes it, the same shape DisconnectInstitution already uses for an
    // institution — RestoreAccountAsync below is the reverse the dog-ear's undo calls.
    public async Task<Account?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
    }

    public async Task RemoveAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return;
        }

        db.Accounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken);
    }

    // The undo's reverse: re-inserts the captured row exactly as it was, including its original Id, so
    // the card reappears where it was rather than as a new account at the end. A no-op if the window
    // somehow closed twice (defensive; DogEarState only ever fires one reverse per fold).
    public async Task RestoreAccountAsync(Account account, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Accounts.AnyAsync(a => a.Id == account.Id, cancellationToken))
        {
            return;
        }

        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- Release C: reconnecting in Plaid's update mode (D150) ----

    // Repairs a broken connection IN PLACE — same institution row, same SortOrder, same Name — rather
    // than creating a fresh one: a reconnect must never look like a brand-new bank landing at the
    // bottom of the owner's order. Resets exactly the failure state a fresh AddInstitutionAsync/
    // RestoreInstitutionsAsync also clear, and marks it due for an immediate refresh.
    public async Task ReconnectInstitutionAsync(
        Guid institutionId, string newAccessToken, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var institution = await db.Institutions.FirstOrDefaultAsync(i => i.Id == institutionId, cancellationToken);
        if (institution is null)
        {
            return;
        }

        institution.AccessToken = newAccessToken;
        institution.RefreshStatus = WorkStatus.Pending;
        institution.Attempts = 0;
        institution.LastError = null;
        institution.NextAttemptAtUtc = null; // due now
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- Release C: Post's BalanceReceiver handoff (D87, D91) ----

    // Routes one incoming balance to at most one account, by the pure precedence in
    // BalanceReceiverMatch: a hand-entered account's servicer first (D87), a STALE (RefreshStatus ==
    // Failed) Plaid institution of the same name only as a fallback (D91). Returns whether anything
    // matched, so the caller (BalanceReceiver) can log a miss rather than assume success.
    public async Task<bool> ApplyBalanceReceiptAsync(
        string merchantName, decimal balance, DateOnly? asOf, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var institutions = await db.Institutions.Include(i => i.Accounts).ToListAsync(cancellationToken);

        var candidates = new List<BalanceMatchCandidate>();
        foreach (var institution in institutions)
        {
            var isHandEntered = string.IsNullOrEmpty(institution.AccessToken);
            if (isHandEntered)
            {
                foreach (var account in institution.Accounts)
                {
                    candidates.Add(new BalanceMatchCandidate(account.Id, institution.Name, BalanceMatchSource.Manual));
                }
            }
            else if (institution.RefreshStatus == WorkStatus.Failed)
            {
                foreach (var account in institution.Accounts)
                {
                    candidates.Add(new BalanceMatchCandidate(account.Id, institution.Name, BalanceMatchSource.Plaid));
                }
            }
        }

        var matchedAccountId = BalanceReceiverMatch.Resolve(merchantName, candidates);
        if (matchedAccountId is null)
        {
            return false;
        }

        var matched = institutions.SelectMany(i => i.Accounts).First(a => a.Id == matchedAccountId);
        matched.CurrentBalance = balance;
        // A Post balance's as-of is a DATE (mockup 15's "From Post, Sep 12"), stored at UTC midnight like a due
        // date and read back through CalendarDate. A receipt with no statement date is dated the phone's day
        // today — not UTC's, which is already tomorrow on a US evening.
        matched.BalanceAsOfUtc = CalendarDate.ToStored(asOf ?? CalendarDate.PhoneDay(clock.UtcNow));
        matched.BalanceSource = BalanceSource.Post;
        await db.SaveChangesAsync(cancellationToken);
        return true;
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

    // ---- Undo-first disconnect (mockup 2, Guard R-G11) ----
    //
    // Removing a bank cascade-deletes its accounts (Account.InstitutionId is the ONLY foreign key in the
    // model). Everything else keyed by an account id — Snowball plans and snapshots, savings goals and
    // snapshots, cached transactions, worksheet set-asides — is a plain column with no constraint, so it
    // is left in place by the delete and re-attaches the moment the account returns under its original id.
    //
    // CHOICE: snapshot-then-restore, not "defer the delete until the window closes". A deferred delete
    // would lose nothing on a kill mid-window, but the bank would have to be hidden from every reader
    // (Accounts, Snowball, Savings, Budget, the household snapshot, the refresh worker) through a new
    // column — a schema change with a wide blast radius, and SetupPage already shows the cost of the other
    // road. The snapshot is taken in the same unit of work as the delete, so what is restored is by
    // construction what was removed; the cost is that a process kill inside the six-second window loses the
    // undo (never the owner's intent: they asked for the removal), and the deferred /item/remove, which is
    // only ever sent from CommitDisconnectAsync, is not sent.

    /// <summary>Captures the institution and every account under it, then removes it. Null when there is no
    /// such institution. The returned handle is what the dog-ear's undo and the window's close both act on.</summary>
    public async Task<DisconnectedInstitution?> DisconnectInstitutionAsync(
        Guid institutionId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Untracked, so the copy is detached and complete by construction: every mapped column of the
        // institution and of each account, including ones added after this was written, is materialised
        // by EF itself rather than copied field by field.
        var snapshot = await db.Institutions.AsNoTracking().Include(i => i.Accounts)
            .FirstOrDefaultAsync(i => i.Id == institutionId, cancellationToken);
        if (snapshot is null)
            return null;

        var institution = await db.Institutions.FirstAsync(i => i.Id == institutionId, cancellationToken);
        db.Institutions.Remove(institution); // cascades to accounts
        await db.SaveChangesAsync(cancellationToken);
        return new DisconnectedInstitution(snapshot);
    }

    /// <summary>Re-inserts the institution and its accounts exactly as captured, original ids included.
    /// False when the window has already settled, or when the bank is already back (the same id, or the
    /// same Plaid Item restored from the vault meanwhile) — in which case nothing is duplicated.</summary>
    public Task<bool> UndoDisconnectAsync(DisconnectedInstitution disconnected, CancellationToken cancellationToken) =>
        disconnected.TrySettleAsync(DisconnectedInstitution.Outcome.Undone, async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var snapshot = disconnected.Snapshot;

            var alreadyBack = await db.Institutions.AnyAsync(
                i => i.Id == snapshot.Id
                     || (snapshot.ItemId != string.Empty && i.ItemId == snapshot.ItemId),
                cancellationToken);
            if (alreadyBack)
                return false;

            db.Institutions.Add(snapshot); // the graph: the institution and each of its accounts
            await db.SaveChangesAsync(cancellationToken);

            // A bank connected while the window was open took the next free SortOrder, which may be the
            // restored bank's own. Renumber only when that collision exists, so a clean restore touches
            // no other row.
            var all = await db.Institutions.ToListAsync(cancellationToken);
            if (all.Select(i => i.SortOrder).Distinct().Count() != all.Count)
            {
                var order = 0;
                foreach (var institution in all.OrderBy(i => i.SortOrder).ThenBy(i => i.CreatedAtUtc))
                    institution.SortOrder = order++;
                await db.SaveChangesAsync(cancellationToken);
            }

            return true;
        });

    /// <summary>The undo window closed without an undo: release the Item at Plaid. Skipped for a bank with
    /// no access token (hand-entered). True only when Plaid was asked and accepted.</summary>
    public async Task<bool> CommitDisconnectAsync(
        DisconnectedInstitution disconnected, IPlaidClient plaid, ILogger logger, CancellationToken cancellationToken)
    {
        var released = false;
        await disconnected.TrySettleAsync(DisconnectedInstitution.Outcome.Released, async () =>
        {
            released = await PlaidItemRelease.ReleaseAsync(
                plaid, disconnected.Snapshot.AccessToken, disconnected.Name, logger, cancellationToken);
            return true;
        });
        return released;
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
