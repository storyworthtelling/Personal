using System.Text.Json;
using System.Text.Json.Serialization;
using Folio.Domain.Analysis;
using Folio.Domain.Text;

namespace Folio.Domain.Reading;

/// <summary>
/// One line of the round table (release H, R40): who spoke, in what words, and whom they were answering,
/// if anyone. <see cref="Speaker"/> is a reader's persona id or <see cref="RoundTablePlan.HostId"/>.
/// </summary>
public sealed record RoundTableTurn(string Speaker, string Words)
{
    public string Answering { get; init; } = string.Empty;

    /// <summary>On a host turn, the part of the show it belongs to: the splits, or an agenda topic.</summary>
    public string Topic { get; init; } = string.Empty;
}

/// <summary>One item of the round table's open discussion (owner's ruling), and the read that put it there.</summary>
public sealed record AgendaItem(string Topic, string Why);

/// <summary>The two parts of the show, in order (owner's ruling): the disagreements, then the open discussion.</summary>
public enum RoundTablePhase
{
    Disagreements,
    Discussion,
}

/// <summary>
/// The open discussion's agenda (owner's ruling): structure, themes and characters always, then whatever else
/// the earlier reads found something to say about, in the order the sections read. Each item carries the
/// section's own summary, so the host knows why it is there.
/// </summary>
public static class RoundTableAgenda
{
    private static readonly (string PersonaId, string Topic)[] Others =
    [
        ("story-integrity", "Story integrity"),
        ("setup-and-payoff", "Setup and payoff"),
        ("pacing", "Pacing"),
        ("voice-consistency", "Voice"),
        ("series-continuity", "Series continuity"),
        ("length", "Length"),
    ];

    public static IReadOnlyList<AgendaItem> For(IReadOnlyList<SectionRead> sections)
    {
        string Why(string personaId) =>
            sections.FirstOrDefault(section => section.PersonaId == personaId) is { HasContent: true } read
                ? read.Summary.Trim()
                : string.Empty;

        var agenda = new List<AgendaItem>
        {
            new("Structure", Why("structure")),
            new("Themes", Why("theme-hierarchy")),
            new("Characters", string.Empty),
        };
        agenda.AddRange(Others
            .Where(other => sections.Any(section => section.PersonaId == other.PersonaId && section.HasContent))
            .Select(other => new AgendaItem(other.Topic, Why(other.PersonaId))));
        return agenda;
    }
}

/// <summary>What the host has put to the next speaker, and whom they are to answer, if anyone.</summary>
public sealed record RoundTableCue(string Speaker, string Ask)
{
    public string Answering { get; init; } = string.Empty;
}

/// <summary>Where the readers ended up (R32): what they agreed on, and where they still differ. No ruling.</summary>
public sealed record RoundTableSummary(IReadOnlyList<string> Agreed, IReadOnlyList<string> StillDiffer);

/// <summary>The host's last words and where the readers ended up.</summary>
public sealed record RoundTableClosing(string Says, RoundTableSummary Summary);

/// <summary>Two scored readers three or more points apart (R30): each owes the other one answer.</summary>
public sealed record RoundTableSplit(string First, string Second);

/// <summary>
/// The host's move (R40): what it says aloud, whom it hands to and what it asks them, whom they are to
/// answer, or that it would like to close.
/// </summary>
public sealed record HostMove(string Says, string Speaker, string Ask, string Answering, bool Close)
{
    /// <summary>The agenda topic this move is on, in the open discussion; empty otherwise.</summary>
    public string Topic { get; init; } = string.Empty;
}

public enum RoundTableStepKind
{
    HostTurn,
    ReaderTurn,
    Close,
    Done,
}

/// <summary>The next thing the round table needs: a host turn, a reader's turn on a cue, the close, or nothing.</summary>
public sealed record RoundTableStep(RoundTableStepKind Kind, RoundTableCue? Cue = null);

/// <summary>
/// The round table as it stands (release H): the transcript, the cue waiting for its speaker, whether the
/// host has asked to close, and, once closed, the summary. Persisted after every turn, so a worker stopped
/// mid-show resumes at the next turn.
/// </summary>
public sealed record RoundTable(IReadOnlyList<RoundTableTurn> Turns)
{
    public static RoundTable Empty { get; } = new([]);

    public RoundTableCue? Next { get; init; }

    public bool CloseAsked { get; init; }

    public RoundTableSummary? Summary { get; init; }

    /// <summary>The open discussion's agenda, fixed when the show begins.</summary>
    public IReadOnlyList<AgendaItem> Agenda { get; init; } = [];

    /// <summary>The agenda topics raised in the open discussion so far.</summary>
    public IReadOnlyList<string> Covered { get; init; } = [];

    /// <summary>The turn cap, fixed when the show begins; zero when it was not (a table from before the agenda).</summary>
    public int Cap { get; init; }

    [JsonIgnore]
    public bool Closed => Summary is not null;
}

