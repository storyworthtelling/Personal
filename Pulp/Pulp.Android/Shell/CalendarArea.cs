using Android.App;
using Android.Content;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Components;
using Pulp.Domain.Calendar;
using Pulp.Domain.Core;
using Pulp.Domain.People;
using Pulp.Domain.Shell;
using Pulp.Domain.Voice;
using Pulp.Title;

namespace Pulp.Shell;

/// <summary>
/// Calendar, one tab of Pulp's binder (mockup 8, D66, D244-D247): the next two weeks of the viewer's own and
/// shared items, grouped by day under relative headings, each line a mono time or "All day", a title, a quiet
/// kind-and-service line with whose it is at the end. A tap opens the title's page, or the league on Sports.
/// There is no bottom bar (D246): plans are made from a title's page. Reading happens off the UI thread and
/// the page says so in a status sentence while it does, never a spinner. The words are the Domain's
/// (<see cref="CalendarScreen"/>); the page is not shown in a child's view (<c>ChildViewAreas</c>).
/// </summary>
public sealed class CalendarArea(Activity host) : PulpAreaPage(host, PulpArea.Calendar)
{
    /// <summary>The mono time column's width, as drawn.</summary>
    private const int TimeColumnDp = 56;

    private CancellationTokenSource? loading;
    private CalendarScreenPage? page;
    private ThisPhoneStatus phone = ThisPhoneStatus.NoRoster;
    private bool unreadable;

    public override View BuildView()
    {
        if (unreadable)
        {
            return Column(PaperUi.Secondary(Host, CalendarScreen.UnreadableStatus));
        }

        if (page is null)
        {
            return Column(PaperUi.Secondary(Host, CalendarScreen.ReadingStatus));
        }

        if (phone == ThisPhoneStatus.Unmatched)
        {
            return Column(PaperUi.Secondary(Host, ThisPhone.UnmatchedSentence));
        }

        if (page.IsEmpty)
        {
            var empty = new EmptyState(Host, VoiceSeat.EmptyCalendar);
            empty.Present(VoiceLineAction.ShowNeutral, null);
            return empty;
        }

        var list = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        list.AddView(PaperUi.Secondary(Host, CalendarScreen.StatusLine));
        foreach (var day in page.Days)
        {
            var heading = PaperUi.Heading(Host, day.Label);
            var top = PaperTheme.Dp(Host, DesignTokens.Space.X4);
            heading.SetPadding(0, top, 0, PaperTheme.Dp(Host, DesignTokens.Space.X1));
            list.AddView(heading);
            foreach (var row in day.Rows)
            {
                list.AddView(RowView(row));
            }
        }

        var scroll = new ScrollView(Host) { FillViewport = true };
        scroll.AddView(list);
        return scroll;
    }

    public override void Resume() => Reload();

    public override void Pause()
    {
        loading?.Cancel();
        loading = null;
    }

    private View RowView(CalendarScreenRow row)
    {
        var line = new LinearLayout(Host) { Orientation = Orientation.Horizontal, Clickable = true };
        var vertical = PaperTheme.Dp(Host, DesignTokens.Space.X2);
        line.SetPadding(0, vertical, 0, vertical);
        line.Click += (_, _) => Open(row);

        var time = PaperUi.Mono(Host, row.TimeText);
        time.SetTextColor(PaperTheme.InkSoft);
        line.AddView(time, new LinearLayout.LayoutParams(PaperTheme.Dp(Host, TimeColumnDp), ViewGroup.LayoutParams.WrapContent));

        var text = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        text.AddView(PaperUi.Body(Host, row.Title));
        text.AddView(DetailView(row));
        line.AddView(text, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        column.AddView(line);
        var rule = new View(Host);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        column.AddView(rule, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(Host, DesignTokens.Shape.HairlineDp)));
        return column;
    }

    /// <summary>The kind-and-service line in the muted ink, with whose it is at the end in the softer one (mockup 8).</summary>
    private TextView DetailView(CalendarScreenRow row)
    {
        var detail = PaperUi.Secondary(Host, row.Detail);
        if (string.IsNullOrWhiteSpace(row.Whose))
        {
            return detail;
        }

        var text = new SpannableStringBuilder(row.Detail + " · ");
        var start = text.Length();
        text.Append(row.Whose);
        text.SetSpan(new ForegroundColorSpan(PaperTheme.InkSoft), start, text.Length(), SpanTypes.ExclusiveExclusive);
        detail.TextFormatted = text;
        return detail;
    }

    /// <summary>
    /// A title opens its page; a game opens the Sports tab. The shell is the running activity, so the tab is
    /// requested with the same intent <c>MainActivity.OpenOn</c> sends but without its Finish, which would
    /// close the shell this page lives in; SingleTop hands it to the running shell's OnNewIntent.
    /// </summary>
    private void Open(CalendarScreenRow row)
    {
        if (row.OpenTitle is { } title)
        {
            Host.StartActivity(TitleActivity.For(Host, title));
            return;
        }

        var intent = new Intent(Host, Host.GetType());
        intent.PutExtra(AreaRequest.Extra, AreaRequest.Word(PulpArea.Sports));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        Host.StartActivity(intent);
    }

    private void Reload()
    {
        loading?.Cancel();
        var cancel = new CancellationTokenSource();
        loading = cancel;
        var services = PulpServices.For(Host);

        _ = Task.Run(async () =>
        {
            try
            {
                var household = await services.HouseholdAsync(cancel.Token);
                var who = services.ThisPhoneOf(household);
                var read = who.Viewer is { } viewer ? await services.CalendarPageAsync(viewer, cancel.Token) : null;
                Host.RunOnUiThread(() =>
                {
                    if (cancel.IsCancellationRequested)
                    {
                        return;
                    }

                    unreadable = false;
                    phone = who.Status;
                    page = read ?? new CalendarScreenPage([]);
                    Invalidate();
                });
            }
            catch (Exception exception)
            {
                if (CancellationVerdict.IsUserCancel(exception, cancel.Token))
                {
                    return;
                }

                services.Diagnostics.Warn("calendar", "The calendar page could not be read.", exception);
                Host.RunOnUiThread(() =>
                {
                    unreadable = page is null;
                    Invalidate();
                });
            }
        });
    }

    private View Column(View child)
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        column.SetPadding(0, 0, 0, PaperTheme.Dp(Host, DesignTokens.Space.X4));
        column.AddView(child, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return column;
    }
}
