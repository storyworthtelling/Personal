using Index.Domain.Model;

namespace Index.Domain.Household;

/// <summary>Which of mockup 14's first-run screens Accounts shows when it has no bank to draw.</summary>
public enum FirstRunScreen
{
    /// <summary>State 1: nothing connected, nothing in the vault. The one screen that offers Add.</summary>
    NothingConnected,

    /// <summary>State 2: the vault file exists and no passphrase is set. The only action is the passphrase.</summary>
    VaultHoldsBanks,

    /// <summary>State 3 before any bank has landed: a restore is running and will fill the page in.</summary>
    Restoring,
}

/// <summary>
/// The pure half of first run (D107, D158; guard G26 and R-G22). Kept apart from the Android head so
/// "Add is never offered while the vault's banks are on their way back" is a tested rule rather than a
/// property of one page's layout code.
/// </summary>
public static class FirstRunPlan
{
    /// <summary>
    /// A running restore outranks everything: the banks are coming back, so neither "nothing connected"
    /// nor "enter the passphrase" is true. Otherwise the vault only counts as holding banks while no
    /// passphrase is set (it cannot be read, and adding a bank by hand would double-link what it holds).
    /// </summary>
    public static FirstRunScreen Decide(bool passphraseSet, bool vaultHoldsSomething, bool restoreRunning)
    {
        if (restoreRunning)
        {
            return FirstRunScreen.Restoring;
        }

        return !passphraseSet && vaultHoldsSomething ? FirstRunScreen.VaultHoldsBanks : FirstRunScreen.NothingConnected;
    }

    /// <summary>Adding a bank from scratch is offered only when nothing is connected and nothing is coming
    /// back: anywhere else it would double-link a bank the vault already holds and spend a Plaid Trial
    /// Item (D149).</summary>
    public static bool OffersAdd(FirstRunScreen screen) => screen == FirstRunScreen.NothingConnected;
}

/// <summary>
/// State 3 (mockup 14): banks that exist but have not answered their first refresh. "Loading is a status
/// sentence, not a spinner" (§7), the page fills in place as each answers, and a bank that has not (or
/// cannot) answer keeps its place with a plain line instead of vanishing.
/// </summary>
public sealed record RestoreProgress(int Total, int Answered)
{
    /// <summary>The longest the page keeps asking whether more banks have answered. The worker's own
    /// cycle is capped at nine minutes, so ten covers one full cycle with a margin; after that the page
    /// stops asking and the next visit reads once more.</summary>
    public const int MaxPollMinutes = 10;

    public bool Settling => Answered < Total;

    /// <summary>"Bringing back 6 banks · 2 so far" while any bank has not answered; null once all have
    /// (the page then shows the ordinary last-refresh summary).</summary>
    public string? StatusLine => Settling
        ? $"Bringing back {Total} bank{(Total == 1 ? string.Empty : "s")} · {Answered} so far"
        : null;

    /// <summary>A bank has answered once it has refreshed, or has failed (a failure is an answer; it
    /// waits behind the dog-ear).</summary>
    public static RestoreProgress Of(IReadOnlyList<Institution> institutions)
    {
        var still = institutions.Count(i => i.LastRefreshedUtc is null && i.RefreshStatus != WorkStatus.Failed);
        return new RestoreProgress(institutions.Count, institutions.Count - still);
    }

    /// <summary>A Plaid-linked bank with no account rows yet: restored or just connected, and its first
    /// refresh has not produced a figure. It is drawn with a plain line rather than skipped (D124 only
    /// hides a bank whose accounts are all settled). A hand-entered bank is never in this state.</summary>
    public static bool AwaitsFirstFigures(Institution institution) =>
        !string.IsNullOrEmpty(institution.AccessToken) && institution.Accounts.Count == 0;

    /// <summary>The plain line a bank without figures shows under its heading.</summary>
    public static string PendingLine(Institution institution) =>
        institution.RefreshStatus == WorkStatus.Failed ? "No balances yet." : "Waiting for its balances.";

    /// <summary>Whether the pinned Add button is offered while banks exist. Not while a restore has
    /// brought banks back and none has a card to show yet: the page would otherwise sit under a status
    /// line counting banks with an Add button beside it.</summary>
    public static bool OffersAdd(IReadOnlyList<Institution> institutions)
    {
        if (!Of(institutions).Settling)
        {
            return true;
        }

        return HasCardsToShow(institutions);
    }

    /// <summary>Whether any account would be drawn as a card (D124: a settled card or loan is not).</summary>
    public static bool HasCardsToShow(IReadOnlyList<Institution> institutions) =>
        institutions.Any(i => i.Accounts.Any(a => !AccountStatus.IsPaidOff(a)));

    /// <summary>Changes exactly when something the page draws could have changed, so a poll can skip a
    /// re-render (which would destroy a field being typed in, R-G15) when nothing has.</summary>
    public static string Signature(IReadOnlyList<Institution> institutions, bool restoring) =>
        string.Join(
            "|",
            institutions.Select(i =>
                $"{i.Id}:{i.RefreshStatus}:{i.LastRefreshedUtc?.UtcTicks}:{i.Accounts.Count}"))
        + (restoring ? "#restoring" : string.Empty);
}
