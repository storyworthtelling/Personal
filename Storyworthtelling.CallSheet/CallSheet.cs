namespace Storyworthtelling.CallSheet;

/// <summary>
/// <b>The voice-line Call Sheet</b> (audit W10.3, owner's ruling): every place in the suite that speaks in the
/// owner's voice, in one list. To add a line, add an entry here and have the app ask for it by its app and
/// seat; to change what a line is asked for, edit its entry. The tests hold the rules every line keeps.
///
/// <para><b>Errors are never here.</b> A voice is for an app's own remarks, never its bad news: a failure is a
/// fact to act on and folds into the dog-ear in plain words (DESIGN.md §14). <c>CallSheetTests</c> fails on a
/// seat that reads as trouble.</para>
///
/// <para><b>How a line is reused</b> is the same everywhere: <see cref="SeatPlan"/>.</para>
/// </summary>
public static class CallSheet
{
    public static IReadOnlyList<VoiceLine> All { get; } =
    [
        // ── Flyleaf ─────────────────────────────────────────────────────────────────────────────────
        new("Flyleaf", "welcome", "welcome", 40, VoiceTier.Strong, VoiceFallback.Hide),

        // ── Score ───────────────────────────────────────────────────────────────────────────────────
        new("Score", "welcome", "welcome", 40, VoiceTier.Strong, VoiceFallback.Hide),
        new("Score", "crate-note",
            "a one-line note about a record in this week's crate, as the person who picked it out — "
            + "vary how it opens from one pick to the next (lead with an image, a memory, a detail "
            + "about the record itself, whatever fits) rather than starting with \"Picked this "
            + "because\" or any other fixed opening every time",
            40, VoiceTier.Strong, VoiceFallback.Hide),
        new("Score", "workout-pun",
            "one line about the track now playing, built on its title or its words, delivered flat — "
            + "a pun the speaker is not going to acknowledge having made",
            40, VoiceTier.Fast, VoiceFallback.Hide),
        new("Score", "workout-congratulation",
            "one line congratulating the listener out of all proportion to what they have done, "
            + "then quietly withdrawing the claim",
            40, VoiceTier.Fast, VoiceFallback.Hide),
        new("Score", "workout-coaching",
            "one line of encouragement from someone performing the part of a coach and not quite "
            + "committing to it — it lands on a flat retraction rather than a cheer",
            40, VoiceTier.Fast, VoiceFallback.Hide),

        // ── Post ────────────────────────────────────────────────────────────────────────────────────
        new("Post", "empty-mailbox",
            "the Mailbox is empty: everything that arrived has been dealt with; say the baseline line your "
            + "own way, keeping what it means and how far it goes. Add no facts and no instruction it does "
            + "not already give.",
            40, VoiceTier.Strong, VoiceFallback.Neutral),

        // ── Plate ───────────────────────────────────────────────────────────────────────────────────
        new("Plate", "empty-darkroom",
            "the Darkroom is empty: no picture is waiting to be chosen; say the baseline line your own way, "
            + "keeping what it means and how far it goes. Add no facts and no instruction it does not "
            + "already give.",
            40, VoiceTier.Strong, VoiceFallback.Neutral),
        new("Plate", "caption",
            "one short line over the pictures waiting in the Darkroom",
            15, VoiceTier.Fast, VoiceFallback.Hide,
            Guidance:
                "You write one short line for a photographer looking at their own unsorted pictures, "
                + "in their voice, described by the PROFILE that follows; never instructional. Say something "
                + "true about what is in front of them. Do not name or identify any person. Do not tell them "
                + "what to do with the pictures. Reply with the line only — no quotes, no preamble, at most "
                + "fifteen words."),

        // ── Pulp ────────────────────────────────────────────────────────────────────────────────────
        new("Pulp", "greeting", "a greeting under the Continue Watching header", 40, VoiceTier.Strong, VoiceFallback.Hide),
        new("Pulp", "why-this",
            "one line on a title page giving the reason this title was chosen for the viewer",
            40, VoiceTier.Strong, VoiceFallback.Hide),
        new("Pulp", "playlist-line", "one line about a playlist", 40, VoiceTier.Strong, VoiceFallback.Hide),
        new("Pulp", "child-greeting", "a gentle greeting for a child", 40, VoiceTier.Strong, VoiceFallback.Hide,
            VoiceAudience.Child),
        new("Pulp", "child-favorites", "one gentle line above a child's favorites", 40, VoiceTier.Strong,
            VoiceFallback.Hide, VoiceAudience.Child),
        new("Pulp", "empty-continue-watching", "an invitation on a home page with nothing started yet", 40,
            VoiceTier.Strong, VoiceFallback.Neutral, Neutral: "Nothing started yet. Discover has this week's stack."),
        new("Pulp", "empty-playlists", "an invitation to make a first playlist", 40, VoiceTier.Strong,
            VoiceFallback.Neutral, Neutral: "No playlists yet."),
        new("Pulp", "empty-playlist", "an invitation to put something on a new playlist", 40, VoiceTier.Strong,
            VoiceFallback.Neutral, Neutral: "Nothing on this playlist yet."),
        new("Pulp", "empty-recently-watched", "a line for a recently-watched page with nothing on it yet", 40,
            VoiceTier.Strong, VoiceFallback.Neutral, Neutral: "Nothing watched yet."),
        new("Pulp", "empty-child-favorites", "a gentle line for a child whose favorites page has nothing on it yet",
            40, VoiceTier.Strong, VoiceFallback.Neutral, VoiceAudience.Child, Neutral: "No favorites yet."),
        new("Pulp", "empty-child-watching", "a gentle line for a child whose watching page has nothing started yet",
            40, VoiceTier.Strong, VoiceFallback.Neutral, VoiceAudience.Child, Neutral: "Nothing started yet."),
        new("Pulp", "empty-child-playlists", "a gentle line for a child whose playlists page has none on it yet",
            40, VoiceTier.Strong, VoiceFallback.Neutral, VoiceAudience.Child, Neutral: "No playlists yet."),
        new("Pulp", "empty-calendar", "a line for a calendar with nothing coming up", 40, VoiceTier.Strong,
            VoiceFallback.Neutral, Neutral: "Nothing in the next two weeks."),
        new("Pulp", "empty-sports", "an invitation to follow a first team", 40, VoiceTier.Strong,
            VoiceFallback.Neutral, Neutral: "No teams followed yet."),
        new("Pulp", "stack-used-up",
            "a line for a week's stack that has all been seen, until the next one on Friday", 40, VoiceTier.Strong,
            VoiceFallback.Neutral, Neutral: "That's the week. More on Friday."),

        // ── Constitution (hides like every other app: owner's ruling, W10) ─────────────────────────
        new("Constitution", "today-top-line",
            "one line at the top of Today about the day's workout and tonight's dinner", 25, VoiceTier.Strong,
            VoiceFallback.Hide),
        new("Constitution", "rest-day-line",
            "one line at the top of Today on a rest day, about the rest and tonight's dinner", 25, VoiceTier.Strong,
            VoiceFallback.Hide),
        new("Constitution", "body-monthly",
            "one sentence on Body about how the month's signals moved against last month", 35, VoiceTier.Strong,
            VoiceFallback.Hide),
        new("Constitution", "signal-sentence",
            "one sentence on a signal's page setting its latest reading against the owner's own past", 30,
            VoiceTier.Strong, VoiceFallback.Hide),
        new("Constitution", "workout-encouragement",
            "one line of encouragement on a workout page, about progress since last time", 20, VoiceTier.Strong,
            VoiceFallback.Hide),
        new("Constitution", "meal-sheet-intro",
            "one line opening the week's dinner sheet, about the week ahead", 30, VoiceTier.Strong,
            VoiceFallback.Hide),

        // ── Index ───────────────────────────────────────────────────────────────────────────────────
        new("Index", "accounts", "a line above the accounts about how the household's debt has been moving", 24,
            VoiceTier.Strong, VoiceFallback.Hide, Guidance: IndexCommon + IndexNoAmounts),
        new("Index", "snowball", "a line above the debt-free date about how far the debt has come down", 24,
            VoiceTier.Strong, VoiceFallback.Hide, Guidance: IndexCommon + IndexNoAmounts),
        new("Index", "savings",
            "a line under the switch on the savings page about the pace the savings are growing at", 24,
            VoiceTier.Strong, VoiceFallback.Hide,
            Guidance: IndexCommon + " Mention only the monthly rate the FACTS give, as an amount per month. Never "
                + "state a balance, a total saved, a goal or target, a date, an account name or a bank name."),

        // ── Folio ───────────────────────────────────────────────────────────────────────────────────
        new("Folio", "desk-line", "a one-line note on the desk about how this book is coming along", 18,
            VoiceTier.Fast, VoiceFallback.Hide),
        new("Folio", "empty-desk", "a line for an empty desk, inviting a first manuscript", 40, VoiceTier.Strong,
            VoiceFallback.Hide),

        // ── Foreword ────────────────────────────────────────────────────────────────────────────────
        new("Foreword", "welcome", "welcome", 40, VoiceTier.Strong, VoiceFallback.Hide),
        new("Foreword", "brief-opening", "one line opening the morning brief, before the day's stories", 25,
            VoiceTier.Strong, VoiceFallback.Hide),
        new("Foreword", "day-narrator", "one line at the top of the day's calendar page, about the shape of the day", 25,
            VoiceTier.Strong, VoiceFallback.Hide),
        new("Foreword", "checklist-coach", "one line at the top of the day's checklist, about the tasks ahead", 25,
            VoiceTier.Strong, VoiceFallback.Hide),
        new("Foreword", "prayer-line", "one short line at the top of a private prayer page", 25, VoiceTier.Strong,
            VoiceFallback.Hide,
            Guidance:
                "You write one short line at the top of a private prayer page, in the voice of the person "
                + "whose page it is, described by the PROFILE that follows — the way they talk to themselves. It "
                + "introduces the list below it. Never more than one sentence. Never a prayer, never "
                + "advice, never scripture, never encouragement of the greeting-card kind. Do not name "
                + "anybody on the list. Respond with the line and nothing else."),
    ];

