using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Foreword.Domain.Calendar;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Storyworthtelling.Maui;

namespace Foreword.Android.Pages;

/// <summary>
/// C3. The words one calendar routes on, and — the part that matters — what they actually do.
///
/// <para><b>The foot is the point.</b> Keyword routing fails quietly: a word that matches nothing
/// looks exactly like a word that matches everything, and the owner finds out on paper the next
/// morning. So the rule is run against the events really on this calendar today, and each one shows
/// where it would go. A rule you cannot see failing is one you will not fix.</para>
///
/// <para>Adding a word is the only edit. There is no regex, because a regex you get wrong is silent
/// too.</para>
/// </summary>
public partial class CalendarSplitPage : BinderPage
{
    private readonly CalendarSettings settings = new();
    private readonly string calendarId;

    public CalendarSplitPage(string calendarId, string name)
    {
        InitializeComponent();
        this.calendarId = calendarId;
        Wrap(ForewordArea.Calendar, name);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        settings.Reload();
        RenderWords();
        await RenderPreview();
    }

    private void RenderWords()
    {
        Words.Clear();

        foreach (var person in ColumnSet.People)
        {
            AddWordRow(person);
        }

        RenderAddresses();

        AddRule();
        Words.Add(new Grid
        {
            Padding = new Thickness(16, 12),
            Children =
            {
                new VerticalStackLayout
                {
                    Children =
                    {
                        new Label
                        {
                            Text = "No word matched",
                            Style = PaperResources.Token<Style>("CardRowTitle"),
                        },
                        new Label
                        {
                            // Which is what a calendar called Family means.
                            Text = "Everyone",
                            Style = PaperResources.Token<Style>("CardRowDescription"),
                        },
                    },
                },
            },
        });
    }

    private void RenderAddresses()
    {
        Addresses.Clear();

        // The two who have one. An address field for Wren or Linden could never match anything and
        // would never say so — see PersonIdentity, and the guard that pins this loop.
        foreach (var person in PersonIdentity.WithMailboxes)
        {
            AddAddressRow(person);
        }
    }

    private void AddAddressRow(PersonColumn person)
    {
        if (Addresses.Count > 0)
        {
            Addresses.Add(new BoxView { HeightRequest = DesignTokens.Shape.HairlineDp,
                Color = PaperResources.Colour("Hairline") });
        }

        if (!settings.Mapping.PersonAddresses.TryGetValue(person, out var addresses))
        {
            addresses = [];
            settings.Mapping.PersonAddresses[person] = addresses;
        }

        var entry = new Entry
        {
            Text = string.Join(", ", addresses),
            Placeholder = "name@example.com",
            Keyboard = Keyboard.Email,
            Style = PaperResources.Token<Style>("MonoEntry"),
        };

        entry.TextChanged += (_, e) =>
        {
            addresses.Clear();
            addresses.AddRange((e.NewTextValue ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            settings.SaveMapping();
        };

        Addresses.Add(new VerticalStackLayout
        {
            Padding = new Thickness(16, 12),
            Spacing = DesignTokens.Space.X2,
            Children =
            {
                new Label
                {
                    Text = $"{person} — invited as",
                    Style = PaperResources.Token<Style>("CardRowTitle"),
                },
                entry,
            },
        });
    }

    private void AddWordRow(PersonColumn person)
    {
        AddRule();

        var words = settings.WordsFor(person);

        var entry = new Entry
        {
            Text = string.Join(", ", words),
            Placeholder = person.ToString(),
            Style = PaperResources.Token<Style>("MonoEntry"),
        };

        // Committed as it is typed. There is no Save on this screen, and a word the owner typed and
        // then navigated away from is exactly the one they meant to add.
        entry.TextChanged += (_, e) =>
        {
            words.Clear();
            words.AddRange((e.NewTextValue ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            settings.SaveMapping();
        };

        Words.Add(new VerticalStackLayout
        {
            Padding = new Thickness(16, 12),
            Spacing = DesignTokens.Space.X2,
            Children =
            {
                new Label
                {
                    Text = $"{person} — if the title contains",
                    Style = PaperResources.Token<Style>("CardRowTitle"),
                },
                entry,
            },
        });
    }

    /// <summary>
    /// The rule against today's real events. Failures here are a status line, never a dialog: the
    /// words are still editable without a network, and an owner offline cannot fix a connection by
    /// being told about it.
    /// </summary>
    private async Task RenderPreview()
    {
        Preview.Clear();
        PreviewCaption.Text = "Reading today's events…";

        List<CalendarEvent> events;
        try
        {
            var client = IPlatformApplication.Current!.Services.GetRequiredService<IGoogleCalendarClient>();
            events = (await client.GetEventsForDay(DateOnly.FromDateTime(DateTime.Today)))
                .Where(e => e.SourceCalendarId == calendarId)
                .ToList();
        }
        catch (Exception ex)
        {
            PreviewCaption.Text = $"Could not read today's events — {ex.Message}";
            return;
        }

        if (events.Count == 0)
        {
            PreviewCaption.Text = "Nothing on this calendar today, so there is nothing to show yet.";
            return;
        }

        PreviewCaption.Text = string.Empty;
        var routing = new ColumnRouting(
            Column: null,
            Addresses: ColumnSet.People.ToDictionary(
                p => p, p => (IReadOnlyList<string>)(settings.Mapping.PersonAddresses.TryGetValue(p, out var a) ? a : [])),
            Names: ColumnSet.People.ToDictionary(p => p, p => (IReadOnlyList<string>)settings.WordsFor(p)),
            // The SAME fallback the morning will use, not this screen's own reading of it. A
            // preview free to disagree with the delivery is worse than no preview.
            Default: IPlatformApplication.Current!.Services
                .GetRequiredService<CalendarColumnMapper>().FallbackColumn);

        foreach (var e in events.OrderBy(e => e.Start))
        {
            var columns = ColumnSet.Resolve(e, routing);
            var goes = ColumnSet.People.All(columns.Contains)
                ? "Everyone"
                : string.Join(" · ", columns);

            Preview.Add(new Grid
            {
                Padding = new Thickness(16, 12),
                Children =
                {
                    new VerticalStackLayout
                    {
                        Children =
                        {
                            new Label { Text = e.Title, Style = PaperResources.Token<Style>("CardRowTitle") },
                            new Label
                            {
                                Text = $"{e.Start:dddd h:mm} → {goes}",
                                Style = PaperResources.Token<Style>("CardRowDescription"),
                            },
                        },
                    },
                },
            });
        }
    }

    private void AddRule()
    {
        if (Words.Count > 0)
        {
            Words.Add(new BoxView
            {
                HeightRequest = DesignTokens.Shape.HairlineDp,
                Color = PaperResources.Colour("Hairline"),
            });
        }
    }
}
