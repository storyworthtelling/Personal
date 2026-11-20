using System.Globalization;
using Foreword.Domain.Models;
using Storyworthtelling.Household;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;
using Storyworthtelling.People;
using Storyworthtelling.Time;

namespace Foreword.Android.Pages;

/// <summary>
/// The household every app reads, edited here: the people (name, adult or child, Google account, pickup, birthday, calendar) and the
/// household's rhythm (the family calendar, dinner, bedtime). <b>Foreword owns it</b> (<see cref="ForewordHousehold"/>): on the owner's
/// phone every change is written to the household at once and sent to Drive; on the partner's phone the same page reads it.
///
/// <para>The order of the people is this phone's own (it is the order The Day prints its columns in), so Up and Down move this phone's
/// columns and nothing in the household.</para>
/// </summary>
public partial class HouseholdPage : BinderPage
{
    private const int StepMinutes = 15;
    private const string AdultWord = "Adult";
    private const string ChildWord = "Child";
    private static readonly TimeOnly DinnerStandIn = new(18, 0);
    private static readonly TimeOnly BedtimeStandIn = new(20, 0);
    private static readonly TimeSpan DinnerLengthStandIn = TimeSpan.FromMinutes(60);

    private readonly CalendarSettings settings = new();

    // Who the last removal took, so it can be put back while the page is open.
    private HouseholdPerson? removed;

    public HouseholdPage()
    {
        InitializeComponent();
        Wrap(ForewordArea.Calendar, "Household");
    }