/// <summary>
/// The rules of the round table (release H, R28–R33, R40). The model decides what is said; these decide
/// who may say it, what is owed, and when it ends:
/// <list type="bullet">
/// <item>The host opens, and speaks between readers, handing to whom it names.</item>
/// <item>Every split owes two rebuttals, one each way (R30); the host may not close while one is owed.</item>
/// <item>The show runs in two parts (owner's ruling): the disagreements while any rebuttal is owed, then an
/// open discussion through the agenda. The host may not close while a topic is unraised; a close it asks
/// for early raises the next topic instead.</item>
/// <item>The show is capped by the manuscript's length (R33, R40) — a cap, not a quota, and the agenda wins
/// over it (owner's ruling: running long is fine). Past it, owed rebuttals are cued straight, every unraised
/// topic still gets a host turn and a reader's answer, and then the show closes.</item>
/// </list>
/// </summary>
public static class RoundTablePlan
{
    public const string HostId = "host";

    public const string HostName = "Tae Boll";

    /// <summary>The host's voice (R42): how it talks. It never rules on a split (R32).</summary>
    public const string HostVoice =
        "warm, curious and quick; a podcast host who keeps the conversation moving, hands the floor to whoever was just "
        + "named or disagreed with, asks the follow-up a listener would, and never says who is right";

    /// <summary>The most turns, host's included, for a manuscript of this length (R40).</summary>
    public static int TurnCap(StoryLength length) => length switch
    {
        StoryLength.ShortStory => 15,
        StoryLength.Novella => 30,
        _ => 50,
    };

    /// <summary>The heading of the show's first part.</summary>
    public const string SplitsTopic = "Where they split";

    /// <summary>A show about to begin: its agenda and cap fixed, so they hold however long it runs.</summary>
    public static RoundTable Begin(IReadOnlyList<AgendaItem> agenda, int cap) =>
        RoundTable.Empty with { Agenda = agenda, Cap = cap };

    /// <summary>Which part of the show this is: the disagreements while any rebuttal is owed, then the discussion.</summary>
    public static RoundTablePhase Phase(RoundTable table, IReadOnlyList<Review> reviews) =>
        Owed(table, reviews).Count > 0 ? RoundTablePhase.Disagreements : RoundTablePhase.Discussion;

    /// <summary>The agenda topics not yet raised in the open discussion.</summary>
    public static IReadOnlyList<AgendaItem> Uncovered(RoundTable table) =>
        [.. table.Agenda.Where(item => !table.Covered.Contains(item.Topic, StringComparer.OrdinalIgnoreCase))];

    /// <summary>The name a speaker goes by in the transcript: the host's, or the reader's invented name.</summary>
    public static string Name(string speaker) =>
        speaker == HostId
            ? HostName
            : PersonaCatalogue.Readers.FirstOrDefault(persona => persona.Id == speaker) is { } persona
                ? persona.ReaderName ?? persona.Name
                : speaker;

    /// <summary>Every pair of scored readers three or more points apart (R30), in panel order.</summary>
    public static IReadOnlyList<RoundTableSplit> Splits(IReadOnlyList<Review> reviews)
    {
        var scored = reviews.Where(review => review.Score > 0).ToList();
        var splits = new List<RoundTableSplit>();
        for (var i = 0; i < scored.Count; i++)
            for (var j = i + 1; j < scored.Count; j++)
                if (Math.Abs(scored[i].Score - scored[j].Score) >= 3)
                    splits.Add(new RoundTableSplit(scored[i].PersonaId, scored[j].PersonaId));
        return splits;
    }

    /// <summary>The rebuttals still owed (R30): a cue for each reader in a split who has not yet answered the other.</summary>
    public static IReadOnlyList<RoundTableCue> Owed(RoundTable table, IReadOnlyList<Review> reviews)
    {
        var owed = new List<RoundTableCue>();
        foreach (var split in Splits(reviews))
        {
            foreach (var (speaker, other) in new[] { (split.First, split.Second), (split.Second, split.First) })
            {
                if (!table.Turns.Any(turn => turn.Speaker == speaker && turn.Answering == other))
                {
                    owed.Add(new RoundTableCue(speaker,
                        $"You and {Name(other)} scored this book well apart. Answer {Name(other)} directly on where you differ.")
                    {
                        Answering = other,
                    });
                }
            }
        }
        return owed;
    }

    /// <summary>What the round table needs next.</summary>
    public static RoundTableStep Next(RoundTable table, IReadOnlyList<Review> reviews, StoryLength length)
    {
        if (table.Closed)
            return new RoundTableStep(RoundTableStepKind.Done);
        if (table.Next is { } cue)
            return new RoundTableStep(RoundTableStepKind.ReaderTurn, cue);
        if (table.CloseAsked)
            return new RoundTableStep(RoundTableStepKind.Close);
        var cap = table.Cap > 0 ? table.Cap : TurnCap(length);
        if (table.Turns.Count >= cap)
        {
            if (Owed(table, reviews) is { Count: > 0 } owed)
                return new RoundTableStep(RoundTableStepKind.ReaderTurn, owed[0]);
            return Uncovered(table).Count > 0
                ? new RoundTableStep(RoundTableStepKind.HostTurn)
                : new RoundTableStep(RoundTableStepKind.Close);
        }
        return new RoundTableStep(RoundTableStepKind.HostTurn);
    }

