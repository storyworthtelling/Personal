using Folio.Domain.Report;
using Folio.Domain.Series;
using SkiaSharp;

namespace Folio.Infrastructure.Imaging;

/// <summary>
/// The series bible printed as its own document (release G, R26), for the tablet and Drive. It wears the
/// frame every tablet document wears: a tab for each kind of entry that prints, linked to where it
/// starts. It opens with the books, in the order they were written, and where each sits in story time;
/// then every entry with what each book says of it, in that book's own words, and the author's
/// correction, which wins over the books.
/// </summary>
public static class SeriesBibleDocument
{
    private static readonly (BibleKind Kind, ReportSection Section)[] Parts =
    [
        (BibleKind.Character, new ReportSection("Characters", "Characters", "bible-characters")),
        (BibleKind.Setting, new ReportSection("Settings", "Settings", "bible-settings")),
        (BibleKind.Rule, new ReportSection("Rules", "World rules", "bible-rules")),
        (BibleKind.Event, new ReportSection("Timeline", "Timeline events", "bible-timeline")),
        (BibleKind.Thread, new ReportSection("Threads", "Open threads", "bible-threads")),
    ];

    /// <summary>The document, or null for a bible with nothing in it: there is nothing to send.</summary>
    public static byte[]? RenderOrNull(
        string seriesName, SeriesBible bible, IReadOnlyDictionary<Guid, StoryPlacement> placements) =>
        bible.Entries.Count == 0 ? null : Render(seriesName, bible, placements);

    public static byte[] Render(string seriesName, SeriesBible bible, IReadOnlyDictionary<Guid, StoryPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(bible);
        var name = string.IsNullOrWhiteSpace(seriesName) ? "The series" : seriesName.Trim();
        var present = Parts.Where(part => bible.Entries.Any(entry => entry.Kind == part.Kind)).ToList();

        using var title = ReportType.Title();
        using var section = ReportType.Section();
        using var heading = ReportType.Heading();
        using var body = ReportType.Body();
        using var bodyBold = ReportType.BodyBold();
        using var bodyItalic = ReportType.BodyItalic();
        using var writer = new PageWriter($"{name} · series bible", [.. present.Select(part => part.Section)]);

        writer.Heading(name, title, ReportPalette.Ink, ruled: true);
        writer.Paragraph(
            "The series bible: what the books have established, kept for continuity. The earliest-written book is canon, "
            + "and the author's corrections win over every book.", bodyItalic, ReportPalette.InkSoft);

        writer.Heading("The books", heading, ReportPalette.Ink);
        foreach (var book in bible.Books)
        {
            var where = placements.TryGetValue(book.BookId, out var placement) ? $" — {placement.Line}" : string.Empty;
            writer.Bullet($"{book.Title}{where}", body, ReportPalette.Ink);
        }

        foreach (var (kind, part) in present)
        {
            writer.NewPage();
            writer.BeginSection(part);
            writer.Heading(part.Name, section, ReportPalette.Ink, ruled: true);
            foreach (var entry in bible.Entries.Where(entry => entry.Kind == kind))
                Entry(writer, entry, heading, body, bodyBold, bodyItalic);
        }

        return writer.Finish();
    }

    private static void Entry(PageWriter writer, BibleEntry entry, SKFont heading, SKFont body, SKFont bodyBold, SKFont bodyItalic)
    {
        writer.KeepTogether(heading.Size * 4);
        writer.Heading(entry.Name, heading, ReportPalette.Ink);
        if (entry.Aliases.Count > 0)
            writer.Paragraph("Also " + string.Join(", ", entry.Aliases), body, ReportPalette.InkSoft);

        foreach (var state in entry.States)
        {
            var when = state.Era.Length > 0 ? $" ({state.Era})" : string.Empty;
            writer.Paragraph($"{state.Book}{when}", bodyBold, ReportPalette.Ink);
            writer.Paragraph(state.Text, body, ReportPalette.Ink);
            if (state.Quote.Length > 0)
                writer.Paragraph($"“{state.Quote}”", bodyItalic, ReportPalette.InkSoft);
        }

        if (entry.Correction.Length > 0)
        {
            writer.Paragraph("The author's correction", bodyBold, ReportPalette.Ink);
            writer.Paragraph(entry.Correction, body, ReportPalette.Ink);
        }
        writer.Gap(PageGeometry.BlockGap);
    }
}
