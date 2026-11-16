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
public sealed record HostMove(string Says, string Speaker, string Ask, string Answering, bool Close);

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

    [JsonIgnore]
    public bool Closed => Summary is not null;
}

/// <summary>
/// The rules of the round table (release H, R28–R33, R40). The model decides what is said; these decide
/// who may say it, what is owed, and when it ends:
/// <list type="bullet">
/// <item>The host opens, and speaks between readers, handing to whom it names.</item>
/// <item>Every split owes two rebuttals, one each way (R30); the host may not close while one is owed.</item>
/// <item>The show is capped by the manuscript's length (R33, R40) — a cap, not a quota. Past it, owed
/// rebuttals are cued straight, with no host turn between, and then the show closes.</item>
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
        if (table.Turns.Count >= TurnCap(length))
        {
            return Owed(table, reviews) is { Count: > 0 } owed
                ? new RoundTableStep(RoundTableStepKind.ReaderTurn, owed[0])
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
        var spoken = string.IsNullOrWhiteSpace(move.Says)
            ? table
            : table with { Turns = [.. table.Turns, new RoundTableTurn(HostId, move.Says.Trim())] };

        if (move.Close)
        {
            var owed = Owed(spoken, reviews);
            return owed.Count == 0
                ? spoken with { CloseAsked = true, Next = null }
                : spoken with { Next = owed[0] };
        }

        var speaker = panel.Contains(move.Speaker?.Trim() ?? string.Empty)
            ? move.Speaker!.Trim()
            : panel.OrderBy(id => spoken.Turns.Count(turn => turn.Speaker == id)).ThenBy(panel.IndexOf).First();
        var answering = move.Answering?.Trim() ?? string.Empty;
        // A host turn always lands in the transcript, so silent host turns cannot loop short of the cap.
        if (string.IsNullOrWhiteSpace(move.Says))
            spoken = spoken with { Turns = [.. spoken.Turns, new RoundTableTurn(HostId, $"Over to you, {Name(speaker)}.")] };
        return spoken with
        {
            Next = new RoundTableCue(speaker, move.Ask?.Trim() ?? string.Empty)
            {
                Answering = panel.Contains(answering) && answering != speaker ? answering : string.Empty,
            },
        };
    }

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