    private static HouseholdPeopleFile File => ForewordHousehold.File;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Render();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var standing = await ForewordHousehold.RefreshAsync(IPlatformApplication.Current!.Services, CancellationToken.None);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Render();
            if (standing.Sentence.Length > 0)
            {
                Say(standing.Sentence);
            }
        });
    }

    private void Render()
    {
        settings.Reload();
        var household = File.People;
        var editable = File.CanEdit && File.Standing.State != PeopleFileState.Unreadable;
        AddButton.IsEnabled = editable;

        People.Clear();
        var ordered = InThisPhonesOrder(household);
        for (var i = 0; i < ordered.Count; i++)
        {
            if (i > 0)
            {
                People.Add(Rule());
            }

            People.Add(PersonRow(ordered[i], i, ordered.Count, editable));
        }

        if (ordered.Count == 0)
        {
            People.Add(new Label
            {
                Text = editable ? "Nobody yet. Add the first person below." : "The household appears here once it reaches this phone.",
                Style = PaperResources.Token<Style>("CardCaption"),
                Padding = new Thickness(16, 12),
            });
        }

        Rhythm.Clear();
        RenderRhythm(household.Rhythm, editable);

        if (!File.CanEdit)
        {
            Say(HouseholdPeopleEditor.ReadOnlySentence);
        }
    }

    // This phone's column order first (the household's people it already prints), then anyone it doesn't print yet.
    private List<HouseholdPerson> InThisPhonesOrder(HouseholdPeople household)
    {
        var order = settings.Mapping.Columns.Select(column => column.HouseholdId).Where(id => id.Length > 0).ToList();
        return household.People
            .OrderBy(person => order.IndexOf(person.Id) is var at && at >= 0 ? at : int.MaxValue)
            .ThenBy(person => person.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private View PersonRow(HouseholdPerson person, int index, int count, bool editable)
    {
        var name = new Entry { Text = person.Name, Placeholder = "Their name", IsEnabled = editable };
        CommitOnLeave(name, typed =>
        {
            if (typed == person.Name)
            {
                return;
            }

            if (typed.Length == 0)
            {
                Say("A person needs a name.");
                name.Text = person.Name;
                return;
            }

            if (Changed(() => person with { Name = typed }, $"A name is at most {Quantities.Count(HouseholdPerson.MaxNameLength, "character")}.") is { } renamed)
            {
                _ = Put(renamed);
            }
            else
            {
                name.Text = person.Name;
            }
        });

        var kind = new PaperSegmented([AdultWord, ChildWord], person.IsChild ? ChildWord : AdultWord) { IsEnabled = editable };
        kind.Picked += picked => _ = Put(person with { OnPickup = picked != ChildWord && person.OnPickup, IsChild = picked == ChildWord });

        var account = new Entry
        {
            Text = person.GoogleAddress ?? string.Empty,
            Placeholder = "Google address, if they have one",
            Keyboard = Keyboard.Email,
            IsEnabled = editable,
        };
        CommitOnLeave(account, typed =>
        {
            var next = typed.Length == 0 ? null : typed;
            if (next == person.GoogleAddress)
            {
                return;
            }

            if (Changed(() => person with { GoogleAddress = next }, "That address needs one @ and no spaces.") is { } changed)
            {
                _ = Put(changed, onRefused: () => account.Text = person.GoogleAddress ?? string.Empty);
            }
            else
            {
                account.Text = person.GoogleAddress ?? string.Empty;
            }
        });

        var calendar = new Entry
        {
            Text = person.CalendarId ?? string.Empty,
            Placeholder = "Calendar address, if not their own",
            IsEnabled = editable,
        };
        CommitOnLeave(calendar, typed =>
        {
            var next = typed.Length == 0 ? null : typed;
            if (next != person.CalendarId)
            {
                _ = Put(person with { CalendarId = next });
            }
        });

        var birthday = new Entry
        {
            Text = person.Birthdate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            Placeholder = "Birthday, year-month-day",
            IsEnabled = editable,
        };
        CommitOnLeave(birthday, typed =>
        {
            DateOnly? next = null;
            if (typed.Length > 0)
            {
                if (!DateOnly.TryParseExact(typed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    Say("Write the birthday as year-month-day.");
                    birthday.Text = person.Birthdate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
                    return;
                }

                next = date;
            }

            if (next != person.Birthdate)
            {
                _ = Put(person with { Birthdate = next });
            }
        });

        var row = new VerticalStackLayout
        {
            Padding = new Thickness(16, 12),
            Spacing = DesignTokens.Space.X2,
            Children = { name, kind, account },
        };

        if (!person.IsChild)
        {
            var pickup = new PaperSegmented(["Off pickup", "On pickup"], person.OnPickup ? "On pickup" : "Off pickup") { IsEnabled = editable };
            pickup.Picked += picked => _ = Put(person with { OnPickup = picked == "On pickup" });
            row.Children.Add(pickup);
        }

        row.Children.Add(calendar);
        row.Children.Add(birthday);
        row.Children.Add(new HorizontalStackLayout
        {
            Spacing = DesignTokens.Space.X2,
            Children =
            {
                Quiet("Up", index > 0, () => Move(person, -1)),
                Quiet("Down", index < count - 1, () => Move(person, 1)),
                Quiet("Remove", editable, () => _ = RemoveWithUndo(person)),
            },
        });
        return row;
    }

    private void RenderRhythm(HouseholdRhythm rhythm, bool editable)
    {
        var family = new Entry
        {
            Text = rhythm.FamilyCalendarId ?? string.Empty,
            Placeholder = "Family calendar address",
            IsEnabled = editable,
        };
        CommitOnLeave(family, typed =>
        {
            var next = typed.Length == 0 ? null : typed;
            if (next != rhythm.FamilyCalendarId)
            {
                _ = SetRhythm(rhythm with { FamilyCalendarId = next, NoFamilyCalendar = false });
            }
        });

        var stack = new VerticalStackLayout { Padding = new Thickness(16, 12), Spacing = DesignTokens.Space.X2 };
        stack.Children.Add(Caption("Family calendar"));
        stack.Children.Add(family);
        stack.Children.Add(Stepper(
            "Family dinner", rhythm.DinnerStart, DinnerStandIn, editable,
            time => SetRhythm(rhythm with { DinnerStart = time })));
        stack.Children.Add(LengthStepper(rhythm, editable));
        stack.Children.Add(Stepper(
            "Children’s bedtime", rhythm.Bedtime, BedtimeStandIn, editable,
            time => SetRhythm(rhythm with { Bedtime = time })));
        Rhythm.Add(stack);
    }

    private View Stepper(string label, TimeOnly? value, TimeOnly standIn, bool editable, Func<TimeOnly, Task> change)
    {
        var current = value ?? standIn;
        var shown = value is { } set ? TimeFormats.Clock12(DateTime.Today.Add(set.ToTimeSpan())) : "Not set";
        return new VerticalStackLayout
        {
            Spacing = DesignTokens.Space.X1,
            Children =
            {
                Caption(label),
                new HorizontalStackLayout
                {
                    Spacing = DesignTokens.Space.X2,
                    Children =
                    {
                        Quiet("Earlier", editable, () => _ = change(current.AddMinutes(value is null ? 0 : -StepMinutes))),
                        new Label { Text = shown, VerticalOptions = LayoutOptions.Center },
                        Quiet("Later", editable, () => _ = change(current.AddMinutes(value is null ? 0 : StepMinutes))),
                    },
                },
            },
        };
    }

    private View LengthStepper(HouseholdRhythm rhythm, bool editable)
    {
        var current = rhythm.DinnerLength ?? DinnerLengthStandIn;
        var shown = rhythm.DinnerLength is { } length ? Quantities.Duration(length) : "Not set";
        Task Change(int minutes) => SetRhythm(rhythm with
        {
            DinnerLength = rhythm.DinnerLength is null ? current : TimeSpan.FromMinutes(Math.Clamp(current.TotalMinutes + minutes, StepMinutes, 240)),
        });

        return new VerticalStackLayout
        {
            Spacing = DesignTokens.Space.X1,
            Children =
            {
                Caption("How long dinner takes"),
                new HorizontalStackLayout
                {
                    Spacing = DesignTokens.Space.X2,
                    Children =
                    {
                        Quiet("Shorter", editable, () => _ = Change(-StepMinutes)),
                        new Label { Text = shown, VerticalOptions = LayoutOptions.Center },
                        Quiet("Longer", editable, () => _ = Change(StepMinutes)),
                    },
                },
            },
        };
    }

    private void Move(HouseholdPerson person, int delta)
    {
        if (settings.Mapping.Columns.FirstOrDefault(column => column.HouseholdId == person.Id) is { } column
            && settings.MoveColumn(new PersonColumn(column.Name), delta))
        {
            Render();
        }
    }

    private async Task RemoveWithUndo(HouseholdPerson person)
    {
        var edit = await File.EditAsync((editor, document, next, writer) => editor.Remove(document, person.Id, next(), writer), CancellationToken.None);
        if (!edit.Accepted)
        {
            Say(edit.Refusal!);
            return;
        }

        removed = person;
        Landed();
        Say($"Removed {person.Name} from the household.");
        UndoButton.IsVisible = true;
    }

    private async void OnUndoClicked(object sender, EventArgs e)
    {
        if (removed is not { } person)
        {
            return;
        }

        removed = null;
        UndoButton.IsVisible = false;
        Say(string.Empty);
        await Put(person);
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var typed = await PaperCards.AskText(this, "Add a person", null, "Who is it?", null, "Add",
            text => text.Length == 0 ? "A person needs a name." : null);
        if (typed is null)
        {
            return;
        }

        if (File.People.FindByName(typed) is not null)
        {
            Say($"{typed.Trim()} is already in the household.");
            return;
        }

        var id = "p-" + Guid.NewGuid().ToString("N")[..10]; // not copy: a record id
        await Put(new HouseholdPerson(id, typed, IsChild: false, Birthdate: null, OnPickup: false, GoogleAddress: null, CalendarId: null));
    }

    private async Task Put(HouseholdPerson person, Action? onRefused = null)
    {
        PeopleEdit edit;
        try
        {
            edit = await File.EditAsync((editor, document, next, writer) => editor.Put(document, person, next(), writer), CancellationToken.None);
        }
        catch (ArgumentException)
        {
            // The person broke a household rule as typed (an address without one @): nothing was written.
            Say("That address needs one @ and no spaces.");
            onRefused?.Invoke();
            return;
        }

        if (!edit.Accepted)
        {
            Say(edit.Refusal!);
            onRefused?.Invoke();
            return;
        }

        Say(string.Empty);
        Landed();
    }

    // The person with one change, or null (and the sentence said) when the change breaks a household rule.
    private HouseholdPerson? Changed(Func<HouseholdPerson> change, string refusal)
    {
        try
        {
            return change();
        }
        catch (ArgumentException)
        {
            Say(refusal);
            return null;
        }
    }

    private async Task SetRhythm(HouseholdRhythm rhythm)
    {
        var edit = await File.EditAsync((editor, document, next, writer) => editor.SetRhythm(document, rhythm, next(), writer), CancellationToken.None);
        if (!edit.Accepted)
        {
            Say(edit.Refusal!);
            return;
        }

        Landed();
    }

    // A change landed on this phone: the columns follow it at once, and Drive hears of it without the page waiting.
    private void Landed()
    {
        ForewordHousehold.Project(File.People);
        Render();
        _ = File.SyncAsync(CancellationToken.None);
    }

    private static void CommitOnLeave(Entry entry, Action<string> commit)
    {
        void Commit() => commit((entry.Text ?? string.Empty).Trim());
        entry.Unfocused += (_, _) => Commit();
        entry.Completed += (_, _) => Commit();
    }

    private void Say(string text)
    {
        StatusLabel.Text = text;
        StatusLabel.IsVisible = text.Length > 0;
    }

    private static Label Caption(string text) => new() { Text = text, Style = PaperResources.Token<Style>("CardCaption") };

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
