using Storyworthtelling.Diagnostics;
using Codex.Domain.Diagnostics;
using Pulp.Domain.Diagnostics;
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
using Pulp.Domain.Household.Records;
using Pulp.Domain.People;
using Pulp.Domain.Shell;
using Pulp.Domain.Voice;
using Pulp.Title;

namespace Pulp.Shell;

/// <summary>
/// Calendar, one tab of Pulp's binder (mockup 8, D66, D244-D247): the next two weeks of the viewer's own and
/// shared items, grouped by day under relative headings, each line a mono time or "All day", a title, a quiet
/// kind-and-service line with whose it is at the end. A tap opens the title's page, or the league on Sports.
/// There is no bottom bar (D246); a plan is added from a quiet "Add a plan" line under the status (D305) and
/// removed from a long-press on its row, undo-first on the dog-ear. Reading happens off the UI thread and
/// the page says so in a status sentence while it does, never a spinner. The words are the Domain's
/// (<see cref="CalendarScreen"/>); the page is not shown in a child's view (<c>ChildViewAreas</c>).
/// </summary>
public sealed class CalendarArea(Activity host) : PulpAreaPage(host, PulpArea.Calendar)
{
    /// <summary>The mono time column's width, as drawn.</summary>
    private const int TimeColumnDp = 56;

    private const string AddPlanLabel = "Add a plan";

    private CancellationTokenSource? loading;
    private CalendarScreenPage? page;
    private ThisPhoneStatus phone = ThisPhoneStatus.NoRoster;
    private bool unreadable;
    private ViewerId? me;
    private IReadOnlyList<ViewerId> adults = [];
    private IReadOnlyList<HouseholdPlan> plans = [];

    // The form's draft, kept across the rebuilds a date or time pick causes.
    private bool adding;
    private string planText = string.Empty;
    private string planPlace = string.Empty;
    private DateOnly planDate = DateOnly.FromDateTime(DateTime.Today);
    private TimeOnly? planTime;
    private bool planShared;
    private string? planProblem;
    private string? planSaved;

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

        if (page.IsEmpty && !adding && me is null)
        {
            var empty = new EmptyState(Host, VoiceSeat.EmptyCalendar);
            empty.Present(VoiceLineAction.ShowNeutral, null);
            return empty;
        }

        var list = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        if (page.IsEmpty)
        {
            var empty = new EmptyState(Host, VoiceSeat.EmptyCalendar);
            empty.Present(VoiceLineAction.ShowNeutral, null);
            list.AddView(empty);
        }
        else
        {
            list.AddView(PaperUi.Secondary(Host, CalendarScreen.StatusLine));
        }

