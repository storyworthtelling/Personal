namespace Index.Domain.Household;

/// <summary>Where the resolved role is remembered between launches (local settings on the device).</summary>
public interface IInstallRoleCache
{
    /// <summary>The remembered role, or null when none has been settled yet.</summary>
    InstallRole? Get();

    void Set(InstallRole role);

    void Clear();
}

/// <summary>What the household file says about who published it.</summary>
public enum HouseholdFileKind
{
    /// <summary>The file could not be read at all (offline, Drive refused, wrong passphrase, damaged).</summary>
    Unreadable,

    /// <summary>No household file exists yet — the owner's install has never published.</summary>
    Absent,

    /// <summary>The file was opened; <see cref="HouseholdFilePublisher.Email"/> names its publisher.</summary>
    Published,
}

public sealed record HouseholdFilePublisher(HouseholdFileKind Kind, string? Email = null, string? Detail = null)
{
    public static HouseholdFilePublisher Absent { get; } = new(HouseholdFileKind.Absent);

    public static HouseholdFilePublisher Unreadable(string detail) => new(HouseholdFileKind.Unreadable, null, detail);

    public static HouseholdFilePublisher PublishedBy(string? email) => new(HouseholdFileKind.Published, email);
}

/// <summary>The seam over the Drive read, so the decision above it can be tested without Drive.</summary>
public interface IHouseholdPublisherLookup
{
    /// <summary>Reads the household file and reports its publisher. Never throws: a failure is
    /// <see cref="HouseholdFileKind.Unreadable"/> with its reason in <c>Detail</c>.</summary>
    Task<HouseholdFilePublisher> LookAsync(string passphrase, CancellationToken cancellationToken);
}

/// <summary>
/// Which install this is, settled ONCE and remembered (Phase 1.1 of the remediation plan).
///
/// <para>The role is still derived from the household file and the signed-in account, never chosen
/// (R2): a file published by a different account means Hers. What changed is who asks and when. Five
/// pages used to re-read Drive on every appearance to find out, and nothing at launch asked at all, so
/// the work that must never run on her phone — restoring the owner's Plaid credentials, scheduling the
/// refresh, publishing over the owner's file — ran before any page could say who she was.</para>
///
/// <para>Only a definitive answer is remembered. "No passphrase yet", "no file yet" and "could not
/// read it" all answer Owner (the safe default <see cref="InstallRoleResolver.Resolve"/> has always
/// given) but are NOT cached: remembering Owner on her phone because the owner had not published yet
/// would lock the wrong answer in. Once Hers is remembered it stays until the signed-in account or the
/// passphrase changes (<see cref="Invalidate"/>), so a flaky connection can never flip it back.</para>
/// </summary>
public sealed class InstallRoleService
{
    private readonly IInstallRoleCache cache;
    private readonly IHouseholdPublisherLookup lookup;
    private readonly Func<string?> signedInAccount;
    private readonly Func<string?> passphrase;

    public InstallRoleService(
        IInstallRoleCache cache, IHouseholdPublisherLookup lookup,
        Func<string?> signedInAccount, Func<string?> passphrase)
    {
        this.cache = cache;
        this.lookup = lookup;
        this.signedInAccount = signedInAccount;
        this.passphrase = passphrase;
    }

    /// <summary>Why the last attempt could not settle the role, for the diagnostics log; null when it did.</summary>
    public string? UnresolvedReason { get; private set; }

    /// <summary>The remembered role, or Owner while nothing is settled. No I/O.</summary>
    public InstallRole Current => cache.Get() ?? InstallRole.Owner;

    public bool IsResolved => cache.Get() is not null;

    /// <summary>The remembered role without any I/O, or — only while nothing is settled — one attempt
    /// to settle it from the household file.</summary>
    public async Task<InstallRole> ResolveAsync(CancellationToken cancellationToken)
    {
        if (cache.Get() is { } remembered)
        {
            return remembered;
        }

        var account = signedInAccount();
        var secret = passphrase();
        if (string.IsNullOrWhiteSpace(account))
        {
            UnresolvedReason = "No Google account signed in yet.";
            return InstallRole.Owner;
        }

        if (string.IsNullOrEmpty(secret))
        {
            UnresolvedReason = "No vault passphrase set yet.";
            return InstallRole.Owner;
        }

        var found = await lookup.LookAsync(secret, cancellationToken);
        var proposed = found.Kind == HouseholdFileKind.Published
            ? InstallRoleResolver.Propose(account, found.Email)
            : null;

        if (proposed is null)
        {
            UnresolvedReason = found.Kind switch
            {
                HouseholdFileKind.Absent => "No household file has been published yet.",
                HouseholdFileKind.Unreadable => found.Detail ?? "The household file could not be read.",
                _ => "The household file does not name a publisher.",
            };
            return InstallRole.Owner;
        }

        UnresolvedReason = null;
        cache.Set(proposed.Value);
        return proposed.Value;
    }

    /// <summary>Forgets the remembered role. Called when the signed-in account or the passphrase
    /// changes — the two inputs the answer depends on.</summary>
    public void Invalidate() => cache.Clear();
}

/// <summary>
/// What a launch may do, decided from the role alone. Kept apart from the Android head so it can be
/// tested: the head cannot be loaded by the desktop test project, and "her install does not do this" is
/// exactly the kind of rule that must not live only inside an Activity.
///
/// <para>Hers never talks to Plaid (D09). So she never restores the owner's Plaid credentials and bank
/// tokens from the vault onto the device, never schedules the daily refresh or the worksheet delivery,
/// and never kicks an immediate refresh. The owner's launch is unchanged.</para>
/// </summary>
public sealed record LaunchPlan(
    bool RestoreFromVault, bool ScheduleRefresh, bool RunRefreshNow, bool ScheduleWorksheet)
{
    public static LaunchPlan For(InstallRole role) => InstallRoleGate.MayRunPlaidWork(role)
        ? new LaunchPlan(true, true, true, true)
        : new LaunchPlan(false, false, false, false);
}

public static class InstallRoleGate
{
    /// <summary>Whether this install may use Plaid, or anything that needs the owner's Plaid credentials
    /// (restoring them, the refresh worker, the worksheet worker). Hers never may.</summary>
    public static bool MayRunPlaidWork(InstallRole role) => role != InstallRole.Hers;
}

/// <summary>Who may take over the household file.</summary>
public static class HouseholdFileGate
{
    /// <summary>Whether this account may publish over the file as it stands. A file published by a
    /// different account is never taken over: that is what would flip the owner's install to
    /// read-only. No file yet, or one this account published, may be written.</summary>
    public static bool MayPublishOver(string? signedInAccountEmail, string? existingPublisherEmail)
    {
        if (string.IsNullOrWhiteSpace(existingPublisherEmail))
        {
            return true;
        }

        return string.Equals(
            signedInAccountEmail?.Trim(), existingPublisherEmail.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
