using Storyworthtelling.Maui.State;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// The body of the Waiting page (DESIGN.md §13, D162, mockup 17): an optional status line, then every
/// notice written straight onto the page — a thin bar in its role colour, the headline and its date, the
/// detail, and at most one secondary-button action — or, when nothing is waiting, the sentence saying so.
/// No cards, no count.
///
/// <para><b>It is a view, not a page, so an app can put it inside its own frame.</b> Mockup 17 draws the
/// Waiting page wearing the calling app's binder strip with the area still lit, and on that app's graph
/// paper; the strip and the paper are per-app (<c>LedgerBinderPage</c>, <c>FolioBinderPage</c>) and this
/// shared kit cannot see either. So the kit supplies the part that is the same everywhere, and the host
/// page places it: <c>LedgerWaitingPage</c> puts it inside <c>LedgerBinderPage.Wrap</c>. A host with no
/// frame uses <see cref="WaitingPage"/>.</para>
///
/// <para><b>It is live.</b> Given a <see cref="DogEarState"/> it redraws when the state reports a change
/// while the page is showing, so the last notice resolving leaves "Nothing waiting." on screen instead of
/// a stale row. The host calls <see cref="Attach"/> when it appears and <see cref="Detach"/> when it goes;
/// a view that stayed subscribed after its page was popped would keep the whole page alive. Given a plain
/// list (the older constructor) it draws that list once.</para>
///
/// <para>What is drawn is decided by <see cref="WaitingPlan"/>, which has no view in it and is tested;
/// this only paints it.</para>
/// </summary>
public sealed class WaitingView : ContentView
{
    private readonly DogEarState? state;
    private readonly Func<IReadOnlyList<DogEarNotice>> notices;
    private readonly string? subtitle;
    private bool attached;

    /// <summary>A live view over <paramref name="state"/>.</summary>
    public WaitingView(DogEarState state, string? subtitle = null)
        : this(() => state.Notices, state, subtitle)
    {
    }

    /// <summary>A view of a fixed list, drawn once — what the Waiting page took before it was live.</summary>
    public WaitingView(IReadOnlyList<DogEarNotice> notices, string? subtitle = null)
        : this(() => notices, null, subtitle)
    {
    }

    private WaitingView(Func<IReadOnlyList<DogEarNotice>> notices, DogEarState? state, string? subtitle)
    {
        this.notices = notices;
        this.state = state;
        this.subtitle = subtitle;
        Render();
    }

    /// <summary>Starts listening, and draws again in case the state moved while nobody was looking.</summary>
    public void Attach()
    {
        if (attached)
        {
            return;
        }

        attached = true;
        if (state is not null)
        {
            state.NoticesChanged += OnNoticesChanged;
        }

        Render();
    }

    /// <summary>Stops listening. Idempotent.</summary>
    public void Detach()
    {
        if (!attached)
        {
            return;
        }

        attached = false;
        if (state is not null)
        {
            state.NoticesChanged -= OnNoticesChanged;
        }
    }

    private void OnNoticesChanged()
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(Render);
        }
        else
        {
            Render();
        }
    }

    private void Render()
    {
        var plan = WaitingPlan.From(notices(), DateTimeOffset.Now);

        var column = new VerticalStackLayout
        {
            Padding = new Thickness(DesignTokens.Space.X6, 0, DesignTokens.Space.X4, DesignTokens.Space.X6),
        };

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            column.Children.Add(new Label
            {
                Text = subtitle,
                Style = PaperResources.Token<Style>("StatusLine"),
            });
        }

        if (plan.EmptySentence is { } sentence)
        {
            column.Children.Add(new Label
            {
                Text = sentence,
                Style = PaperResources.Token<Style>("EmptyStateLabel"),
                Margin = new Thickness(0, DesignTokens.Space.X12, 0, 0),
            });
        }
        else
        {
            foreach (var row in plan.Rows)
            {
                column.Children.Add(BuildRow(row));
                column.Children.Add(PaperList.Divider());
            }
        }

        Content = column;
    }

    // One row: a role-colour bar on the left; the headline with its date on the same line; the detail;
    // and at most one secondary-button action (mockup 17's anatomy).
    private static View BuildRow(WaitingRow row)
    {
        var bar = new BoxView
        {
            WidthRequest = RoleBarWidthDp,
            Color = PaperResources.Colour(row.BarToken),
            VerticalOptions = LayoutOptions.Fill,
            CornerRadius = RoleBarWidthDp / 2,
        };

        var top = new Grid
        {
            ColumnSpacing = DesignTokens.Space.X2,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        var headline = new Label { Text = row.Headline, Style = PaperResources.Token<Style>("HeadingLabel") };
        Grid.SetColumn(headline, 0);
        top.Children.Add(headline);

        if (row.Date is { } date)
        {
            var when = new Label
            {
                Text = date,
                Style = PaperResources.Token<Style>("StatusLine"),
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, DesignTokens.Space.X1, 0, 0),
            };
            Grid.SetColumn(when, 1);
            top.Children.Add(when);
        }

        var text = new VerticalStackLayout { Spacing = DesignTokens.Space.X1 };
        text.Children.Add(top);
        text.Children.Add(new Label
        {
            Text = row.Detail,
            Style = PaperResources.Token<Style>("SecondaryLabel"),
            TextColor = PaperResources.Colour("InkSoft"),
        });

        if (row.ActionLabel is { } label)
        {
            // A notice's action is a secondary button, never the primary style — there is no
            // pinned bar on this page, because nothing on it is THE primary action (§7).
            var button = new Button
            {
                Text = label,
                Style = PaperResources.Token<Style>("SecondaryButton"),
                HorizontalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, DesignTokens.Space.X3, 0, 0),
            };
            button.Clicked += (_, _) => row.OnAction?.Invoke();
            text.Children.Add(button);
        }

        return new Grid
        {
            ColumnSpacing = DesignTokens.Space.X3,
            Padding = new Thickness(0, DesignTokens.Space.X4),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
            },
            Children =
            {
                Column(bar, 0),
                Column(text, 1),
            },
        };
    }

    private static View Column(View view, int column)
    {
        Grid.SetColumn(view, column);
        return view;
    }

    /// <summary>The role bar's width. Mockup 17 draws it at 4dp; kept as its own named constant
    /// rather than a bare literal so a future adjustment has one place to make it.</summary>
    private const int RoleBarWidthDp = 4;
}
