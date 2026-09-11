using Microsoft.Extensions.DependencyInjection;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Tasks;
using Foreword.Infrastructure.Tasks;
using Storyworthtelling.Maui;

namespace Foreword.Android.Pages;

/// <summary>
/// T4. The parsed rule, shown back before it is saved.
///
/// <para>Nothing is stored until the owner accepts, which is the whole reason this screen exists: a
/// misparse can never quietly schedule the wrong thing. The fields are rendered by the same
/// <see cref="TaskSummary"/> the edit form uses, so what they accept here and what they find later
/// cannot be two different descriptions of one rule.</para>
/// </summary>
public partial class TaskReadBackPage : ContentPage
{
    private readonly ForewordTask read;

    public TaskReadBackPage(string written, ForewordTask read)
    {
        InitializeComponent();
        this.read = read;

        WrittenLabel.Text = $"“{written}”";
        RenderFields();
    }

    private void RenderFields()
    {
        Fields.Clear();

        // The title is the first thing to check, and it is the one field the model rewrote rather
        // than extracted — "Mow the lawn" out of a sentence about Saturdays and dry hours.
        AddRow("Task", read.Title, first: true);

        foreach (var row in TaskSummary.Rows(read))
        {
            AddRow(row.Label, row.Value, first: false);
        }
    }

    private void AddRow(string label, string value, bool first)
    {
        if (!first)
        {
            Fields.Add(new BoxView
            {
                HeightRequest = DesignTokens.Shape.HairlineDp,
                Color = PaperResources.Colour("Hairline"),
            });
        }

        Fields.Add(new Grid
        {
            Padding = new Thickness(16, 12),
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = DesignTokens.Space.X3,
            MinimumHeightRequest = DesignTokens.Shape.ListRowMinHeightDp,
            Children =
            {
                new Label
                {
                    Text = label,
                    Style = PaperResources.Token<Style>("CardRowDescription"),
                    VerticalOptions = LayoutOptions.Center,
                },
                Value(value),
            },
        });
    }

    private static View Value(string value)
    {
        var label = new Label
        {
            Text = value,
            Style = PaperResources.Token<Style>("CardRowTitle"),
            HorizontalTextAlignment = TextAlignment.End,
            VerticalOptions = LayoutOptions.Center,
        };

        Grid.SetColumn(label, 1);
        return label;
    }

    private async void OnAcceptClicked(object sender, EventArgs e)
    {
        var editor = IPlatformApplication.Current!.Services.GetRequiredService<TaskEditor>();
        var result = await editor.Save(read);

        if (!result.Saved)
        {
            // Said out loud rather than swallowed: a task the owner believes is in Google and is not
            // is worse than one that plainly failed to save.
            await DisplayAlert("Saved here only", result.Message, "OK");
        }

        // Back past the add screen, so Accept lands on the list rather than on an empty form.
        Navigation.RemovePage(Navigation.NavigationStack[^2]);
        await Navigation.PopAsync();
    }

    private async void OnEditClicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new TaskEditPage(read, unsaved: true));
}
