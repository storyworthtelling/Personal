using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <summary>
/// One line on the card. Lead marks the primary intent. Detail is a second, quieter line
/// beneath the label — a notice needs to say what happened, not just name itself, and Hue
/// lets a notice carry its own functional colour (§13).
///
/// <para>There is no destructive weight (audit W8.2): a delete on a card happens at once and
/// is undone from the dog-ear, so painting it red would warn about something that cannot
/// hurt. The same reason §7 has no Destructive button.</para>
/// </summary>
public sealed record CardAction(
    string Label,
    Action Invoke,
    bool Lead = false,
    string? Detail = null,
    Color? Hue = null);

/// <summary>
/// A button along a card's foot. The primary goes last (right) and takes the room left over,
/// the same rule as the pinned bar; everything else is quiet and sized to its words.
/// </summary>
public sealed record CardButton(string Label, Action Invoke, bool Primary = false);

/// <summary>
/// Item actions (DESIGN.md §7): long-press opens a <b>centred paper card</b> over a
/// PaperDeep haze; tapping outside dismisses it. This is the only overflow the app
/// has — §9 bans the kebab menu — and it is <b>never a confirm dialog</b>: the
/// entries here do things, and anything destructive happens immediately with an
/// undo dog-ear rather than asking first.
///
/// Overlay it on a FrameLayout host, like the dog-ear and the undo bar.
/// </summary>
public static class ActionCard
{
    public static void Show(
        Context context, FrameLayout host, string title, string? subtitle, params CardAction[] actions)
    {
        var rows = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X4);
        foreach (var action in actions)
        {
            rows.AddView(BuildRow(context, host, action, pad));
        }