    /// <summary>
    /// The table after the host's move: its words in the transcript, then the cue it asked for — or, when it
    /// names nobody on the panel, the quietest reader; or, when it asks to close with a rebuttal still owed,
    /// that rebuttal.
    /// </summary>
    public static RoundTable Apply(RoundTable table, HostMove move, IReadOnlyList<Review> reviews)
    {
        ArgumentNullException.ThrowIfNull(move);
        var panel = reviews.Select(review => review.PersonaId).ToList();
        var phase = Phase(table, reviews);
        var pastCap = table.Cap > 0 && table.Turns.Count >= table.Cap;
        string Quietest(RoundTable at) =>
            panel.OrderBy(id => at.Turns.Count(turn => turn.Speaker == id)).ThenBy(panel.IndexOf).First();

        // In the discussion, the topic this move is on counts once it is one the agenda holds and has not had.
        // Past the cap, a host turn always raises the next unraised topic, whatever it said, so the show ends.
        AgendaItem? raised = null;
        if (phase == RoundTablePhase.Discussion)
        {
            var uncovered = Uncovered(table);
            raised = uncovered.FirstOrDefault(item => string.Equals(item.Topic, move.Topic?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (raised is null && (pastCap || move.Close) && uncovered.Count > 0)
                raised = uncovered[0];
        }
        var covered = raised is null ? table.Covered : [.. table.Covered, raised.Topic];
        var topic = phase == RoundTablePhase.Disagreements
            ? SplitsTopic
            : raised?.Topic ?? table.Turns.LastOrDefault(turn => turn.Speaker == HostId)?.Topic ?? string.Empty;
        var spoken = string.IsNullOrWhiteSpace(move.Says)
            ? table with { Covered = covered }
            : table with { Covered = covered, Turns = [.. table.Turns, new RoundTableTurn(HostId, move.Says.Trim()) { Topic = topic }] };

        if (move.Close)
        {
            var owed = Owed(spoken, reviews);
            if (owed.Count > 0)
                return spoken with { Next = owed[0] };
            if (raised is null)
                return spoken with { CloseAsked = true, Next = null };

            // A close asked for with a topic still unraised raises it instead, and a reader answers.
            var answerer = Quietest(spoken);
            if (string.IsNullOrWhiteSpace(move.Says))
                spoken = spoken with { Turns = [.. spoken.Turns, new RoundTableTurn(HostId, $"Before we wrap up: {raised.Topic.ToLowerInvariant()}.") { Topic = raised.Topic }] };
            return spoken with { Next = new RoundTableCue(answerer, TopicAsk(raised)) };
        }

        var speaker = panel.Contains(move.Speaker?.Trim() ?? string.Empty) ? move.Speaker!.Trim() : Quietest(spoken);
        var answering = move.Answering?.Trim() ?? string.Empty;
        // A host turn always lands in the transcript, so silent host turns cannot loop short of the cap.
        if (string.IsNullOrWhiteSpace(move.Says))
            spoken = spoken with { Turns = [.. spoken.Turns, new RoundTableTurn(HostId, $"Over to you, {Name(speaker)}.") { Topic = topic }] };
        var forced = raised is not null && !string.Equals(raised.Topic, move.Topic?.Trim(), StringComparison.OrdinalIgnoreCase);
        return spoken with
        {
            Next = new RoundTableCue(speaker, forced ? TopicAsk(raised!) : move.Ask?.Trim() ?? string.Empty)
            {
                Answering = !forced && panel.Contains(answering) && answering != speaker ? answering : string.Empty,
            },
        };
    }

    private static string TopicAsk(AgendaItem item) =>
        $"What did you make of the book’s {item.Topic.ToLowerInvariant()}?"
        + (item.Why.Length > 0 ? $" The reads said: {item.Why}" : string.Empty);

    /// <summary>The table after the cued reader has spoken.</summary>
    public static RoundTable Spoken(RoundTable table, string words)
    {
        var cue = table.Next ?? throw new InvalidOperationException("No reader was cued to speak.");
        return table with
        {
            Turns = [.. table.Turns, new RoundTableTurn(cue.Speaker, words.Trim()) { Answering = cue.Answering }],
            Next = null,
        };
    }

    /// <summary>The table closed by the host's last words, with where the readers ended up.</summary>
    public static RoundTable Closed(RoundTable table, string says, RoundTableSummary summary)
    {
        var turns = string.IsNullOrWhiteSpace(says) ? table.Turns : [.. table.Turns, new RoundTableTurn(HostId, says.Trim())];
        return table with { Turns = turns, Next = null, CloseAsked = true, Summary = summary };
    }
}

/// <summary>How the round table is stored on the draft: JSON; anything unreadable is a table not yet begun.</summary>
public static class RoundTableCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Write(RoundTable table) => JsonSerializer.Serialize(table, Options);

    public static RoundTable Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return RoundTable.Empty;
        try
        {
            var table = JsonSerializer.Deserialize<RoundTable>(json, Options);
            return table?.Turns is null ? RoundTable.Empty : table;
        }
        catch (JsonException)
        {
            return RoundTable.Empty;
        }
    }
}
