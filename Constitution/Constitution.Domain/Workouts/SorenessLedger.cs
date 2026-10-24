using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.Voice;

namespace Constitution.Domain.Workouts;

/// <summary>One open soreness: whose, where in the owner's words, the area it was placed in, when it was noted, and whether the doctor suggestion has printed.</summary>
public sealed record SorenessNote(string Person, string Place, SoreArea Area, DateOnly Noted, bool DoctorNotePrinted);

/// <summary>What <see cref="SorenessLedger.FromJson"/> found.</summary>
public sealed record SorenessLedgerRead(SorenessLedger Ledger, int UnreadableEntries, bool WholeFileUnreadable);

/// <summary>
/// The open sorenesses (C-5.2.3). A margin note read as soreness opens one with <see cref="Note"/>; every page of the
/// workout documents after that asks "Still sore?" until the question is answered by a blank (D28); the doctor suggestion
/// prints once (D29). Immutable: every method returns the next ledger.
///
/// <para><b>What counts as a blank.</b> A blank on every "Still sore?" of a session the owner <i>did</i> (some page marked)
/// and on a page printed after the note was made (a blank on the day it was noted, or before, could not have been an
/// answer to a question that was not yet on the page). A mark on any page keeps it open. A page not read, a question
/// waiting to be asked, or a box the read left out is no answer. <b>A session not done is no answer either</b>: D28 says
/// silence means better, but a page the pen never touched is not the owner saying so, and wrongly clearing a soreness
/// has a worse cost than asking once more. One answer applies to all of that person's open notes.</para>
///
/// <para><b>The doctor suggestion</b> is due when the soreness has lasted <see cref="PersistsAfterDays"/> days, or the
/// morning's signals read as <see cref="RecoveryState.Alarming"/>, and it has not yet printed for this person's
/// soreness. <see cref="MarkDoctorNotePrinted"/> is the delivery's to call once the document is sent.</para>
/// </summary>
public sealed class SorenessLedger
{
    private const int Version = 1;
    private const int LongestPlace = 60;
    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>Days (counting the day it was noted) after which a soreness is lasting.</summary>
    public const int PersistsAfterDays = 7;

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly IReadOnlyList<SorenessNote> notes;

    private SorenessLedger(IReadOnlyList<SorenessNote> notes)
    {
        this.notes = notes;
    }

    public static SorenessLedger Empty { get; } = new([]);

    /// <summary>Every open note, in the order they were made.</summary>
    public IReadOnlyList<SorenessNote> Notes => notes;

    /// <summary>
    /// Opens a soreness from a margin note. An area already open for the person is not noted twice (the first date stands),
    /// and a place that names nothing the plan can place is kept as an unknown area, which the plan answers by resting.
    /// </summary>
    public SorenessLedger Note(string person, string place, DateOnly noted)
    {
        if (string.IsNullOrWhiteSpace(person))
        {
            throw new ArgumentException("A soreness belongs to someone.", nameof(person));
        }

        var words = Tidy(place);
        if (words.Length == 0)
        {
            throw new ArgumentException("A soreness needs a few words for where it is.", nameof(place));
        }

        var area = SoreAreas.Read(words);
        var name = person.Trim();
        var already = notes.Any(item => Same(item.Person, name)
            && (area == SoreArea.Unknown
                ? item.Area == SoreArea.Unknown && string.Equals(item.Place, words, StringComparison.OrdinalIgnoreCase)
                : item.Area == area));
        return already ? this : new SorenessLedger([.. notes, new SorenessNote(name, words, area, noted, false)]);
    }

    public IReadOnlyList<SorenessNote> OpenFor(string person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return notes.Where(item => Same(item.Person, person)).ToList();
    }

    /// <summary>
    /// Reads back a finished day. <paramref name="stillSoreBoxIds"/> are the "Still sore?" boxes the person's pages carried;
    /// <paramref name="outcome"/> is how <see cref="DayClose"/> judged the session. See the class for what clears a note.
    /// </summary>
    public SorenessLedger Settle(string person, DateOnly day, SessionOutcome outcome, IReadOnlyCollection<string> stillSoreBoxIds, IEnumerable<InkResolution> resolutions)
    {
        if (string.IsNullOrWhiteSpace(person))
        {
            throw new ArgumentException("A session belongs to someone.", nameof(person));
        }

        ArgumentNullException.ThrowIfNull(stillSoreBoxIds);
        ArgumentNullException.ThrowIfNull(resolutions);
        if (stillSoreBoxIds.Count == 0)
        {
            throw new ArgumentException("A page that asked nothing cannot have been answered.", nameof(stillSoreBoxIds));
        }

        if (outcome != SessionOutcome.Done || !IsBlank(stillSoreBoxIds, resolutions))
        {
            return this;
        }

        var kept = notes.Where(item => !(Same(item.Person, person) && day > item.Noted)).ToList();
        return kept.Count == notes.Count ? this : new SorenessLedger(kept);
    }

