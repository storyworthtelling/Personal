using Folio.Domain.Analysis;

using Storyworthtelling.Maui;

namespace Folio.Android.Pages;

// Browses the revision report in-app — the same content as the analysis PDF, laid out as scrollable
// sections so the author can read it without opening a viewer. Built from the folded BookAnalysisResult.
public partial class ReportPage : FolioBinderPage
{
    public ReportPage(string title, BookAnalysisResult report)
    {
        InitializeComponent();
        Wrap(FolioArea.Shelf, "Revision report");
        Build(title, report);
    }

    private void Build(string title, BookAnalysisResult report)
    {
        Heading(title, isDisplay: true);

        var frame = new List<string>();
        if (!string.IsNullOrWhiteSpace(report.Genre))
            frame.Add(report.Genre);
        // Prefer the expansion read's length note (how far short the draft runs) over the plain count.
        var lengthNote = report.Expansion.Length.Note;
        if (!string.IsNullOrWhiteSpace(lengthNote))
            frame.Add(lengthNote);
        else if (report.WordCount > 0)
            frame.Add($"{report.WordCount:N0} words{(string.IsNullOrWhiteSpace(report.WordCountRange) ? "" : $" (genre norm {report.WordCountRange})")}");
        if (frame.Count > 0)
            Paragraph(string.Join(" · ", frame));

        if (!string.IsNullOrWhiteSpace(report.Logline))
            Paragraph(report.Logline);
        if (!string.IsNullOrWhiteSpace(report.Synopsis))
        {
            Heading("Synopsis");
            Paragraph(report.Synopsis);
        }

        Section("Characters", report.Characters,
            c => $"{c.Name} — {c.Role}", c => c.ArcSummary);
        Bullets("Themes", report.Themes);

        Findings("Plot holes", report.PlotHoles);
        Findings("Inconsistencies", report.Inconsistencies);
        Findings("Inaccuracies", report.Inaccuracies);
        Findings("Pacing", report.PacingNotes);

        Section("Revision plan", report.RevisionPlan,
            r => $"[{r.Priority}] {Chapter(r.Chapter)}{r.Issue}", r => r.SuggestedFix);

        Section("Reviews", report.Reviews,
            r => r.Perspective, r => r.Assessment);

        // The additive engine: scenes to add + show-vs-tell (both ends). Pointers only, never drafted prose.
        Section("Scenes to add", report.Expansion.ScenesToAdd,
            a => $"[{a.Priority}] {KindLabel(a.Kind)}{(string.IsNullOrWhiteSpace(a.Location) ? "" : $" · {a.Location}")}: {a.Missing}",
            a => a.Why);
        Section("Show vs tell", report.Expansion.ShowVsTell,
            n => $"[{(n.Direction == ShowTellDirection.Dramatize ? "dramatize" : "tighten")}] {Chapter(n.Chapter)}“{n.Excerpt}”",
            n => n.Suggestion);

        Section("Line edits", report.LineEdits,
            l => $"{Chapter(l.Chapter)}“{l.Excerpt}”", l => $"{l.Suggestion} — {l.Reason}");
        Section("Prose & voice", report.ProseNotes,
            p => $"{Chapter(p.Chapter)}{p.Pattern}", p => $"{p.Evidence} — {p.Fix}");
        Section("Dialogue", report.DialogueNotes,
            d => $"{Chapter(d.Chapter)}{d.Speakers}", d => $"{d.Observation} — {d.Fix}");
        Section("Scene by scene", report.SceneBreakdown,
            s => $"{Chapter(s.Chapter)}{s.Scene}", s => $"{s.Purpose} — {s.Verdict}");

        if (!string.IsNullOrWhiteSpace(report.VoiceConsistency.DominantVoice))
        {
            Heading("Authorial voice");
            Paragraph(report.VoiceConsistency.DominantVoice);
        }
        Section("Voice drift", report.VoiceConsistency.Divergences,
            v => $"{Chapter(v.Chapter)}“{v.Excerpt}”", v => $"{v.Divergence} — {v.Suggestion}");

        // The candid state-of-the-draft that closes the report.
        var verdict = report.Verdict;
        if (verdict.Strengths.Count > 0 || verdict.Problems.Count > 0 || !string.IsNullOrWhiteSpace(verdict.MostNeeds))
        {
            Bullets("Biggest strengths", verdict.Strengths);
            Bullets("Biggest problems", verdict.Problems);
            if (!string.IsNullOrWhiteSpace(verdict.MostNeeds))
            {
                Heading("What this draft most needs");
                Paragraph(verdict.MostNeeds);
            }
        }
    }

    // A section of items rendered as "primary" (bold) plus a muted "secondary" line each. Skipped when empty.
    private void Section<T>(string title, IReadOnlyList<T> items, Func<T, string> primary, Func<T, string> secondary)
    {
        if (items.Count == 0)
            return;
        Heading(title);
        foreach (var item in items)
        {
            Content.Add(new Label { Text = primary(item), Style = PaperResources.Token<Style>("HeadingLabel") });
            var detail = secondary(item);
            if (!string.IsNullOrWhiteSpace(detail))
                Paragraph(detail);
        }
    }

    private void Findings(string title, IReadOnlyList<ReportFinding> findings)
    {
        if (findings.Count == 0)
            return;
        Heading(title);
        foreach (var f in findings)
            Paragraph($"{Chapter(f.Chapter)}{f.Detail}");
    }

    private void Bullets(string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return;
        Heading(title);
        foreach (var item in items)
            Paragraph("• " + item);
    }

    private void Heading(string text, bool isDisplay = false) =>
        Content.Add(new Label
        {
            Text = text,
            Style = PaperResources.Token<Style>(isDisplay ? "DisplayLabel" : "TitleLabel"),
            Margin = new Thickness(0, isDisplay ? 0 : 12, 0, 0)
        });

    private void Paragraph(string text) =>
        Content.Add(new Label { Text = text, Style = PaperResources.Token<Style>("StatusLine") });

    private static string KindLabel(AdditionKind kind) => kind switch
    {
        AdditionKind.MissingBeat => "Missing beat",
        AdditionKind.CharacterDeepening => "Character",
        AdditionKind.TensionStakes => "Tension & stakes",
        AdditionKind.ThemeDeepening => "Theme",
        _ => "Add"
    };

    private static string Chapter(string chapter) =>
        string.IsNullOrWhiteSpace(chapter) ? string.Empty : $"[{chapter}] ";
}
