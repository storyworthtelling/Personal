using Codex.Domain.Design;
using Constitution.Domain.Notices;
using Constitution.Domain.Time;

namespace Constitution.Domain.Waiting;

/// <summary>One of the two big chips on a low-confidence read; picking it is the answer.</summary>
public sealed record ChipChoice(string Value);

/// <summary>What a pick means: the cause that was asked about and the value the owner chose.</summary>
public sealed record WaitingAnswer(string NoticeCause, string ChosenValue);

/// <summary>
/// One row of the Waiting page: what happened and what to do. <paramref name="Chips"/> is empty for
/// every row but a readable low-confidence read, and then <paramref name="Button"/> is null — a row
/// has a button or chips, never both, and never two buttons.
/// </summary>
public sealed record WaitingItem(
    NoticeKind Kind,
    string CauseKey,
    FoldRole Role,
    string Title,
    string Body,
    string DateText,
    WaitingButton? Button,
    IReadOnlyList<ChipChoice> Chips);

/// <summary>
/// The Waiting page (book p.18, C-9.1.2), decided with no view: a snapshot of the notice board as
/// rows, newest first, each with its role (the bar's colour), fixed plain copy, a relative date and
/// at most one button. Pure; time is passed in.
///
/// <para>Where a title would name a fact the app does not have yet (which card, how many in the
/// box), the wording is neutral and generic, not a guess: the mockup's "Apple crisp" and "3 of 10"
/// are invented data. Only the low-confidence read carries its facts, in its cause key.</para>
/// </summary>
public sealed class WaitingPageModel
{
    private WaitingPageModel(IReadOnlyList<WaitingItem> items) => Items = items;

    /// <summary>Everything waiting, newest first.</summary>
    public IReadOnlyList<WaitingItem> Items { get; }

    public static WaitingPageModel From(NoticeBoard board, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        var items = board.Waiting().Select(notice => ItemFor(notice, now)).ToList();
        return new WaitingPageModel(items);
    }

    /// <summary>
    /// A chip was picked. The pick is the answer: the notice is resolved and nothing further is
    /// asked. Nothing is stored yet — the read-back worker (Release C) will carry the answer on.
    /// </summary>
    public static WaitingAnswer Answer(NoticeBoard board, WaitingItem item, ChipChoice chosen)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(chosen);

        if (!item.Chips.Contains(chosen))
        {
            throw new ArgumentException(
                $"\"{chosen.Value}\" is not one of the chips on this row ({item.Chips.Count} offered).", nameof(chosen));
        }

        board.Resolve(item.CauseKey);
        return new WaitingAnswer(item.CauseKey, chosen.Value);
    }

    private static WaitingItem ItemFor(Notice notice, DateTimeOffset now)
    {
        var date = RelativeTime.Format(notice.CreatedAt.LocalDateTime, now.LocalDateTime);
        var (title, body, button, chips) = notice.Kind switch
        {
            NoticeKind.ReMarkableDelivery => (
                "The reMarkable didn’t take this morning’s workout",
                "Its sign-in was refused. Pair the tablet again; the new token goes to the vault and Constitution resends.",
                new WaitingButton("Pair again", WaitingActionId.PairAgain),
                NoChips),
            NoticeKind.HealthConnectRevoked => (
                "Health Connect was switched off",
                "Constitution can’t read your steps and sleep until it is allowed again. Allow it, and the charts pick up where they stopped.",
                new WaitingButton("Allow again", WaitingActionId.ReconnectHealth),
                NoChips),
            NoticeKind.BedOffline => (
                "The bed couldn’t be reached",
                "Its sign-in was refused, so the sleep line is waiting. Sign in again and Constitution picks it back up.",
                new WaitingButton("Sign in again", WaitingActionId.SignInAgain),
                NoChips),
            NoticeKind.LowConfidenceRead => LowConfidence(notice),
            NoticeKind.ScanToCheck => (
                "A scanned card is waiting to be checked",
                "Some words were hard to read.",
                new WaitingButton("Check it", WaitingActionId.CheckCard),
                NoChips),
            NoticeKind.BoxUnderTen => (
                "The recipe box is under ten",
                "Weekly meal sheets start after the tenth card.",
                new WaitingButton("Scan a card", WaitingActionId.ScanCard),
                NoChips),
            NoticeKind.KeepHandoff => (
                "This week’s groceries are ready",
                "Send the list to Keep to shop from it.",
                new WaitingButton("Send to Keep", WaitingActionId.SendToKeep),
                NoChips),
            _ => throw new ArgumentOutOfRangeException(
                nameof(notice), notice.Kind, "This notice kind has no Waiting copy; add it to WaitingPageModel."),
        };

        return new WaitingItem(notice.Kind, notice.CauseKey, notice.Role, title, body, date, button, chips);
    }

    private static readonly IReadOnlyList<ChipChoice> NoChips = [];

    private static (string, string, WaitingButton?, IReadOnlyList<ChipChoice>) LowConfidence(Notice notice)
    {
        var read = LowConfidenceCause.TryParse(notice.CauseKey);
        if (read is null)
        {
            // The cause named no candidates: say so plainly rather than offer chips with nothing on them.
            return (
                "A handwritten number was hard to read",
                "The page was hard to read, and the two possible values could not be shown.",
                null,
                NoChips);
        }

        return (
            $"Was it {read.First} or {read.Second} {read.Subject}?",
            "The assessment page was hard to read. Pick one and the plan starts from it.",
            null,
            [new ChipChoice(read.First), new ChipChoice(read.Second)]);
    }
}
