using Storyboarder.Domain.Analysis;

namespace Storyboarder.Android.Pages;

// Shows what the last revision moved: the findings the report resolved and newly raised, and the panels
// whose source text changed — each paired old-vs-new so the author can see how the illustration shifted
// (the comprehension probe). Built from the DraftDiff already loaded via ProjectService.
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
        Paragraph($"{diff.ChangedPanels.Count} panel(s) re-illustrated · {diff.ReusablePanelCount} carried forward");

        Findings("Findings resolved", "✓ ", diff.ResolvedFindings);
        Findings("New findings", "• ", diff.NewFindings);

        if (diff.ChangedPanels.Count > 0)
        {
            Heading("Changed panels");
            foreach (var panel in diff.ChangedPanels)
                Content.Add(PanelComparison(panel));
        }
    }

    // A card pairing the previous draft's render (left) with the new one (right) for one changed panel.
    private View PanelComparison(PanelChange panel)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8
        };
        grid.Add(Side("Before", panel.PreviousImagePath), 0);
        grid.Add(Side("After", panel.NewImagePath), 1);

        var caption = new Label
        {
            Text = $"Volume {panel.VolumeNumber} · page {panel.PageNumber} · panel {panel.PanelNumber}",
            Style = (Style)Application.Current!.Resources["SecondaryLabel"]
        };

        return new Border
        {
            Style = (Style)Application.Current!.Resources["HairlineCard"],
            Margin = new Thickness(0, 6, 0, 0),
            Content = new VerticalStackLayout { Spacing = 4, Children = { caption, grid } }
        };
    }

    // One side of a before/after pair: the render, or a note when there's nothing to show (a brand-new
    // panel has no "before"; a panel not yet re-rendered has no "after").
    private View Side(string label, string? imagePath)
    {
        var heading = new Label
        {
            Text = label,
            Style = (Style)Application.Current!.Resources["FieldLabel"]
        };

        View body = imagePath is not null && System.IO.File.Exists(imagePath)
            ? new Border
            {
                Stroke = (Color)Application.Current!.Resources["Hairline"],
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Padding = 0,
                Content = new Image { Source = ImageSource.FromFile(imagePath), Aspect = Aspect.AspectFit }
            }
            : new Label
            {
                Text = label == "Before" ? "(new panel)" : "(not yet rendered)",
                Style = (Style)Application.Current!.Resources["StatusLine"]
            };

        return new VerticalStackLayout { Spacing = 4, Children = { heading, body } };
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
