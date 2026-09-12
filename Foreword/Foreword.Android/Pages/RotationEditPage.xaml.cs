using Foreword.Domain.Prayer;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;

namespace Foreword.Android.Pages;

/// <summary>
/// One standing list: its name, whether it is printing, and the names in the order they come round.
///
/// <para>Reordering is a <b>Move</b>, not a drag. A drag handle on a 48dp row is not a target a
/// thumb can hit, which is the same finding Plate's scan tray settled — and here the cost of
/// missing is reordering somebody's prayer list by accident.</para>
///
/// <para>No Save (D35): every change commits. A structural change — a name added, removed, moved,
/// the list paused — writes the archive there and then; the name field writes when it loses focus,
/// because saving to Drive on every keystroke would be a Drive round-trip per letter.</para>
/// </summary>
public partial class RotationEditPage : BinderPage
{
    private readonly PrayerSettings settings;

    private PrayerRotation rotation;

    private readonly bool isNew;

    public RotationEditPage(PrayerSettings settings, PrayerRotation? rotation)
    {
        this.settings = settings;
        isNew = rotation is null;

        this.rotation = rotation ?? new PrayerRotation
        {
            Id = Guid.NewGuid().ToString("n"),
            Name = string.Empty,

            // A new list starts today, so its first name is the one that prints tomorrow morning.
            Began = DateOnly.FromDateTime(DateTime.Today),
        };

        InitializeComponent();
        Wrap(ForewordArea.Settings, "List");
        Render();
    }

    private void Render()
    {
        HeaderText = string.IsNullOrWhiteSpace(rotation.Name) ? "New list" : rotation.Name;

        Fields.Clear();
        Names.Clear();

        AddField("What it is called", NameEntry());
        AddField("This morning", Segmented(
            ["Printing", "Paused"],
            rotation.Paused ? "Paused" : "Printing",
            picked => Commit(rotation with { Paused = picked == "Paused" })));

        for (var i = 0; i < rotation.Members.Count; i++)
        {
            AddNameRow(i);
        }

        NamesCaption.Text = rotation.Members.Count switch
        {
            0 => "Add the names. One prints each morning, in this order.",
            1 => "One name, so it prints every morning.",
            _ => $"{rotation.Members.Count} names, so each comes round every {rotation.Members.Count} days.",
        };
    }

    private View NameEntry()
    {
        var entry = new Entry
        {
            Text = rotation.Name,
            Placeholder = "Missionaries",
            Style = PaperResources.Token<Style>("CardRowTitle"),
        };

        // On unfocus rather than on change: this writes to Drive, and a round-trip per keystroke is
        // a screen that fights the person typing on it.
        entry.Unfocused += (_, _) => Commit(rotation with { Name = entry.Text ?? string.Empty });
        entry.Completed += (_, _) => Commit(rotation with { Name = entry.Text ?? string.Empty });

        return entry;
    }

    private void AddNameRow(int index)
    {
        if (index > 0)
        {
            Names.Add(Rule());
        }

        var reader = new EditorReader();
        var name = rotation.Members[index].ReadBy(reader);

        var up = Quiet("Up", enabled: index > 0, () => Move(index, index - 1));
        var down = Quiet("Down", enabled: index < rotation.Members.Count - 1, () => Move(index, index + 1));
        var remove = Quiet("Remove", enabled: true, () => RemoveAt(index));

        var actions = new HorizontalStackLayout
        {
            Spacing = DesignTokens.Space.X2,
            VerticalOptions = LayoutOptions.Center,
            Children = { up, down, remove },
        };

        var row = new Grid
        {
            Padding = new Thickness(16, 12),
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = DesignTokens.Space.X3,
            MinimumHeightRequest = DesignTokens.Shape.ListRowMinHeightDp,
            Children =
            {
                new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = name, Style = PaperResources.Token<Style>("CardRowTitle") },
                        new Label
                        {
                            Text = Turn(index),
                            Style = PaperResources.Token<Style>("CardRowDescription"),
                        },
                    },
                },
                actions,
            },
        };

        Grid.SetColumn(actions, 1);
        Names.Add(row);
    }

    /// <summary>
    /// Which morning this name is next due, counted the same way the page counts it. It is the
    /// question the owner actually has about a list — not what position a row is in.
    /// </summary>
    private string Turn(int index)
    {
        if (rotation.Paused || rotation.Members.Count == 0)
        {
            return "Not printing";
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var elapsed = today.DayNumber - rotation.Began.DayNumber;
        if (elapsed < 0)
        {
            return "Starts later";
        }

        var current = elapsed % rotation.Members.Count;
        var away = (index - current + rotation.Members.Count) % rotation.Members.Count;

        return away switch
        {
            0 => "Today",
            1 => "Tomorrow",
            _ => $"In {away} days",
        };
    }

    private void Move(int from, int to)
    {
        var members = rotation.Members.ToList();
        (members[from], members[to]) = (members[to], members[from]);

        Commit(rotation with { Members = members });
    }

    private void RemoveAt(int index)
    {
        var members = rotation.Members.ToList();
        members.RemoveAt(index);

        Commit(rotation with { Members = members });
    }

    private async void OnAddNameClicked(object sender, EventArgs e)
    {
        var typed = await DisplayPromptAsync("Add a name", "Who is on this list?", "Add", "Cancel");
        if (string.IsNullOrWhiteSpace(typed))
        {
            return;
        }

        Commit(rotation with { Members = [.. rotation.Members, PrayerText.Of(typed.Trim())] });
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (isNew && rotation.Members.Count == 0 && string.IsNullOrWhiteSpace(rotation.Name))
        {
            await Navigation.PopAsync();
            return;
        }

        if (await Save(settings.Rotations.Where(kept => kept.Id != rotation.Id).ToList()))
        {
            await Navigation.PopAsync();
        }
    }

    private void Commit(PrayerRotation changed)
    {
        rotation = changed;

        var kept = settings.Rotations.ToList();
        var at = kept.FindIndex(existing => existing.Id == rotation.Id);

        if (at < 0)
        {
            kept.Add(rotation);
        }
        else
        {
            kept[at] = rotation;
        }

        _ = Commit(kept);
    }

    private async Task Commit(IReadOnlyList<PrayerRotation> kept)
    {
        if (await Save(kept))
        {
            Render();
        }
    }

    private async Task<bool> Save(IReadOnlyList<PrayerRotation> kept)
    {
        var saved = await settings.Save(kept);

        StatusLabel.IsVisible = !saved;
        StatusLabel.Text = settings.Sentence();

        return saved;
    }

    // ---- the controls ----

    private void AddField(string label, View control)
    {
        if (Fields.Count > 0)
        {
            Fields.Add(Rule());
        }

        Fields.Add(new VerticalStackLayout
        {
            Padding = new Thickness(16, 12),
            Spacing = DesignTokens.Space.X1,
            Children =
            {
                new Label { Text = label, Style = PaperResources.Token<Style>("CardRowDescription") },
                control,
            },
        });
    }

    private static View Segmented(IEnumerable<string> options, string picked, Action<string> commit)
    {
        var control = new PaperSegmented(options, picked);
        control.Picked += commit;
        return control;
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

    /// <summary>
    /// The screen that edits the lists has to read the names on them, so it declares itself rather
    /// than borrowing somebody else's permission. It is the fifth reader and the only one on the
    /// phone — which is why the guard enumerates them by source rather than only by reflection over
    /// the two assemblies a test can load.
    /// </summary>
    private sealed class EditorReader : IPrayerReader
    {
    }
}
