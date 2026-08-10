using Storyboarder.Domain.Analysis;

namespace Storyboarder.Android.Pages;

// Shows what the last revision moved: the report findings it resolved and newly raised. Built from the
// DraftDiff already loaded via ProjectService. (Before/after scene-image pairs are the Phase 2 draft-loop
// investment; this carries the finding diff.)
public partial class DraftDiffPage : ContentPage
{
    public DraftDiffPage(string title, int draftNumber, DraftDiff diff)
    {
        InitializeComponent();
        Build(title, draftNumber, diff);
    }

    private void Build(string title, int draftNumber, DraftDiff diff)
    {
        Heading($"{title} — draft {draftNumber} vs {draftNumber - 1}", isDisplay: true);

        var resolved = diff.ResolvedFindings.Count;
        var raised = diff.NewFindings.Count;
        Paragraph($"{resolved} finding(s) resolved · {raised} newly raised");

        Findings("Findings resolved", "✓ ", diff.ResolvedFindings);
        Findings("New findings", "• ", diff.NewFindings);

        if (resolved == 0 && raised == 0)
            Paragraph("No findings changed between these drafts.");
    }

    private void Findings(string title, string bullet, IReadOnlyList<DiffFinding> findings)
    {
        if (findings.Count == 0)
            return;
        Heading(title);
        foreach (var f in findings)
            Paragraph($"{bullet}[{f.Category}] {f.Detail}");
    }

    private void Heading(string text, bool isDisplay = false) =>
        Content.Add(new Label
        {
            Text = text,
            Style = (Style)Application.Current!.Resources[isDisplay ? "DisplayLabel" : "TitleLabel"],
            Margin = new Thickness(0, isDisplay ? 0 : 12, 0, 0)
        });

    private void Paragraph(string text) =>
        Content.Add(new Label { Text = text, Style = (Style)Application.Current!.Resources["StatusLine"] });
}
