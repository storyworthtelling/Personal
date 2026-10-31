using System.Globalization;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;

namespace Constitution.Domain.Workouts;

/// <summary>What <see cref="ProgressionLedger.FromJson"/> found: the ledger, entries that could not be read, and whether the whole file could not.</summary>
public sealed record ProgressionLedgerRead(ProgressionLedger Ledger, int UnreadableEntries, bool WholeFileUnreadable);

/// <summary>Where one exercise stands for one person.</summary>
/// <param name="Rung">Place on the bodyweight ladder (ignored for a weighted exercise).</param>
/// <param name="Pounds">The weight, or zero for an exercise without one.</param>
/// <param name="EasyRun">Consecutive easy ratings so far.</param>
/// <param name="UpFrom">When the last settled session moved the exercise up, the total it moved up from (pounds, or all the reps in the session); otherwise null.</param>
public sealed record ExerciseState(int Rung, int Pounds, int EasyRun, int? UpFrom);

/// <summary>
/// What the exercise pages have taught the plan (C-5.1.3): for each person and exercise, the rung it is on and the run of
/// easy ratings behind it; for each person, how many exercises a strength session may hold after one ran long.
/// Immutable: <see cref="Settle"/> returns the next ledger. Pure: no clock, and nothing leaves it but a string the caller stores.
///
/// <para><b>What a page says.</b> A ticked page was done as written (D18: any mark counts) and moves nothing by itself;
/// a circled word with no tick is the same page, attempted. <i>Hard</i> eases that exercise one step. <i>Easy</i> twice
/// running (<see cref="DoseLadder.EasyRunToProgress"/>) moves it up one. <i>Right</i> holds it and breaks a run of easies;
/// a tick with no circle says nothing either way. A page that was not read, or is waiting on a question, changes nothing
/// at all, and neither does a page left empty (the skip ledger already counts that).</para>
///
/// <para><b>Stopped early.</b> The pages up to some page were done and every page after it was read and left empty:
/// the last ticked exercise was too much (eased as if it had been rated hard, once) and the session ran long, so the next
/// strength session holds only as many exercises as were done (never fewer than <see cref="MinimumExercises"/>). A
/// session finished at the cap lifts it one step, until it reaches <see cref="MaximumExercises"/> and goes.
/// A skipped page in the middle is not stopping early, and nor is anything unread: with a page unknown there is no
/// "everything after it".</para>
///
/// <para><b>A day is learned from once.</b> A session already settled for the same person and day is left as it is, so reading
/// the same page again cannot count an easy run twice. A read that reached none of the pages does not use the day up.</para>
///
/// <para><b>Only strength sessions teach it.</b> Mobility and the walks have no dose to climb; their pages are ignored here.</para>
/// </summary>
public sealed class ProgressionLedger
{
    private const int Version = 1;

    /// <summary>The fewest exercises a capped strength session is left with.</summary>
    public const int MinimumExercises = 2;

    /// <summary>The most a strength session holds; a cap that reaches this is gone.</summary>
    public const int MaximumExercises = 5;

    /// <summary>How long a settled day is remembered. A page is given up on after a week, so no retry comes back to a day older than this.</summary>
    public const int RetainDays = 28;

    private readonly IReadOnlyDictionary<(string Person, string Exercise), Entry> states;
    private readonly IReadOnlyDictionary<string, CapEntry> caps;
    private readonly IReadOnlySet<SettledDay> settled;

    private ProgressionLedger(
        IReadOnlyDictionary<(string Person, string Exercise), Entry> states,
        IReadOnlyDictionary<string, CapEntry> caps,
        IReadOnlySet<SettledDay> settled)
    {
        this.states = states;
        this.caps = caps;
        this.settled = settled;
    }

    public static ProgressionLedger Empty { get; } = new(
        new Dictionary<(string Person, string Exercise), Entry>(),
        new Dictionary<string, CapEntry>(),
        new HashSet<SettledDay>());

