using System.Globalization;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Body;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// What the pages have taught the plan, in the app's private storage: the skip ledger (the read-back's own file, passed in
/// so there is one copy), and beside it the exercise ladders, the open soreness, the assessment baselines and the weeks an
/// assessment was last planned for. It is the weekly run's <see cref="IWorkoutLearning"/> and the read-back's
/// <see cref="IReadBackSettled"/>: one reads what the other learned.
///
/// <para><b>A record that cannot be read is never read as empty.</b> An established person read as new goes back to the
/// assessment, and a soreness read as healed prints a plan that hurts. So <see cref="Load"/> says which record it could not
/// read (<see cref="WorkoutLearning.UnreadableBecause"/>, no values in it), and nothing here ever writes over such a file.
/// It is also not set aside (<see cref="UnreadableCopies"/> would make the next run see nothing and carry on): the file stays
/// where it is, in full, until it can be read or the owner deals with it. A ledger that is only partly readable counts the
/// same, since the file written back would drop the entries this version cannot read.</para>
///
/// <para>The files hold names, days, exercise names, rungs and the owner's counted answers; never a page, a mark or a word the
/// owner wrote.</para>
/// </summary>
public sealed class FileWorkoutLearning : IWorkoutLearning, IReadBackSettled
{
    public const string ProgressionFile = "progression.json";
    public const string SorenessFile = "soreness.json";
    public const string BaselinesFile = "baselines.json";
    public const string AssessmentsFile = "assessments.json";

    private const int Version = 1;
    private const string DayFormat = "yyyy-MM-dd";

    // One lock for every instance in the process: the worker and the screen share these files.
    private static readonly object FileLock = new();

    private readonly ILedgerStore skips;
    private readonly string directory;

    public FileWorkoutLearning(ILedgerStore skips, string directory)
    {
        ArgumentNullException.ThrowIfNull(skips);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.skips = skips;
        this.directory = directory;
    }

    /// <inheritdoc />
    public WorkoutLearning Load()
    {
        lock (FileLock)
        {
            var state = Read();
            return new WorkoutLearning(
                state.Skips, state.Progression, state.Soreness, state.Baselines, state.Weeks, state.Unreadable);
        }
    }

