using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Music;
using Codex.Ui;

namespace Score;

/// <summary>
/// The rule editor: a name, whether a track has to satisfy every clause or just one,
/// the clauses themselves, and — the point of the whole screen — a live count of what
/// they currently select.
///
/// The count is the argument for building it this way. A rule set is an assertion
/// about a library you cannot see all of, so the only honest way to write one is to
/// watch the set shrink as you type and commit when it says what you meant. It
/// therefore recounts on every keystroke, which is affordable because
/// <see cref="PlaylistRules.Apply"/> is a pure pass over an in-memory catalog.
///
/// Everything expands in place (DESIGN.md §7): the pickers are accordions, not
/// dialogs, and the editor itself replaces the list rather than floating over it.
/// </summary>
public sealed class PlaylistEditor
{
    private static readonly PickerOption[] Fields =
    [
        new("Artist"), new("Album artist"), new("Album"), new("Genre"), new("Title"),
    ];

    private static readonly PickerOption[] Operators =
    [
        new("is"), new("contains"), new("is not"),
    ];

    private static readonly PickerOption[] Matches =
    [
        new("all of these", "every clause has to hold"),
        new("any of these", "one clause is enough"),
    ];

    private readonly Activity host;
    private readonly IReadOnlyList<Track> catalog;
    private readonly Playlist? existing;
    private readonly Action<Playlist> onSave;
    private readonly Action onCancel;

    private readonly List<PlaylistRule> rules = [];
    private RuleMatch match;
    private string name;

    private TextView count = null!;
    private LinearLayout clauses = null!;

    public PlaylistEditor(
        Activity host,
        IReadOnlyList<Track> catalog,
        Playlist? existing,
        Action<Playlist> onSave,
        Action onCancel)
    {
        this.host = host;
        this.catalog = catalog;
        this.existing = existing;
        this.onSave = onSave;
        this.onCancel = onCancel;

        var source = existing?.Rules ?? PlaylistRules.Empty;
        match = source.Match;
        rules.AddRange(source.Rules);
        name = existing?.Name ?? string.Empty;

        // A new playlist opens with one empty clause rather than a bare "Add a rule"
        // button: the shape of the thing is what tells you what a rule even is here.
        if (rules.Count == 0)
        {
            rules.Add(new PlaylistRule(TrackField.Artist, RuleOperator.Is, string.Empty));
        }
    }

    public View Build()
    {
        var column = new LinearLayout(host) { Orientation = Orientation.Vertical };
        var pad = PaperTheme.Dp(host, DesignTokens.Space.X3);
        column.SetPadding(pad, 0, pad, pad);

        var nameField = PaperUi.Field(host, "Playlist name");
        nameField.Text = name;
        nameField.TextChanged += (_, _) => name = nameField.Text ?? string.Empty;
        column.AddView(nameField);

        var matchPicker = new AccordionPicker(host, "Match", Matches, match == RuleMatch.All ? 0 : 1);
        matchPicker.Chosen += index =>
        {
            match = index == 0 ? RuleMatch.All : RuleMatch.Any;
            Recount();
        };
        column.AddView(matchPicker);

        clauses = new LinearLayout(host) { Orientation = Orientation.Vertical };
        column.AddView(clauses);
        FillClauses();

        column.AddView(PaperUi.SecondaryButton(host, "Add a rule", () =>
        {
            rules.Add(new PlaylistRule(TrackField.Artist, RuleOperator.Is, string.Empty));
            FillClauses();
            Recount();
        }));

        count = PaperUi.Secondary(host, string.Empty);
        column.AddView(count);
        Recount();

        var scroll = new ScrollView(host);
        scroll.AddView(column);

        var pane = new LinearLayout(host) { Orientation = Orientation.Vertical };
        pane.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        pane.AddView(PaperUi.BottomBar(host,
            PaperUi.QuietButton(host, "Cancel", onCancel),
            PaperUi.PrimaryButton(host, "Save", Save)));
        return pane;
    }

    // Rebuilt only when a clause is added or removed. A picker relabels itself and a
    // value field owns its own text, so neither needs the column rebuilt underneath
    // it — which would take the keyboard and the cursor with it mid-word.
    private void FillClauses()
    {
        clauses.RemoveAllViews();
        for (var i = 0; i < rules.Count; i++)
        {
            clauses.AddView(BuildClause(i));
        }
    }

    private View BuildClause(int index)
    {
        var row = new LinearLayout(host) { Orientation = Orientation.Vertical };
        row.SetPadding(0, PaperTheme.Dp(host, DesignTokens.Space.X2), 0, 0);

        var field = new AccordionPicker(host, "Where", Fields, (int)rules[index].Field);
        field.Chosen += chosen =>
        {
            rules[index] = rules[index] with { Field = (TrackField)chosen };
            Recount();
        };
        row.AddView(field);

        var op = new AccordionPicker(host, "…", Operators, (int)rules[index].Operator);
        op.Chosen += chosen =>
        {
            rules[index] = rules[index] with { Operator = (RuleOperator)chosen };
            Recount();
        };
        row.AddView(op);

        var value = PaperUi.Field(host, "Value");
        value.Text = rules[index].Value;
        value.TextChanged += (_, _) =>
        {
            rules[index] = rules[index] with { Value = value.Text ?? string.Empty };
            Recount();
        };
        row.AddView(value);

        // Removing a clause is not destructive — nothing exists yet to lose — so it
        // is a quiet button and not an undo bar.
        if (rules.Count > 1)
        {
            row.AddView(PaperUi.QuietButton(host, "Remove this rule", () =>
            {
                rules.RemoveAt(index);
                FillClauses();
                Recount();
            }));
        }

        return row;
    }

    private void Recount()
    {
        var selected = PlaylistRules.Apply(new PlaylistRules(match, rules), catalog).Count;
        count.Text = selected switch
        {
            0 => "Nothing yet — fill in a value.",
            1 => "1 track",
            _ => $"{selected} tracks",
        };
    }

    private void Save()
    {
        var title = string.IsNullOrWhiteSpace(name) ? "Untitled playlist" : name.Trim();
        var id = existing?.Id ?? Guid.NewGuid().ToString("n");
        onSave(Playlist.FromRules(id, title, new PlaylistRules(match, [.. rules])));
    }
}