    /// <summary>
    /// Settles a session from the pages of its read document. Exercise <c>n</c> of the session is page <c>n + 2</c> of the
    /// document (<see cref="PageReading"/>), so each page answers for its own exercise whatever ids the pages share.
    /// </summary>
    public ProgressionLedger Settle(SessionPages session, IEnumerable<PageResolutions> pages, AssessmentBaselines? baselines = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(pages);
        if (string.IsNullOrWhiteSpace(session.Person))
        {
            throw new ArgumentException("A session belongs to someone.", nameof(session));
        }

        if (session.Pages is null || session.Pages.Count == 0)
        {
            throw new ArgumentException("A session with no exercise pages teaches nothing.", nameof(session));
        }

        if (session.Kind != SessionKind.Strength)
        {
            return this;
        }

        var person = session.Person.Trim();

        // A day is learned from once. The same page read again (a retry after a step that failed, a second morning's run) must
        // not count the same easy run twice.
        var day = new SettledDay(NormalPerson(person), session.Day);
        if (settled.Contains(day))
        {
            return this;
        }

        var reads = PageReading.Read(session, pages);
        if (reads.All(read => read.Verdict == PageVerdict.Unknown))
        {
            // Nothing was read, so nothing was learned and the day is still to be learned from.
            return this;
        }

        var lastDone = StoppedEarlyAt(reads);

        var nextStates = states.ToDictionary(pair => pair.Key, pair => pair.Value);
        for (var index = 0; index < reads.Count; index++)
        {
            var read = reads[index];
            if (read.Verdict != PageVerdict.Done)
            {
                continue;
            }

            var exercise = read.Page.Exercise;
            var key = KeyOf(person, exercise);
            var state = states.TryGetValue(key, out var existing) ? existing.State : Start(exercise, baselines);
            var weighted = DoseLadder.IsWeighted(exercise);

            ExerciseState next;
            if (read.Rating == ExerciseRating.Hard || index == lastDone)
            {
                next = Ease(state, weighted);
            }
            else
            {
                next = read.Rating switch
                {
                    ExerciseRating.Easy => state.EasyRun + 1 >= DoseLadder.EasyRunToProgress ? Progress(state, weighted) : state with { EasyRun = state.EasyRun + 1, UpFrom = null },
                    ExerciseRating.Right => state with { EasyRun = 0, UpFrom = null },
                    _ => state with { UpFrom = null },
                };
            }

            nextStates[key] = new Entry(person, exercise.Name, next);
        }

        return new ProgressionLedger(nextStates, NextCaps(person, reads, lastDone), NextSettled(day));
    }

    /// <summary>
    /// The same, from answers that came without their page. Only safe when no answer can belong to two pages: one whose id
    /// is shared by several pages is refused (<see cref="PageReading.Read(SessionPages, IEnumerable{InkResolution})"/>).
    /// Prefer the overload that takes the pages.
    /// </summary>
    public ProgressionLedger Settle(SessionPages session, IEnumerable<InkResolution> resolutions, AssessmentBaselines? baselines = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolutions);
        if (session.Pages is null || session.Pages.Count == 0)
        {
            throw new ArgumentException("A session with no exercise pages teaches nothing.", nameof(session));
        }

