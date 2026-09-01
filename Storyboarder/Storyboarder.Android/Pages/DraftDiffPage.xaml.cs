using Storyboarder.Domain.Analysis;

namespace Storyboarder.Android.Pages;

// Shows what the last revision moved: the report findings it resolved, newly raised, and regressed (matched
// across drafts by finding identity), and the scenes that changed — each with its before/after evidence
// image so the author can see how the illustration shifted (the comprehension probe). Built from the
// DraftDiff already loaded via ProjectService.
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

        Paragraph(
            $"{diff.ResolvedFindings.Count} resolved · {diff.NewFindings.Count} new · " +
            $"{diff.RegressedFindings.Count} regressed · {diff.ChangedScenes.Count} scene(s) re-illustrated");

        Findings("Findings resolved", "✓ ", diff.ResolvedFindings);
        Findings("New findings", "• ", diff.NewFindings);
        Findings("Regressed findings", "↩ ", diff.RegressedFindings);

        if (diff.ChangedScenes.Count > 0)
        {
            Heading("Changed scenes");
            foreach (var scene in diff.ChangedScenes)
                Content.Add(ScenePair(scene));
        }

        var nothingChanged = diff.ResolvedFindings.Count == 0 && diff.NewFindings.Count == 0
            && diff.RegressedFindings.Count == 0 && diff.ChangedScenes.Count == 0;
        if (nothingChanged)
            Paragraph("Nothing changed between these drafts.");
    }

    // A card pairing the previous draft's evidence image (left) with the new one (right) for one changed scene.
    private View ScenePair(DiffScenePair scene)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8
        };
        grid.Add(Side("Before", scene.PreviousImage), 0);
        grid.Add(Side("After", scene.NewImage), 1);

        var caption = new Label
        {
            Text = scene.Citation,
            Style = (Style)Application.Current!.Resources["SecondaryLabel"]
        };

        return new Border
        {
            Style = (Style)Application.Current!.Resources["HairlineCard"],
            Margin = new Thickness(0, 6, 0, 0),
            Content = new VerticalStackLayout { Spacing = 4, Children = { caption, grid } }
        };
    }

    // One side of a before/after pair: the evidence image, or a note when that draft had none (a new scene
    // has no "before"; an unavailable render has no image).
    private View Side(string label, byte[]? image)
    {
        var heading = new Label
        {
            Text = label,
            Style = (Style)Application.Current!.Resources["FieldLabel"]
        };

        View body = image is not null
            ? new Border
            {
                Stroke = (Color)Application.Current!.Resources["Hairline"],
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Padding = 0,
                Content = new Image { Source = ImageSource.FromStream(() => new MemoryStream(image)), Aspect = Aspect.AspectFit }
            }
            : new Label
            {
                Text = "(no image)",
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