    /// <summary>Records that the doctor suggestion has been delivered, for every open note of the person.</summary>
    public SorenessLedger MarkDoctorNotePrinted(string person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return new SorenessLedger(notes.Select(item => Same(item.Person, person) ? item with { DoctorNotePrinted = true } : item).ToList());
    }

    /// <summary>
    /// What the injury-day page prints for the person today (the oldest open note), or null when nothing is sore.
    /// <paramref name="signals"/> may be the morning's recovery read; only <see cref="RecoveryState.Alarming"/> brings the
    /// doctor suggestion early.
    /// </summary>
    public Soreness? PageModel(string person, DateOnly today, RecoveryRead? signals = null)
    {
        var open = OpenFor(person).Where(item => item.Noted <= today).ToList();
        if (open.Count == 0)
        {
            return null;
        }

        var note = open.OrderBy(item => item.Noted).First();
        var days = today.DayNumber - note.Noted.DayNumber + 1;
        var due = !open.Any(item => item.DoctorNotePrinted)
            && (days >= PersistsAfterDays || signals?.State == RecoveryState.Alarming);
        return new Soreness(note.Place, SeatWords.ShortDate(note.Noted), days, due);
    }

    // ---- the answer ----

    private static bool IsBlank(IReadOnlyCollection<string> boxIds, IEnumerable<InkResolution> resolutions)
    {
        var relevant = resolutions.Where(item => boxIds.Contains(item.SubjectId, StringComparer.Ordinal)).ToList();
        if (relevant.Any(item => item is InkResolution.StillSore))
        {
            return false;
        }

        // Every box has to have said "better": one left out, unread or waiting on a question is not the owner's answer. The tick is
        // printed under one id on every page, so a page that said better does not speak for a page, under the same id, that has not.
        return relevant.All(item => item is InkResolution.Better)
            && boxIds.All(id => relevant.Any(item => item.SubjectId == id));
    }

    private static bool Same(string left, string right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Tidy(string? place)
    {
        var words = Spaces.Replace(place ?? string.Empty, " ").Trim();
        if (words.StartsWith("my ", StringComparison.OrdinalIgnoreCase))
        {
            words = words[3..].TrimStart();
        }

        return words.Length > LongestPlace ? words[..LongestPlace].TrimEnd() : words;
    }

    // ---- the stored file ----

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("notes");
            foreach (var item in notes)
            {
                writer.WriteStartObject();
                writer.WriteString("person", item.Person);
                writer.WriteString("place", item.Place);
                writer.WriteString("area", SoreAreas.StoredName(item.Area));
                writer.WriteString("noted", item.Noted.ToString(DayFormat, CultureInfo.InvariantCulture));
                writer.WriteBoolean("doctorNotePrinted", item.DoctorNotePrinted);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads the stored file. Nothing stored is an empty ledger; a file that is not a ledger comes back empty and flagged; a bad entry is counted and skipped.</summary>
    public static SorenessLedgerRead FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SorenessLedgerRead(Empty, 0, false);
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
                || !root.TryGetProperty("notes", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return new SorenessLedgerRead(Empty, 0, true);
            }

            var read = new List<SorenessNote>();
            var unreadable = 0;
            foreach (var entry in array.EnumerateArray())
            {
                if (TryNote(entry) is { } note)
                {
                    read.Add(note);
                }
                else
                {
                    unreadable++;
                }
            }

            return new SorenessLedgerRead(new SorenessLedger(read), unreadable, false);
        }
        catch (JsonException)
        {
            // Not JSON at all: flagged, so the caller keeps the file instead of reading it as "nothing is sore".
            return new SorenessLedgerRead(Empty, 0, true);
        }
    }

    private static SorenessNote? TryNote(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || Text(entry, "person") is not { } person || string.IsNullOrWhiteSpace(person)
            || Text(entry, "place") is not { } place || string.IsNullOrWhiteSpace(place)
            || SoreAreas.FromStoredName(Text(entry, "area")) is not { } area
            || Text(entry, "noted") is not { } dayText
            || !DateOnly.TryParseExact(dayText, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var noted)
            || !entry.TryGetProperty("doctorNotePrinted", out var printed)
            || printed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        return new SorenessNote(person.Trim(), place.Trim(), area, noted, printed.GetBoolean());
    }

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
