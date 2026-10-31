using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Household;
using Constitution.Domain.Settings;

namespace Constitution.Settings;

/// <summary>What the owner has typed on the person sheet. Held by the page, not by the views, so a redraw does not lose it.</summary>
public sealed class PersonSheetInput(string name, bool onPickup, string address)
{
    public string Name { get; set; } = name;

    public bool OnPickup { get; set; } = onPickup;

    public string Address { get; set; } = address;

    public static PersonSheetInput From(RosterEntry entry) => new(entry.Name, entry.OnPickup, entry.GoogleAddress ?? string.Empty);
}

/// <summary>
/// The person sheet's page and its pinned bar, painted from <see cref="PersonSheetModel"/> (book p.17). The page opens with the Serif
/// header, then who they are and the pickup facts the roster already holds; the owner's fields appear only when the model says the
/// sheet can be edited. The bar paints exactly the actions the model lists, so a sheet that offers no removal has no such button to
/// hide. A sentence under the header is the one place any outcome is said: no dialog, no toast, no spinner.
/// </summary>
public static class PersonSheetView
{
    public static View Build(Context context, PersonSheetModel model, PersonSheetInput input, string? status)
    {
        var scroll = new ScrollView(context);
        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        LinearLayout.LayoutParams Gap(int top) => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(context, top),
        };

        column.AddView(PaperUi.ScreenHeader(context, model.Title, PaperTheme.Ink));
        column.AddView(PaperUi.Body(context, model.RoleLine));
        if (model.PickupLine is { } pickup)
        {
            column.AddView(PaperUi.Secondary(context, pickup), Gap(DesignTokens.Space.X1));
        }

        if (model.CalendarLine is { } calendar)
        {
            column.AddView(PaperUi.Secondary(context, calendar), Gap(DesignTokens.Space.X1));
        }

        if (model.ReadOnlyNote is { } readOnly)
        {
            column.AddView(PaperUi.Secondary(context, readOnly), Gap(DesignTokens.Space.X3));
        }

        if (model.CanEdit)
        {
            AddFields(context, column, model, input, Gap);
        }

        if (model.KeepNote is { } keep)
        {
            column.AddView(PaperUi.Secondary(context, keep), Gap(DesignTokens.Space.X4));
        }

        if (status is not null)
        {
            column.AddView(PaperUi.Secondary(context, status), Gap(DesignTokens.Space.X3));
        }

        scroll.AddView(column);
        return scroll;
    }

    /// <summary>The pinned bottom bar: one button for each action the model lists, none for one it does not.</summary>
    public static View BuildBar(Context context, PersonSheetModel model, Action<PersonSheetAction> act, bool busy)
    {
        var buttons = model.Actions.Select(action =>
        {
            var label = PersonSheetModel.LabelOf(action);
            var button = action switch
            {
                PersonSheetAction.Remove => PaperUi.DestructiveButton(context, label, () => act(action)),
                PersonSheetAction.Save => PaperUi.PrimaryButton(context, label, () => act(action)),
                _ => PaperUi.SecondaryButton(context, label, () => act(action)),
            };
            button.Enabled = !busy;
            return (View)button;
        });
        return PaperUi.BottomBar(context, buttons.ToArray());
    }

    private static void AddFields(
        Context context, LinearLayout column, PersonSheetModel model, PersonSheetInput input, Func<int, LinearLayout.LayoutParams> gap)
    {
        column.AddView(PaperUi.Label(context, "Name"), gap(DesignTokens.Space.X4));
        var name = PaperUi.Field(context, "Name");
        name.Text = input.Name;
        name.TextChanged += (_, _) => input.Name = name.Text ?? string.Empty;
        column.AddView(name, gap(DesignTokens.Space.X1));

        if (model.PickupLine is not null)
        {
            column.AddView(PaperUi.Label(context, "Pickup"), gap(DesignTokens.Space.X4));
            column.AddView(
                PaperUi.Segmented(context, ["Off pickup", "On pickup"], index => input.OnPickup = index == 1, input.OnPickup ? 1 : 0),
                gap(DesignTokens.Space.X1));
        }

        column.AddView(PaperUi.Label(context, "Google address"), gap(DesignTokens.Space.X4));
        var address = PaperUi.MonoField(context, "Calendar address, if they have one");
        address.Text = input.Address;
        address.TextChanged += (_, _) => input.Address = address.Text ?? string.Empty;
        column.AddView(address, gap(DesignTokens.Space.X1));
    }
}