        Present(context, host, title, subtitle, Capped(context, host, rows), bodyPadded: false);
    }

    /// <summary>
    /// A card holding something other than a list of actions — a few words to read, a field,
    /// a set of ticks — with its buttons along the foot. This is what replaces a platform
    /// dialog (audit W8.2): the page stays where it was, behind a paper haze, and tapping the
    /// haze puts the card away.
    /// </summary>
    public static void ShowBody(
        Context context, FrameLayout host, string title, string? subtitle, View body, params CardButton[] buttons)
    {
        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        column.AddView(Capped(context, host, body));
        if (buttons.Length > 0)
        {
            column.AddView(ButtonRow(context, host, buttons, keepOpen: null));
        }

        Present(context, host, title, subtitle, column, bodyPadded: true);
    }

    /// <summary>A card that shows a passage of text — a log, a note — and the buttons to act on it.</summary>
    public static void ShowText(
        Context context, FrameLayout host, string title, string text, params CardButton[] buttons)
    {
        var body = PaperUi.Secondary(context, text);
        body.SetTextIsSelectable(true);
        ShowBody(context, host, title, null, body, buttons);
    }

    /// <summary>
    /// Asks for a few words on a card. <paramref name="submit"/> gets the text and returns
    /// <c>null</c> when it was taken (the card closes) or a sentence saying why not, which is
    /// shown under the field in the Problem hue while the card stays open — a refusal said
    /// where the words are, never in a toast. Returns the field so a caller can set its input
    /// type. <paramref name="also"/> is one more quiet button beside Cancel (a reply's Attach).
    /// </summary>
    public static EditText AskText(
        Context context,
        FrameLayout host,
        string title,
        string? subtitle,
        string hint,
        string? initial,
        string actionLabel,
        Func<string, string?> submit,
        bool multiLine = false,
        CardButton? also = null)
    {
        var field = PaperUi.Field(context, hint);
        if (!string.IsNullOrEmpty(initial))
        {
            field.Text = initial;
            field.SetSelection(initial.Length);
        }

        if (multiLine)
        {
            field.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextFlagMultiLine
                | Android.Text.InputTypes.TextFlagCapSentences;
            field.SetMinLines(3);
        }

        var problem = PaperUi.Secondary(context, string.Empty);
        problem.SetTextColor(Problem);
        problem.Visibility = ViewStates.Gone;

        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        column.AddView(field);
        column.AddView(problem);

        View? self = null;
        var outer = new LinearLayout(context) { Orientation = Orientation.Vertical };
        outer.AddView(column);
        outer.AddView(ButtonRow(context, host,
            also is null
                ? [new CardButton("Cancel", () => { }), new CardButton(actionLabel, () => { }, Primary: true)]
                : [new CardButton("Cancel", () => { }), also, new CardButton(actionLabel, () => { }, Primary: true)],
            keepOpen: () =>
            {
                var said = submit(field.Text ?? string.Empty);
                if (said is null)
                {
                    return false;
                }

                problem.Text = said;
                problem.Visibility = ViewStates.Visible;
                return true;
            },
            self: () => self));

        self = Present(context, host, title, subtitle, outer, bodyPadded: true);

        field.RequestFocus();
        field.Post(() =>
            (context.GetSystemService(Context.InputMethodService) as Android.Views.InputMethods.InputMethodManager)
                ?.ShowSoftInput(field, Android.Views.InputMethods.ShowFlags.Implicit));
        return field;
    }

    /// <summary>
    /// Asks which of several things, any number of them, with a tick beside each (§7's checkbox),
    /// all ticked to begin with unless <paramref name="startPicked"/> says otherwise (one flag
    /// per item).
    /// </summary>
    public static void ChooseMany(
        Context context,
        FrameLayout host,
        string title,
        string? subtitle,
        IReadOnlyList<string> items,
        string actionLabel,
        Action<IReadOnlyList<int>> picked,
        IReadOnlyList<bool>? startPicked = null)
    {
        var chosen = startPicked?.ToArray() ?? Enumerable.Repeat(true, items.Count).ToArray();
        var rows = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var vertical = PaperTheme.Dp(context, DesignTokens.Space.X3);
        var size = PaperTheme.Dp(context, DesignTokens.Shape.CheckboxDp);

        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterVertical);
            row.SetPadding(0, vertical, 0, vertical);
            row.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));

            var tick = new Checkbox(context);
            tick.SetPicked(chosen[index]);
            row.AddView(tick, new LinearLayout.LayoutParams(size, size)
            {
                MarginEnd = PaperTheme.Dp(context, DesignTokens.Space.X3),
            });
            row.AddView(PaperUi.Body(context, items[index]), new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

            row.Clickable = true;
            row.Click += (_, _) =>
            {
                chosen[index] = !chosen[index];
                tick.SetPicked(chosen[index]);
            };
            rows.AddView(row);
        }

        ShowBody(context, host, title, subtitle, rows,
            new CardButton("Cancel", () => { }),
            new CardButton(actionLabel, () => picked(
                Enumerable.Range(0, items.Count).Where(i => chosen[i]).ToList()), Primary: true));
    }

    private static View Present(
        Context context, FrameLayout host, string title, string? subtitle, View body, bool bodyPadded)
    {
        Dismiss(host);

        // The haze: a dimmed PaperDeep, not a black scrim. Paper does not go dark.
        var scrim = new TouchSinkFrame(context) { Tag = HostTag };
        scrim.SetBackgroundColor(new Color(
            PaperTheme.PaperDeep.R, PaperTheme.PaperDeep.G, PaperTheme.PaperDeep.B, HazeAlpha));
        scrim.Click += (_, _) => Dismiss(host);

        // A touch sink: taps on the card are swallowed so they do not fall through and dismiss it, without
        // a click handler that would make the tap watch take the whole card for a control.
        var card = new TouchSinkColumn(context);
        card.Background = PaperUi.Outline(
            context, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);

        var pad = PaperTheme.Dp(context, DesignTokens.Space.X4);
        card.SetPadding(0, pad, 0, PaperTheme.Dp(context, DesignTokens.Space.X2));

        AddHeader(context, card, title, subtitle, pad);

        if (bodyPadded)
        {
            body.SetPadding(pad, PaperTheme.Dp(context, DesignTokens.Space.X3), pad, 0);
        }

        card.AddView(body);

        var edge = PaperTheme.Dp(context, DesignTokens.Shape.ScreenEdgeDp);
        var layout = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center);
        layout.SetMargins(edge, 0, edge, 0);
        scrim.AddView(card, layout);

        scrim.Alpha = 0f;
        host.AddView(scrim, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        scrim.Animate()!.Alpha(1f).SetDuration(DesignTokens.Motion.StandardMs).Start();
        return scrim;
    }

    // A long list (every installed app, every feed in a file) scrolls inside the card rather
    // than pushing its buttons off the screen: the card never grows past most of the host.
    private static View Capped(Context context, FrameLayout host, View content)
    {
        var scroll = new CappedScroll(context, () => (int)(host.Height * MaxCardShare));
        scroll.AddView(content);
        return scroll;
    }

    // keepOpen runs the primary's own work and says whether the card must stay; because that work
    // may open the next card, the card then closes only ITSELF (self), never whatever is on top.
    private static View ButtonRow(
        Context context, FrameLayout host, CardButton[] buttons, Func<bool>? keepOpen, Func<View?>? self = null)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(0, PaperTheme.Dp(context, DesignTokens.Space.X3), 0, 0);

        var gap = PaperTheme.Dp(context, DesignTokens.Space.X2);
        var ordered = buttons.Where(b => !b.Primary).Concat(buttons.Where(b => b.Primary)).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var spec = ordered[i];
            Action press = () =>
            {
                if (spec.Primary && keepOpen is not null)
                {
                    if (!keepOpen() && self?.Invoke() is { Parent: { } parent } card && ReferenceEquals(parent, host))
                    {
                        host.RemoveView(card);
                    }

                    return;
                }

                Dismiss(host);
                spec.Invoke();
            };

            var button = spec.Primary
                ? PaperUi.PrimaryButton(context, spec.Label, press)
                : PaperUi.QuietButton(context, spec.Label, press);
            var layout = spec.Primary
                ? new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 }
                : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
            if (i > 0)
            {
                layout.MarginStart = gap;
            }

            row.AddView(button, layout);
        }

        return row;
    }

    private sealed class CappedScroll(Context context, Func<int> cap) : ScrollView(context)
    {
        protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
        {
            var limit = cap();
            if (limit > 0)
            {
                heightMeasureSpec = MeasureSpec.MakeMeasureSpec(limit, MeasureSpecMode.AtMost);
            }

            base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
        }
    }

    private static void AddHeader(
        Context context, LinearLayout card, string title, string? subtitle, int pad)
    {
        var header = new LinearLayout(context) { Orientation = Orientation.Vertical };
        header.SetPadding(pad, 0, pad, PaperTheme.Dp(context, DesignTokens.Space.X3));

        var heading = PaperUi.Heading(context, title);
        heading.SetSingleLine(true);
        heading.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        header.AddView(heading);

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var secondary = PaperUi.Secondary(context, subtitle);
            secondary.SetSingleLine(true);
            secondary.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            header.AddView(secondary);
        }

        card.AddView(header);
        card.AddView(Hairline(context));
    }

    private static View BuildRow(Context context, FrameLayout host, CardAction action, int pad)
    {
        var label = PaperUi.Body(context, action.Label);
        label.SetTextColor(action.Hue ?? (action.Lead ? PaperTheme.ActionText : PaperTheme.Ink));

        if (action.Lead || action.Hue is not null)
        {
            label.SetTypeface(PaperTheme.SansSemibold(context), TypefaceStyle.Normal);
        }

        var vertical = PaperTheme.Dp(context, DesignTokens.Space.X3);
        var row = new LinearLayout(context) { Orientation = Orientation.Vertical };
        row.SetPadding(pad, vertical, pad, vertical);
        row.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));
        row.AddView(label);

        // A notice has to say what happened, not just name itself. An action with nothing
        // to add stays a single line, so the card is unchanged everywhere it was before.
        if (!string.IsNullOrWhiteSpace(action.Detail))
        {
            row.AddView(PaperUi.Secondary(context, action.Detail));
        }

        row.Clickable = true;
        row.Click += (_, _) =>
        {
            Dismiss(host);
            action.Invoke();
        };

        return row;
    }

    private static View Hairline(Context context)
    {
        var rule = new View(context);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        rule.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp));
        return rule;
    }

    public static void Dismiss(FrameLayout host)
    {
        var existing = host.FindViewWithTag(HostTag);
        if (existing is not null)
        {
            host.RemoveView(existing);
        }
    }

    // Enough to settle the page back without hiding it — you are still looking at
    // the row you long-pressed, which is the point of a card over a dialog.
    private const byte HazeAlpha = 219;

    // How much of the host a card may take before its body scrolls.
    private const double MaxCardShare = 0.7;

    private static readonly Color Problem = Color.ParseColor(DesignTokens.ProblemText);

    private static readonly Java.Lang.String HostTag = new("paper-action-card");
}
