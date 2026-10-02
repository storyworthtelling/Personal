using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;

namespace Foreword.Android.Pages;

/// <summary>
/// The columns this account prints: their names, their order, and which of them belong to somebody
/// with a Google account of their own.
///
/// <para>This is where Foreword sets up the columns it needs. They used to be the household's
/// people written into the app, which put real names in a public repository and fixed the page to
/// one family. They are now the account's own choices, stored with the rest of the calendar settings
/// and backed up with them.</para>
///
/// <para>A name is also how every rule refers to its column, so renaming here renames it in every
/// rule, and removing a column removes its rules — through <see cref="ColumnEdits"/>, which is where
/// that is tested. The calendars that fed a removed column come back as unanswered.</para>
/// </summary>
public partial class CalendarColumnsPage : BinderPage
{
    private readonly CalendarSettings settings = new();

    // What the last removal took, as the two stored documents before it, so it can be put back.
    private (string Mapping, string Work)? removed;

    public CalendarColumnsPage()
    {
        InitializeComponent();
        Wrap(ForewordArea.Calendar, "Columns");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        settings.Reload();
        Render();
    }

    private void Render()
    {
        Columns.Clear();

        var layout = settings.Layout;
        for (var i = 0; i < layout.People.Count; i++)
        {
            if (i > 0)
            {
                Columns.Add(Rule());
            }

            Columns.Add(ColumnRow(layout.People[i], i, layout.People.Count));
        }
    }

    private View ColumnRow(PersonColumn column, int index, int count)
    {
        // The kit's implicit Entry style; a Label style on an Entry is a TargetType mismatch.
        var name = new Entry
        {
            Text = column.Name,
            Placeholder = "Their name",
        };

        // On unfocus rather than on change: a rename rewrites every rule that names the column, and
        // half a name is not one.
        void Commit()
        {
            var typed = (name.Text ?? string.Empty).Trim();
            if (typed == column.Name)
            {
                return;
            }

            if (!settings.RenameColumn(column, typed))
            {
                Say(typed.Length == 0
                    ? "A column needs a name."
                    : $"There is already a column called {typed}.");
                name.Text = column.Name;
                return;
            }

            Say(string.Empty);
            Render();
        }

        name.Unfocused += (_, _) => Commit();
        name.Completed += (_, _) => Commit();

        var account = new PaperSegmented(["Own Google account", "No account"],
            settings.Layout.HasAccount(column) ? "Own Google account" : "No account");
        account.Picked += picked =>
        {
            settings.SetHasAccount(column, picked == "Own Google account");
            Render();
        };

        var actions = new HorizontalStackLayout
        {
            Spacing = DesignTokens.Space.X2,
            Children =
            {
                Quiet("Up", index > 0, () => MoveBy(column, -1)),
                Quiet("Down", index < count - 1, () => MoveBy(column, 1)),
                Quiet("Remove", count > 1, () => RemoveWithUndo(column)),
            },
        };

        return new VerticalStackLayout
        {
            Padding = new Thickness(16, 12),
            Spacing = DesignTokens.Space.X2,
            Children = { name, account, actions },
        };
    }

    private void MoveBy(PersonColumn column, int delta)
    {
        if (settings.MoveColumn(column, delta))
        {
            Render();
        }
    }

    private void RemoveWithUndo(PersonColumn column)
    {
        var before = (CalendarMappingCodec.Write(settings.Mapping), CalendarMappingCodec.Write(settings.Work));
        if (!settings.RemoveColumn(column))
        {
            return;
        }

        removed = before;
        Say($"Removed {column.Name}. Its calendars will ask where to print again.");
        UndoButton.IsVisible = true;
        Render();
    }

    private void OnUndoClicked(object sender, EventArgs e)
    {
        if (removed is not { } snapshot)
        {
            return;
        }

        CredentialStore.CalendarMappingJson = snapshot.Mapping;
        CredentialStore.WorkScheduleJson = snapshot.Work;
        removed = null;
        UndoButton.IsVisible = false;
        Say(string.Empty);

        settings.Reload();
        Render();
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var typed = await DisplayPromptAsync("Add a column", "Whose column is it?", "Add", "Cancel");
        if (typed is null)
        {
            return;
        }

        // Somebody new is assumed to have no account until they are marked as having one: a wrong
        // "no" only means an invitation does not route them, where a wrong "yes" offers an address
        // field that can never match.
        if (!settings.AddColumn(typed, hasAccount: false))
        {
            Say(string.IsNullOrWhiteSpace(typed)
                ? "A column needs a name."
                : $"There is already a column called {typed.Trim()}.");
            return;
        }

        Say(string.Empty);
        Render();
    }

    private void Say(string text)
    {
        StatusLabel.Text = text;
        StatusLabel.IsVisible = text.Length > 0;
    }

    private static Button Quiet(string text, bool enabled, Action tapped)
    {
        var button = new Button
        {
            Text = text,
            Style = PaperResources.Token<Style>("SecondaryButton"),
            IsEnabled = enabled,
            VerticalOptions = LayoutOptions.Center,
        };

        button.Clicked += (_, _) => tapped();
        return button;
    }

    private static View Rule() => new BoxView
    {
        HeightRequest = DesignTokens.Shape.HairlineDp,
        Color = PaperResources.Colour("Hairline"),
    };
}
