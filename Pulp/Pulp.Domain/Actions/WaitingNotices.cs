using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.HandOffs;

namespace Pulp.Domain.Actions;

/// <summary>
/// The colour role a notice's bar and fold carry, by name. Closed to these two on purpose: Pulp draws
/// no Caution (D275), so there is no member through which one could be handed out.
/// </summary>
public enum NoticeRole
{
    /// <summary>Something went wrong and wants doing.</summary>
    Problem,

    /// <summary>Only worth knowing; nothing is known to be wrong.</summary>
    Reference,
}

/// <summary>The notice's one button, if it has one (mockup 12: "at most one button").</summary>
public enum NoticeAction
{
    None,
    OpenTitle,
    OpenVault,
}

/// <summary>
/// One thing waiting behind the dog-ear: what happened and what to do next (§14), dated. <see cref="Key"/>
/// is its cause, so a notice stated again replaces itself and is withdrawn when its cause goes.
/// </summary>
public sealed record WaitingNotice(
    string Key, string Headline, string Detail, NoticeRole Role, string? ActionLabel, NoticeAction Action, DateTimeOffset At);

/// <summary>
/// What waits behind Pulp's dog-ear (D260, mockup 12): a hand-off that did not land, a key rejected, a
/// sports feed unreachable, a household file that would not open. Never voiced (§14), never about
/// another viewer, and never the lookup allowance (D261 — Diagnostics only). The hand-off details are
/// <see cref="HandOffNotice"/>'s sentences, not a second copy of them.
/// </summary>
public static class WaitingNotices
{
    public const string OpenVaultLabel = "Open Vault";

    /// <summary>Every hand-off notice about one title shares a key, so the next one — or a landing — replaces it.</summary>
    public static string HandOffKey(TitleId title) => "handoff:" + title.Value;

    /// <summary>
    /// After the landing check: a mis-landing is a Problem (D122); silence is said quietly and hedged
    /// (D286). A landing that worked, or one nothing can tell, leaves no notice — the caller withdraws
    /// the title's key instead.
    /// </summary>
    public static WaitingNotice? AfterLanding(Landing landing, string service, string titleName, TitleId title, DateTimeOffset at)
    {
        var name = Require(service, nameof(service));
        var titled = Require(titleName, nameof(titleName));
        return landing switch
        {
            Landing.LandedElsewhere => new WaitingNotice(
                HandOffKey(title), $"{name} opened, but not on {titled}", HandOffNotice.LandedElsewhere(name),
                NoticeRole.Problem, OpenTitleLabel(titled), NoticeAction.OpenTitle, at),
            Landing.NotSeen => new WaitingNotice(
                HandOffKey(title), $"{name} may not have opened on {titled}", HandOffNotice.NotSeen(name),
                NoticeRole.Reference, OpenTitleLabel(titled), NoticeAction.OpenTitle, at),
            Landing.Landed or Landing.Unknown => null,
            _ => throw new ArgumentOutOfRangeException(nameof(landing), landing, "A landing with no notice decided."),
        };
    }

    /// <summary>The gate refused the hand-off before anything was opened (G13).</summary>
    public static WaitingNotice Refused(StartRefusal refusal, string service, string titleName, TitleId title, DateTimeOffset at) =>
        Refused(HandOffNotice.ForRefusal(refusal, service), titleName, title, at);

    /// <summary>
    /// The same notice from the sentence the hand-off executor already wrote (its outcome carries the
    /// sentence, not the refusal), so the head never writes a headline of its own.
    /// </summary>
    public static WaitingNotice Refused(string sentence, string titleName, TitleId title, DateTimeOffset at)
    {
        var detail = Require(sentence, nameof(sentence));
        var titled = Require(titleName, nameof(titleName));
        return new WaitingNotice(
            HandOffKey(title), $"{titled} was not opened", detail, NoticeRole.Problem, null, NoticeAction.None, at);
    }

    public static WaitingNotice NotInstalled(string service, TitleId title, DateTimeOffset at)
    {
        var name = Require(service, nameof(service));
        return new WaitingNotice(
            HandOffKey(title), $"{name} is not installed", HandOffNotice.NotInstalled(name),
            NoticeRole.Problem, null, NoticeAction.None, at);
    }

    /// <summary>A key rejected by its service (credential health). <paramref name="keyName"/> is how the page names it.</summary>
    public static WaitingNotice KeyRejected(string credentialId, string keyName, DateTimeOffset at)
    {
        var id = Require(credentialId, nameof(credentialId));
        var name = Require(keyName, nameof(keyName));
        return new WaitingNotice(
            "key:" + id,
            $"The {name} key stopped working",
            "Lookups with it were refused, so what Pulp shows is from before then. Replace the key in Settings, under Vault.",
            NoticeRole.Problem, OpenVaultLabel, NoticeAction.OpenVault, at);
    }

    /// <summary>A league's feed could not be reached (D208); nothing to do, so no button.</summary>
    public static WaitingNotice SportsFeedUnreachable(string leagueId, string leagueName, DateTimeOffset at)
    {
        var id = Require(leagueId, nameof(leagueId));
        var name = Require(leagueName, nameof(leagueName));
        return new WaitingNotice(
            "sports:" + id,
            $"The {name} schedule could not be reached",
            $"Games for {name} are shown as they were the last time the feed answered.",
            NoticeRole.Reference, null, NoticeAction.None, at);
    }

    /// <summary>The household file would not open (D46). Shares its slot with the newer-version notice.</summary>
    public static WaitingNotice HouseholdUnreadable(DateTimeOffset at) => new(
        HouseholdKey,
        "The household file could not be opened",
        "Shared playlists and settings may be out of date on this phone. Check the passphrase in Settings, under Vault.",
        NoticeRole.Problem, OpenVaultLabel, NoticeAction.OpenVault, at);

    /// <summary>The household file was written by a newer Pulp (D46).</summary>
    public static WaitingNotice HouseholdFromNewerVersion(DateTimeOffset at) => new(
        HouseholdKey,
        "The household file is from a newer Pulp",
        "Another phone saved it with a newer version. Update Pulp on this phone to read it.",
        NoticeRole.Problem, null, NoticeAction.None, at);

    private const string HouseholdKey = "household";

    private static string OpenTitleLabel(string titleName) => "Open " + titleName;

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A notice names what it is about.", name)
            : value.Trim();
}
