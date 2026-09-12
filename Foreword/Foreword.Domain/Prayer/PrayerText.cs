namespace Foreword.Domain.Prayer;

/// <summary>
/// What the owner wrote on the prayer page — in a type that cannot be handed to the rest of the
/// morning by accident.
///
/// <para><b>Why a type and not a rule.</b> Four separate Claude calls compose the morning: the
/// brief's opening, the calendar's narrator, the checklist's coach, and this document's own
/// pastoral line. The first three take a <c>string</c>. A rule saying "do not pass the journal to
/// those three" is a rule somebody keeps, until the morning they do not — and the failure is
/// silent, because a prompt with one extra paragraph in it returns a perfectly good sentence. So
/// the journal is not a string. It is this, and the only way to read it is to declare yourself an
/// <see cref="IPrayerReader"/>, which a guard enumerates.</para>
///
/// <para><b>And it redacts itself.</b> <see cref="ToString"/> is the one hole a type cannot close:
/// interpolate anything into a prompt and C# will happily call it. So this one returns
/// <see cref="Redacted"/>, which means the worst a leak can put in front of a public model is the
/// fact that something was withheld.</para>
///
/// <para>What "private" does <b>not</b> mean, stated plainly because it was accepted at gate 1: the
/// page is transcribed, so a vision model does read it. What it means is that nothing from it
/// reaches Google Tasks, the calendar, the voice profile, or any shared file — the entries go to a
/// private Drive document and stop there.</para>
/// </summary>
public sealed class PrayerText
{
    /// <summary>What stands in for the text anywhere the text itself must not go.</summary>
    public const string Redacted = "[prayer — withheld]";

    private readonly string text;

    private PrayerText(string text) => this.text = text;

    public static PrayerText Of(string? text) => new(text ?? string.Empty);

    public static PrayerText Empty { get; } = new(string.Empty);

    public bool IsEmpty => string.IsNullOrWhiteSpace(text);

    /// <summary>
    /// The text itself. Handed only to a reader that has declared itself one of the few things
    /// allowed to see it; <c>ReadBy(null)</c> is a leak, not a convenience, so it throws.
    /// </summary>
    public string ReadBy(IPrayerReader reader) =>
        reader is null
            ? throw new ArgumentNullException(nameof(reader),
                "Prayer text is read only by a declared IPrayerReader.")
            : text;

    public override string ToString() => Redacted;
}

/// <summary>
/// The complete list of things allowed to read prayer content, declared rather than remembered.
///
/// <para>There are four, and a guard names them: the vision call that transcribes the page, the
/// pastoral composer that writes this document's own line, the private archive that keeps it, and
/// the pages that print it back. Nothing else — and surfacing past entries needs no reading at all,
/// because it selects by date.</para>
/// </summary>
public interface IPrayerReader
{
}