    /// <inheritdoc />
    public void RecordAssessmentWeek(string person, DateOnly weekStart)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(person);
        lock (FileLock)
        {
            var state = ReadWhole();
            var weeks = new Dictionary<string, DateOnly>(state.Weeks, StringComparer.OrdinalIgnoreCase) { [person.Trim()] = weekStart };
            Write(AssessmentsFile, EncodeWeeks(weeks));
        }
    }

    /// <inheritdoc />
    public void RecordDoctorNotePrinted(string person)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(person);
        lock (FileLock)
        {
            var state = ReadWhole();
            var next = state.Soreness.MarkDoctorNotePrinted(person);
            if (next.ToJson() != state.Soreness.ToJson())
            {
                Write(SorenessFile, next.ToJson());
            }
        }
    }

    /// <inheritdoc />
    public void Settled(ExpectedDocument document, DocumentResult result)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(result);
        if (document.Marks is null)
        {
            return;
        }

        lock (FileLock)
        {
            var state = ReadWhole();
            var person = document.Person;

            var baselines = state.Baselines.TryGetValue(person, out var known) ? known : null;
            if (MeasuresOf(document) is { } measures)
            {
                var merged = Merge(baselines, AssessmentBaselines.FromPages(measures, result.Pages));
                var all = new Dictionary<string, AssessmentBaselines>(state.Baselines, StringComparer.OrdinalIgnoreCase) { [person] = merged };
                Write(BaselinesFile, EncodeBaselines(all));
                baselines = merged;
            }

            if (SessionPagesOf(document) is { } session)
            {
                var next = state.Progression.Settle(session, result.Pages, baselines);
                if (!ReferenceEquals(next, state.Progression))
                {
                    Write(ProgressionFile, next.ToJson());
                }
            }

            var stillSore = document.Pages
                .SelectMany(page => page.Rules)
                .OfType<StillSoreRule>()
                .Select(rule => rule.BoxId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (stillSore.Count > 0)
            {
                var outcome = DayClose.Decide(document.Marks, result.All);
                var next = state.Soreness.Settle(person, document.Day, outcome, stillSore, result.All);
                if (!ReferenceEquals(next, state.Soreness))
                {
                    Write(SorenessFile, next.ToJson());
                }
            }
        }
    }

    // ---- what a document teaches ----

    // An assessment document's pages are read by a number or a circle rule named for a measure; page n + 2 is test n.
    private static IReadOnlyList<AssessmentMeasure>? MeasuresOf(ExpectedDocument document)
    {
        var known = Enum.GetValues<Constitution.Domain.Workouts.AssessmentTest>().SelectMany(AssessmentCatalogue.MeasuresOf).ToList();
        var measures = new List<AssessmentMeasure>();
        foreach (var page in document.Pages.OrderBy(item => item.Number))
        {
            var rule = page.Rules.FirstOrDefault(item => item is NumberRule or RatingRule);
            var measure = rule is null ? null : known.FirstOrDefault(item => item.Name == rule.Subject);
            if (measure is null || page.Number != AssessmentDocument.PageOfTest(measures.Count))
            {
                return null;
            }

            measures.Add(measure);
        }

        return measures.Count == 0 ? null : measures;
    }

    // A strength page's exercise is told by its rule; the pages must run from the first exercise page without a gap, since the
    // ledger finds each exercise's page by its position.
    private static SessionPages? SessionPagesOf(ExpectedDocument document)
    {
        if (document.Session is not { } kind)
        {
            return null;
        }

        var refs = new List<ExercisePageRef>();
        foreach (var page in document.Pages.OrderBy(item => item.Number))
        {
            if (page.Rules.OfType<ExercisePageRule>().FirstOrDefault() is not { } rule || page.Number != PageReading.PageOfExercise(refs.Count))
            {
                return null;
            }

            refs.Add(new ExercisePageRef(new Exercise(rule.Subject), rule.DoneBoxId, rule.RatingGroup));
        }

        return refs.Count == 0 ? null : new SessionPages(document.Person, document.Day, kind, refs);
    }

    // What is newly answered replaces what was; a measure answered since is no longer missing.
    private static AssessmentBaselines Merge(AssessmentBaselines? earlier, AssessmentBaselines later)
    {
        if (earlier is null)
        {
            return later;
        }

        var numbers = new Dictionary<string, decimal>(earlier.Numbers);
        foreach (var pair in later.Numbers)
        {
            numbers[pair.Key] = pair.Value;
        }

        var choices = new Dictionary<string, string>(earlier.Choices);
        foreach (var pair in later.Choices)
        {
            choices[pair.Key] = pair.Value;
        }

        var missing = earlier.Missing.Concat(later.Missing)
            .Where(id => !numbers.ContainsKey(id) && !choices.ContainsKey(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new AssessmentBaselines(numbers, choices, missing);
    }

    // ---- reading ----

    private sealed record State(
        SkipLedger Skips,
        ProgressionLedger Progression,
        SorenessLedger Soreness,
        Dictionary<string, AssessmentBaselines> Baselines,
        Dictionary<string, DateOnly> Weeks,
        string? Unreadable);

    private State ReadWhole() =>
        Read() is { Unreadable: { } because } ? throw new WorkoutLearningUnreadableException(because) : Read();

    private State Read()
    {
        var unreadable = new List<string>();

        var skipRead = skips.Load();
        // Learning only reads the skip ledger, and the ledger carries any entry it cannot interpret through its own saves,
        // so only a file that is not a ledger at all stops learning.
        if (skipRead.WholeFileUnreadable)
        {
            unreadable.Add("skip ledger");
        }

        var progression = ProgressionLedger.FromJson(Text(ProgressionFile));
        if (progression.WholeFileUnreadable || progression.UnreadableEntries > 0)
        {
            unreadable.Add("exercise ladders");
        }

        var soreness = SorenessLedger.FromJson(Text(SorenessFile));
        if (soreness.WholeFileUnreadable || soreness.UnreadableEntries > 0)
        {
            unreadable.Add("soreness record");
        }

        var baselines = new Dictionary<string, AssessmentBaselines>(StringComparer.OrdinalIgnoreCase);
        try
        {
            DecodeBaselines(Text(BaselinesFile), baselines);
        }
        catch (FormatException)
        {
            unreadable.Add("assessment baselines");
        }

        var weeks = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        try
        {
            DecodeWeeks(Text(AssessmentsFile), weeks);
        }
        catch (FormatException)
        {
            unreadable.Add("assessment weeks");
        }

        return new State(
            skipRead.Ledger, progression.Ledger, soreness.Ledger, baselines, weeks,
            unreadable.Count == 0 ? null : $"The {string.Join(" and the ", unreadable)} could not be read, so nothing was planned from them.");
    }

    private string? Text(string file)
    {
        var path = Path.Combine(directory, file);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private void Write(string file, string text)
    {
        Directory.CreateDirectory(directory);
        AtomicFile.WriteAllText(Path.Combine(directory, file), text);
    }

    // ---- the two records this class owns the shape of ----

    private static string EncodeWeeks(Dictionary<string, DateOnly> weeks) =>
        Encode(writer =>
        {
            writer.WriteStartObject("weeks");
            foreach (var pair in weeks.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                writer.WriteString(pair.Key, pair.Value.ToString(DayFormat, CultureInfo.InvariantCulture));
            }

            writer.WriteEndObject();
        });

    private static void DecodeWeeks(string? json, Dictionary<string, DateOnly> into)
    {
        if (json is null)
        {
            return;
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);
            CheckVersion(parsed.RootElement);
            foreach (var item in parsed.RootElement.GetProperty("weeks").EnumerateObject())
            {
                into[item.Name] = DateOnly.ParseExact(item.Value.GetString() ?? string.Empty, DayFormat, CultureInfo.InvariantCulture);
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new FormatException("The record could not be read as the weeks an assessment was planned.", e);
        }
    }

    private static string EncodeBaselines(Dictionary<string, AssessmentBaselines> all) =>
        Encode(writer =>
        {
            writer.WriteStartObject("people");
            foreach (var pair in all.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(pair.Key);
                writer.WriteStartObject("numbers");
                foreach (var number in pair.Value.Numbers.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(number.Key, number.Value.ToString(CultureInfo.InvariantCulture));
                }

                writer.WriteEndObject();
                writer.WriteStartObject("choices");
                foreach (var choice in pair.Value.Choices.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(choice.Key, choice.Value);
                }

                writer.WriteEndObject();
                writer.WriteStartArray("missing");
                foreach (var id in pair.Value.Missing)
                {
                    writer.WriteStringValue(id);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        });

    private static void DecodeBaselines(string? json, Dictionary<string, AssessmentBaselines> into)
    {
        if (json is null)
        {
            return;
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);
            CheckVersion(parsed.RootElement);
            foreach (var person in parsed.RootElement.GetProperty("people").EnumerateObject())
            {
                var numbers = person.Value.GetProperty("numbers").EnumerateObject()
                    .ToDictionary(item => item.Name, item => decimal.Parse(item.Value.GetString() ?? string.Empty, NumberStyles.Number, CultureInfo.InvariantCulture));
                var choices = person.Value.GetProperty("choices").EnumerateObject()
                    .ToDictionary(item => item.Name, item => item.Value.GetString() ?? throw new FormatException("A choice has no word."));
                var missing = person.Value.GetProperty("missing").EnumerateArray()
                    .Select(item => item.GetString() ?? throw new FormatException("A missing measure has no id."))
                    .ToList();
                into[person.Name] = new AssessmentBaselines(numbers, choices, missing);
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new FormatException("The record could not be read as the assessment baselines.", e);
        }
    }

    private static void CheckVersion(JsonElement root)
    {
        if (root.GetProperty("version").GetInt32() != Version)
        {
            throw new FormatException("This record is of a version this app does not read.");
        }
    }

    private static string Encode(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            body(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
