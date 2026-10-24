using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Calendar;
using Constitution.Domain.Settings;
using Constitution.Domain.Time;

namespace Constitution.Settings;

/// <summary>
/// The body of the Household group (book p.17): the people with their tag and one muted line, the children's bedtime in mono, the
/// family dinner, pickup, who this phone delivers for, and the calendars. Drawn from <see cref="HouseholdGroupModel"/>; a change is
/// written through <see cref="PlanningSetupFromRoster"/> and <see cref="PlanningSetupFile"/> (by <see cref="HouseholdSetupStore"/>) and
/// answered by one inline sentence, never a toast or a dialog. The model carries no birthdate, so this view cannot show one.
/// </summary>
public static class HouseholdGroupView
{
    private const int StepMinutes = 15;

    // onSaved receives the sentence to leave under the group; the page keeps it and redraws.
    public static View Build(
        Context context, HouseholdGroupModel model, HouseholdChoices choices, HouseholdSetupStore store, string? note, Action<string> onSaved)
    {
        var body = new LinearLayout(context) { Orientation = Orientation.Vertical };
        LinearLayout.LayoutParams Gap(int top) => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(context, top),
        };

        if (model.RosterStatus is { } status)
        {
            body.AddView(PaperUi.Secondary(context, status));
        }

        foreach (var person in model.People)
        {
            var line = new LinearLayout(context) { Orientation = Orientation.Vertical };
            line.AddView(PaperUi.Body(context, $"{person.Name}  {person.Tag}"));
            if (person.MutedLine is { } muted)
            {
                line.AddView(PaperUi.Secondary(context, muted));
            }

            body.AddView(line, Gap(DesignTokens.Space.X2));
        }

        if (model.ReadOnlyNote is { } readOnly)
        {
            body.AddView(PaperUi.Secondary(context, readOnly), Gap(DesignTokens.Space.X2));
        }

        AddPickup(context, body, model, choices, store, onSaved, Gap);
        AddTime(context, body, model.Bedtime, choices.Bedtime ?? BedtimeRoutine.Assumed.Bedtime,
            time => HouseholdGroupModel.CheckBedtime(time) ?? store.Save(choices with { Bedtime = time }), onSaved, Gap);
        AddTime(context, body, model.Dinner, choices.DinnerStart ?? FamilyDinner.Assumed.Start,
            time => HouseholdGroupModel.CheckDinner(time, choices.DinnerLength ?? TimeSpan.FromMinutes(60))
                ?? store.Save(choices with { DinnerStart = time }), onSaved, Gap);
        AddCalendars(context, body, model, choices, store, onSaved, Gap);

        if (model.RosterStatus is not null)
        {
            // No household file: the adults can only be named here, and live in the setup file alone.
            var name = PaperUi.Field(context, "Add an adult (kept on this phone)");
            body.AddView(name, Gap(DesignTokens.Space.X4));
            body.AddView(
                PaperUi.SecondaryButton(context, model.AddLabel, () => onSaved(store.AddAdult(name.Text ?? string.Empty))),
                Gap(DesignTokens.Space.X2));
        }

        if (note is not null)
        {
            body.AddView(PaperUi.Secondary(context, note), Gap(DesignTokens.Space.X3));
        }