        return Settle(session, PageReading.ByPage(session, resolutions), baselines);
    }

    // The days already learned from, forgetting those too old for a retry to come back to.
    private IReadOnlySet<SettledDay> NextSettled(SettledDay day)
    {
        var next = settled.Where(item => item.Day >= day.Day.AddDays(-RetainDays)).ToHashSet();
        next.Add(day);
        return next;
    }

    /// <summary>What the exercise asks of the person next time: where the pages have left it, or its starting dose.</summary>
    public ExerciseDose DoseFor(string person, Exercise exercise, AssessmentBaselines? baselines = null)
    {
        var state = StateOf(person, exercise, baselines);
        return DoseLadder.DoseOf(DoseLadder.IsWeighted(exercise), state.Rung, state.Pounds);
    }

    /// <summary>The most exercises the person's next strength session may hold, or null when it is not capped.</summary>
    public int? ExerciseCap(string person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return caps.TryGetValue(NormalPerson(person), out var cap) ? cap.Cap : null;
    }

    internal ExerciseState StateOf(string person, Exercise exercise, AssessmentBaselines? baselines)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(exercise);
        return states.TryGetValue(KeyOf(person, exercise), out var entry) ? entry.State : Start(exercise, baselines);
    }

    // ---- the rules ----

    private static ExerciseState Start(Exercise exercise, AssessmentBaselines? baselines)
    {
        if (DoseLadder.IsWeighted(exercise))
        {
            return new ExerciseState(0, DoseLadder.StartingPounds, 0, null);
        }

        return new ExerciseState(RungFromBaseline(exercise, baselines), 0, 0, null);
    }

    // Half the count done in thirty seconds is a first set the person can finish: 6 or fewer starts at six, 7 or 8 at eight, more at ten.
    private static int RungFromBaseline(Exercise exercise, AssessmentBaselines? baselines)
    {
        var measure = exercise.Name.StartsWith("Sit to stand", StringComparison.Ordinal)
            ? AssessmentCatalogue.SitToStandId
            : exercise.Name.Contains("Push-ups", StringComparison.Ordinal) ? AssessmentCatalogue.WallPushUpsId : null;
        if (measure is null || baselines is null || !baselines.Numbers.TryGetValue(measure, out var count))
        {
            return 0;
        }

        var firstSet = (int)Math.Floor(count / 2m);
        return firstSet <= 6 ? 0 : firstSet <= 8 ? 1 : 2;
    }

    private static ExerciseState Ease(ExerciseState state, bool weighted) =>
        weighted
            ? state with { Pounds = Math.Max(DoseLadder.MinimumPounds, state.Pounds - DoseLadder.PoundStep), EasyRun = 0, UpFrom = null }
            : state with { Rung = Math.Max(0, state.Rung - 1), EasyRun = 0, UpFrom = null };

    private static ExerciseState Progress(ExerciseState state, bool weighted)
    {
        if (weighted)
        {
            return state with { Pounds = state.Pounds + DoseLadder.PoundStep, EasyRun = 0, UpFrom = state.Pounds };
        }

        if (state.Rung >= DoseLadder.TopRung)
        {
            return state with { EasyRun = 0, UpFrom = null };
        }

        var before = DoseLadder.Rungs[state.Rung];
        return state with { Rung = state.Rung + 1, EasyRun = 0, UpFrom = before.Sets * before.Reps };
    }

    // The index of the last done page when the session was stopped early, else -1.
    private static int StoppedEarlyAt(IReadOnlyList<PageRead> reads)
    {
        var lastDone = -1;
        for (var index = 0; index < reads.Count; index++)
        {
            if (reads[index].Verdict == PageVerdict.Done)
            {
                lastDone = index;
            }
        }

        if (lastDone < 0 || lastDone == reads.Count - 1)
        {
            return -1;
        }

        return reads.Skip(lastDone + 1).All(read => read.Verdict == PageVerdict.NotDone) ? lastDone : -1;
    }

    private IReadOnlyDictionary<string, CapEntry> NextCaps(string person, IReadOnlyList<PageRead> reads, int stoppedAt)
    {
        var next = caps.ToDictionary(pair => pair.Key, pair => pair.Value);
        var key = NormalPerson(person);
        if (stoppedAt >= 0)
        {
            next[key] = new CapEntry(person, Math.Max(MinimumExercises, stoppedAt + 1));
        }
        else if (caps.TryGetValue(key, out var existing) && reads.All(read => read.Verdict == PageVerdict.Done) && reads.Count >= existing.Cap)
        {
            if (existing.Cap + 1 >= MaximumExercises)
            {
                next.Remove(key);
            }
            else
            {
                next[key] = existing with { Cap = existing.Cap + 1 };
            }
        }

        return next;
    }

    private static (string Person, string Exercise) KeyOf(string person, Exercise exercise) => (NormalPerson(person), exercise.Name);

    private static string NormalPerson(string person) => person.Trim().ToLowerInvariant();

    // ---- the stored file ----

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("exercises");
            foreach (var entry in states.Values.OrderBy(item => item.Person, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Exercise, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("person", entry.Person);
                writer.WriteString("exercise", entry.Exercise);
                writer.WriteNumber("rung", entry.State.Rung);
                writer.WriteNumber("pounds", entry.State.Pounds);
                writer.WriteNumber("easyRun", entry.State.EasyRun);
                if (entry.State.UpFrom is { } up)
                {
                    writer.WriteNumber("upFrom", up);
                }
                else
                {
                    writer.WriteNull("upFrom");
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("caps");
            foreach (var cap in caps.Values.OrderBy(item => item.Person, StringComparer.OrdinalIgnoreCase))
            {
                writer.WriteStartObject();
                writer.WriteString("person", cap.Person);
                writer.WriteNumber("cap", cap.Cap);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("settled");
            foreach (var day in settled.OrderBy(item => item.Person, StringComparer.Ordinal).ThenBy(item => item.Day))
            {
                writer.WriteStartObject();
                writer.WriteString("person", day.Person);
                writer.WriteString("day", day.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Reads the stored file. Nothing stored is an empty ledger with no problem (a fresh install); a file that cannot be
    /// read as a ledger at all comes back empty with <see cref="ProgressionLedgerRead.WholeFileUnreadable"/> set, so the
    /// caller can leave it alone and say so; an entry that cannot be read is skipped and counted, and the rest are kept.
    /// </summary>
    public static ProgressionLedgerRead FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ProgressionLedgerRead(Empty, 0, false);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number)
                || number != Version
                || !root.TryGetProperty("exercises", out var exercises)
                || exercises.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("caps", out var capList)
                || capList.ValueKind != JsonValueKind.Array)
            {
                return new ProgressionLedgerRead(Empty, 0, true);
            }

            var readStates = new Dictionary<(string Person, string Exercise), Entry>();
            var readCaps = new Dictionary<string, CapEntry>();
            var unreadable = 0;
            foreach (var item in exercises.EnumerateArray())
            {
                if (TryEntry(item) is { } entry)
                {
                    readStates[(NormalPerson(entry.Person), entry.Exercise)] = entry;
                }
                else
                {
                    unreadable++;
                }
            }

            foreach (var item in capList.EnumerateArray())
            {
                if (TryCap(item) is { } cap)
                {
                    readCaps[NormalPerson(cap.Person)] = cap;
                }
                else
                {
                    unreadable++;
                }
            }

            var readSettled = new HashSet<SettledDay>();
            if (root.TryGetProperty("settled", out var settledList))
            {
                // A file written before settled days were kept has no list, and that is not a fault.
                if (settledList.ValueKind != JsonValueKind.Array)
                {
                    return new ProgressionLedgerRead(Empty, 0, true);
                }

                foreach (var item in settledList.EnumerateArray())
                {
                    if (TrySettled(item) is { } day)
                    {
                        readSettled.Add(day);
                    }
                    else
                    {
                        unreadable++;
                    }
                }
            }

            return new ProgressionLedgerRead(new ProgressionLedger(readStates, readCaps, readSettled), unreadable, false);
        }
        catch (JsonException)
        {
            // Not JSON at all. The caller is told so and the file is left for them to keep or repair; it is not read as "nothing taught".
            return new ProgressionLedgerRead(Empty, 0, true);
        }
    }

    private static Entry? TryEntry(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || Text(item, "person") is not { } person || string.IsNullOrWhiteSpace(person)
            || Text(item, "exercise") is not { } exercise || string.IsNullOrWhiteSpace(exercise)
            || Whole(item, "rung") is not { } rung || rung < 0 || rung > DoseLadder.TopRung
            || Whole(item, "pounds") is not { } pounds || pounds < 0
            || Whole(item, "easyRun") is not { } run || run < 0
            || !item.TryGetProperty("upFrom", out var up))
        {
            return null;
        }

        int? upFrom = null;
        if (up.ValueKind == JsonValueKind.Number)
        {
            if (!up.TryGetInt32(out var value) || value < 0)
            {
                return null;
            }

            upFrom = value;
        }
        else if (up.ValueKind != JsonValueKind.Null)
        {
            return null;
        }

        return new Entry(person.Trim(), exercise, new ExerciseState(rung, pounds, run, upFrom));
    }

    private static CapEntry? TryCap(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || Text(item, "person") is not { } person || string.IsNullOrWhiteSpace(person)
            || Whole(item, "cap") is not { } cap || cap < MinimumExercises || cap >= MaximumExercises)
        {
            return null;
        }

        return new CapEntry(person.Trim(), cap);
    }

    private static SettledDay? TrySettled(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || Text(item, "person") is not { } person || string.IsNullOrWhiteSpace(person)
            || Text(item, "day") is not { } day
            || !DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return null;
        }

        return new SettledDay(NormalPerson(person), parsed);
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Whole(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private sealed record Entry(string Person, string Exercise, ExerciseState State);

    private sealed record CapEntry(string Person, int Cap);

    private sealed record SettledDay(string Person, DateOnly Day);
}