        if (me is not null)
        {
            if (!adding && planSaved is not null)
            {
                list.AddView(PaperUi.Secondary(Host, planSaved));
            }

            list.AddView(adding ? PlanForm() : PaperUi.QuietButton(Host, AddPlanLabel, () =>
            {
                adding = true;
                planProblem = null;
                planSaved = null;
                Invalidate();
            }));
        }

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
        if (CalendarPlans.IdOfKey(row.Key) is { } planId)
        {
            line.LongClickable = true;
            line.LongClick += (_, _) => PlanActions(planId, row);
        }

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
                var roster = household.Roster;
                var adultIds = roster?.Viewers.Where(viewer => !roster.IsChild(viewer.Id)).Select(viewer => viewer.Id).ToList() ?? [];
                var read = who.Viewer is { } viewer ? await services.CalendarPageAsync(viewer, cancel.Token) : null;
                Host.RunOnUiThread(() =>
                {
                    if (cancel.IsCancellationRequested)
                    {
                        return;
                    }

                    unreadable = false;
                    phone = who.Status;
                    adults = adultIds;
                    plans = household.CalendarPlans;
                    me = who.Viewer is { } mine && adultIds.Contains(mine) ? mine : null;
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

    // ---- plans (D68, D146, D305) ----

    /// <summary>
    /// The "Add a plan" form, built from Paper's own components: a line of words, a day and optionally a time
    /// from the system pickers, a place, and who it is for. It is checked by <see cref="CalendarPlans.Check"/>
    /// before anything is written, and what is written is a household record, so the other phone sees it too.
    /// </summary>
    private View PlanForm()
    {
        var form = PaperUi.HairlineCard(Host);
        var gap = PaperTheme.Dp(Host, DesignTokens.Space.X2);
        form.AddView(PaperUi.Heading(Host, AddPlanLabel));

        var text = PaperUi.Field(Host, "What is the plan?");
        text.Text = planText;
        text.TextChanged += (_, _) => planText = text.Text ?? string.Empty;
        form.AddView(text);

        form.AddView(PaperUi.SecondaryButton(Host, planDate.ToString("ddd, MMM d", System.Globalization.CultureInfo.CurrentCulture), () =>
        {
            var dialog = new DatePickerDialog(
                Host,
                (_, picked) =>
                {
                    planDate = DateOnly.FromDateTime(picked.Date);
                    Invalidate();
                },
                planDate.Year, planDate.Month - 1, planDate.Day);
            dialog.Show();
        }), Spaced(gap));

        var time = planTime is { } at ? CalendarScreen.TimeText(new CalendarWhen(planDate, at)) : CalendarScreen.AllDayText;
        form.AddView(PaperUi.SecondaryButton(Host, time, () =>
        {
            var start = planTime ?? new TimeOnly(19, 0);
            var dialog = new TimePickerDialog(
                Host,
                (_, picked) =>
                {
                    planTime = new TimeOnly(picked.HourOfDay, picked.Minute);
                    Invalidate();
                },
                start.Hour, start.Minute, false);
            dialog.Show();
        }), Spaced(gap));
        if (planTime is not null)
        {
            form.AddView(PaperUi.QuietButton(Host, "All day instead", () =>
            {
                planTime = null;
                Invalidate();
            }));
        }

        var place = PaperUi.Field(Host, "Where? (optional)");
        place.Text = planPlace;
        place.TextChanged += (_, _) => planPlace = place.Text ?? string.Empty;
        form.AddView(place, Spaced(gap));

        form.AddView(PaperUi.Segmented(Host, ["Just me", "Everyone"], index => planShared = index == 1, planShared ? 1 : 0), Spaced(gap));

        if (planProblem is not null)
        {
            form.AddView(PaperUi.Secondary(Host, planProblem), Spaced(gap));
        }

        form.AddView(PaperUi.PrimaryButton(Host, "Add", SavePlan), Spaced(gap));
        form.AddView(PaperUi.QuietButton(Host, "Cancel", () =>
        {
            adding = false;
            Invalidate();
        }));
        return form;
    }

    private LinearLayout.LayoutParams Spaced(int top) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = top };

    private void SavePlan()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        planProblem = CalendarPlans.Check(planText, planDate, today);
        if (planProblem is not null || me is not { } maker)
        {
            Invalidate();
            return;
        }

        var plan = new HouseholdPlan(
            CalendarPlans.NewId(),
            maker,
            CalendarPlans.Clean(planText)!,
            null,
            planDate,
            planTime,
            CalendarPlans.Clean(planPlace),
            CalendarPlans.Who(maker, planShared, adults));
        adding = false;
        planSaved = CalendarPlans.SavedNote(planDate, today);
        planText = string.Empty;
        planPlace = string.Empty;
        planTime = null;
        Write("add the plan", token => PulpServices.For(Host).PutAsync(HouseholdMaps.Plan, plan, token),
            TapWatch.Action(PulpKeyActions.Plan, TimeSpan.FromSeconds(30)));
    }

    /// <summary>A long-press on a plan row: one entry, taken at once, with the way back on the dog-ear (§13).</summary>
    private void PlanActions(string planId, CalendarScreenRow row)
    {
        var plan = plans.FirstOrDefault(found => string.Equals(found.Id, planId, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            return;
        }

        ActionCard.Show(Host, Overlay, row.Title, row.TimeText, new CardAction("Remove plan", () =>
        {
            var services = PulpServices.For(Host);
            Write("remove the plan", token => services.RemoveAsync(HouseholdMaps.Plan, plan, token));
            DogEar.Show(Host, Overlay, "Undo remove plan, " + plan.Text + ".", () =>
                Write("put the plan back", token => services.PutAsync(HouseholdMaps.Plan, plan, token)));
        }));
    }

    private void Write(string what, Func<CancellationToken, Task> write, KeyAction? watched = null)
    {
        var services = PulpServices.For(Host);
        _ = Task.Run(async () =>
        {
            try
            {
                await write(CancellationToken.None);
                watched?.Succeeded();
            }
            catch (Exception exception)
            {
                watched?.Failed($"{exception.GetType().Name}: {exception.Message}");
                services.Diagnostics.Warn("calendar", "The calendar could not " + what + ".", exception);
            }

            Host.RunOnUiThread(Reload);
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
