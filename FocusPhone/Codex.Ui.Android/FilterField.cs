using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <summary>
/// The live filter (DESIGN.md §7). It <b>narrows the list it sits above</b> and
/// never navigates to a results screen, which is why it is pinned directly under
/// the screen header, above any sort control, and does not scroll away.
///
/// It appears on any list that cannot fit one screen and is omitted where the
/// content already does — that decision belongs to the screen, not to this control.
///
/// The field takes the area's <b>identity border</b> while it holds a query, so a
/// list that is showing you less than it has says so without a banner. The count
/// beside it reports <i>matches of total</i> rather than a total, because while a
/// filter is on, the total is the one number that is not true.
/// </summary>
public class FilterField : LinearLayout
{
    private readonly EditText input;
    private readonly TextView clear;
    private readonly Color idle;
    private readonly Color active;

    public FilterField(Context context, string hint, Color identityHue) : base(context)
    {
        idle = PaperTheme.Hairline;
        active = identityHue;

        Orientation = Orientation.Horizontal;
        SetGravity(GravityFlags.CenterVertical);
        Background = PaperUi.Outline(context, PaperTheme.Paper, idle, DesignTokens.Shape.RadiusInput);
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X3);
        SetPadding(pad, 0, pad, 0);
        SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));

        input = new EditText(context)
        {
            Hint = hint,
            InputType = InputTypes.ClassText | InputTypes.TextVariationFilter,
        };
        input.SetSingleLine(true);
        input.SetTextSize(Android.Util.ComplexUnitType.Sp, DesignTokens.TypeSp.Body);
        input.SetTextColor(PaperTheme.Ink);
        input.SetHintTextColor(PaperTheme.InkMuted);
        input.SetTypeface(PaperTheme.SansRegular(context), TypefaceStyle.Normal);
        input.SetBackgroundColor(Color.Transparent);
        input.SetPadding(0, 0, 0, 0);
        AddView(input, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        clear = PaperUi.Body(context, "×");
        clear.SetTextColor(PaperTheme.InkMuted);
        clear.Visibility = ViewStates.Gone;
        var hit = PaperTheme.Dp(context, DesignTokens.Space.X2);
        clear.SetPadding(hit, hit, 0, hit);
        clear.Click += (_, _) => Clear();
        AddView(clear);

        input.TextChanged += (_, _) =>
        {
            var query = Query;
            var has = query.Length > 0;
            clear.Visibility = has ? ViewStates.Visible : ViewStates.Gone;
            Background = PaperUi.Outline(context, PaperTheme.Paper,
                has ? active : idle, DesignTokens.Shape.RadiusInput);
            QueryChanged?.Invoke(query);
        };

        // Leaving the field (or pressing its keyboard action, which drops focus) is the
        // moment a deferred filter takes effect. The keyboard action is handled here so a
        // Done press commits the same way a tap elsewhere does.
        input.FocusChange += (_, e) =>
        {
            if (!e.HasFocus)
            {
                Committed?.Invoke(Query);
            }
        };
        input.EditorAction += (_, _) => input.ClearFocus();
    }

    /// <summary>Fires on every keystroke. Screens that filter live subscribe to this.</summary>
    public event Action<string>? QueryChanged;

    /// <summary>
    /// Fires when the owner has finished with the field: it lost focus, or its keyboard
    /// action was pressed. Screens that would rather not re-render on every letter filter
    /// on this instead, and take the text from <see cref="Query"/> when it fires.
    /// </summary>
    public event Action<string>? Committed;

    public string Query => input.Text?.Trim() ?? string.Empty;

    /// <summary>
    /// Empties the field. Clearing is a decision, not an edit in progress, so it commits at
    /// once rather than waiting for focus to leave.
    /// </summary>
    public void Clear()
    {
        input.Text = string.Empty;
        Committed?.Invoke(Query);
    }

    /// <summary>
    /// Restores a query the screen was already holding — after a rebuild the field
    /// did not cause. Fires <see cref="QueryChanged"/> like any other edit, so the
    /// list it governs cannot end up showing something other than what the field says.
    /// </summary>
    public void SetQuery(string text)
    {
        if (Query != text)
        {
            input.Text = text;
        }
    }

    /// <summary>
    /// Does this string match? Case- and accent-insensitive substring, which is
    /// what a filter over titles and artists actually wants — someone typing
    /// "bartok" means Bartók, and nobody types a diacritic to find a record.
    /// </summary>
    public static bool Matches(string? candidate, string query) =>
        FilterMatch.Matches(candidate, query);
}