        return body;
    }

    private static void AddPickup(
        Context context, LinearLayout body, HouseholdGroupModel model, HouseholdChoices choices, HouseholdSetupStore store,
        Action<string> onSaved, Func<int, LinearLayout.LayoutParams> gap)
    {
        foreach (var row in model.Pickup)
        {
            var line = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            line.SetGravity(GravityFlags.CenterVertical);
            line.AddView(PaperUi.Body(context, row.Name), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
            if (row.Editable)
            {
                var name = row.Name;
                line.AddView(PaperUi.Segmented(context, ["Off pickup", "On pickup"], index =>
                {
                    var pickup = choices.Pickup.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                    pickup[name] = index == 1;
                    onSaved(store.Save(choices with { Pickup = pickup }));
                }, row.OnPickup ? 1 : 0));
            }
            else
            {
                line.AddView(PaperUi.Secondary(context, row.OnPickup ? "on pickup" : "not on pickup"));
            }

            body.AddView(line, gap(DesignTokens.Space.X3));
        }

        if (model.Pickup.Count > 0)
        {
            var names = model.Pickup.Select(row => row.Name).ToList();
            var selected = names.FindIndex(name => string.Equals(name, choices.DeliversHere, StringComparison.OrdinalIgnoreCase));
            body.AddView(PaperUi.Label(context, "Pages delivered here for"), gap(DesignTokens.Space.X4));
            body.AddView(
                PaperUi.Segmented(context, names, index => onSaved(store.Save(choices with { DeliversHere = names[index] })), Math.Max(selected, 0)),
                gap(DesignTokens.Space.X1));
            if (selected < 0)
            {
                body.AddView(PaperUi.Secondary(context, "Not chosen yet."));
            }
        }
    }

    private static void AddTime(
        Context context, LinearLayout body, HouseholdTimeRow row, TimeOnly current, Func<TimeOnly, string> change,
        Action<string> onSaved, Func<int, LinearLayout.LayoutParams> gap)
    {
        body.AddView(PaperUi.Label(context, row.Label), gap(DesignTokens.Space.X4));
        body.AddView(PaperUi.Secondary(context, row.Helper));

        var line = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        line.SetGravity(GravityFlags.CenterVertical);
        line.AddView(PaperUi.SecondaryButton(context, "Earlier", () => onSaved(change(current.AddMinutes(-StepMinutes)))));
        var value = PaperUi.Mono(context, row.IsAssumed ? row.Value + " (assumed)" : row.Value);
        value.SetPadding(PaperTheme.Dp(context, DesignTokens.Space.X3), 0, PaperTheme.Dp(context, DesignTokens.Space.X3), 0);
        line.AddView(value);
        line.AddView(PaperUi.SecondaryButton(context, "Later", () => onSaved(change(current.AddMinutes(StepMinutes)))));
        body.AddView(line, gap(DesignTokens.Space.X1));
    }

    private static void AddCalendars(
        Context context, LinearLayout body, HouseholdGroupModel model, HouseholdChoices choices, HouseholdSetupStore store,
        Action<string> onSaved, Func<int, LinearLayout.LayoutParams> gap)
    {
        body.AddView(PaperUi.Label(context, "Calendars"), gap(DesignTokens.Space.X4));
        foreach (var row in model.Calendars)
        {
            body.AddView(PaperUi.Body(context, row.Label), gap(DesignTokens.Space.X2));
            var field = PaperUi.MonoField(context, "Calendar id");
            field.Text = row.Id ?? string.Empty;
            body.AddView(field, gap(DesignTokens.Space.X1));
            var key = row.Key;
            body.AddView(PaperUi.SecondaryButton(context, "Save", () => onSaved(store.Save(WithCalendar(choices, key, field.Text ?? string.Empty)))),
                gap(DesignTokens.Space.X1));
            if (row.Id is null)
            {
                body.AddView(PaperUi.Secondary(context, row.StatusText));
            }
        }
    }

    private static HouseholdChoices WithCalendar(HouseholdChoices choices, string key, string id)
    {
        var trimmed = id.Trim();
        if (key == "family")
        {
            return choices with { FamilyCalendarId = trimmed.Length == 0 ? null : trimmed };
        }

        if (key == "children")
        {
            return choices with { ChildrenCalendarIds = trimmed.Length == 0 ? [] : trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) };
        }

        var name = key["adult:".Length..];
        var calendars = choices.AdultCalendars.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        calendars[name] = trimmed;
        return choices with { AdultCalendars = calendars };
    }
}
