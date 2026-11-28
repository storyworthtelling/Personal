using Watch.Domain.Kit;

namespace Watch.Domain.Dash;

/// <summary>One mail line as the wrist draws it (mock: Mail): its subject, who it is from, and when, as the watch says it.</summary>
public sealed record MailRow(string Id, string Subject, string From, string When);

/// <summary>
/// Mail lines (W-5.1.6, A11; P14 B7): subject and sender only, people only (Post sends only mail its own sorting judged
/// <c>Human</c>, B1), newest first whatever order they crossed in, ties in Post's order. They are only read: no mail is
/// opened on the wrist, none buzzes (the haptic plan's <c>Mail</c> is silent), and none is marked unread (WD §1, the
/// owner's ruling). A mail with no subject says so.
/// </summary>
public static class MailLines
{
    public static IReadOnlyList<MailRow> Rows(DashMail mail, DateTimeOffset now, TimeZoneInfo zone) =>
        mail.Mail
            .Select((line, index) => (Line: line, Index: index))
            .OrderByDescending(entry => entry.Line.At)
            .ThenBy(entry => entry.Index)
            .Select(entry => new MailRow(
                entry.Line.Id,
                string.IsNullOrWhiteSpace(entry.Line.Subject) ? DashWords.NoSubject : entry.Line.Subject,
                entry.Line.From,
                WhenWords.Relative(entry.Line.At, now, zone)))
            .ToList();
}