    private const string IndexCommon =
        "Write one short sentence in the owner's voice, as a line of copy at the top of a screen in a "
        + "personal finance notebook. Build it ONLY from the FACTS below: use no number the FACTS do not "
        + "give, and do not invent one. Say numbers in words, and percentages in words as in \"about one "
        + "percent\". No advice, no instruction, no judgement, no exclamation marks, no emoji, no quotation "
        + "marks. At most eighteen words. If the FACTS are not enough for a sentence, reply with nothing at all.";

    private const string IndexNoAmounts =
        " Speak only in percentages and counts. Never state a dollar amount, an account number, "
        + "a bank name or an account name.";

    private static readonly Dictionary<(string App, string Seat), VoiceLine> ByKey =
        All.ToDictionary(line => (line.App, line.Seat));

    /// <summary>The line for an app's seat. A seat not on the sheet is refused: a voiced line is a decision.</summary>
    public static VoiceLine For(string app, string seat) =>
        ByKey.TryGetValue((app, seat), out var line)
            ? line
            : throw new ArgumentOutOfRangeException(nameof(seat), $"{app}/{seat}",
                "This seat is not on the voice-line Call Sheet; add it there before an app asks for it.");

    /// <summary>Every line one app speaks, in the sheet's order.</summary>
    public static IEnumerable<VoiceLine> Of(string app) => All.Where(line => line.App == app);
}
