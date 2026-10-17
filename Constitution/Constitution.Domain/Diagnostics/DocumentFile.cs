using System.Globalization;
using System.Text.RegularExpressions;

namespace Constitution.Domain.Diagnostics;

/// <summary>
/// The file name of a delivered document, as the export prints it ("workout-2026-11-17.pdf"). Built
/// from a <see cref="DocumentKind"/> and a date, or parsed from a string that must already be exactly
/// that shape; anything else is refused. There is no constructor that takes text, so a recipe's title
/// or a person's name cannot become a document name by being passed in.
///
/// <para>The date is the day the document is <i>for</i>, which is the only date the export carries; a
/// birthdate cannot arrive as one because the shape also needs a kind prefix and a ".pdf" suffix, and
/// nothing in the app names a document for a birthday.</para>
/// </summary>
public sealed record DocumentFile
{
    private static readonly Regex Shape = new(
        "^(workout|meals|digest)-([0-9]{4}-[0-9]{2}-[0-9]{2})\\.pdf\\z", RegexOptions.CultureInvariant);

    private DocumentFile(string value)
    {
        Value = value;
    }

    /// <summary>The file name.</summary>
    public string Value { get; }

    /// <summary>The name for a document of this kind for this day.</summary>
    public static DocumentFile For(DocumentKind kind, DateOnly date) =>
        new($"{Prefix(kind)}-{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf");

    /// <summary>
    /// A name that arrived as text — from a log, say. It must be exactly a kind, a real date and ".pdf".
    /// The refusal does not repeat the text: this is the moment a private string would otherwise be logged.
    /// </summary>
    public static DocumentFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var match = Shape.Match(text);
        if (!match.Success
            || !DateOnly.TryParseExact(match.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException("That is not a document file name Diagnostics can carry.", nameof(text));
        }

        return new DocumentFile(text);
    }

    private static string Prefix(DocumentKind kind) => kind switch
    {
        DocumentKind.Workout => "workout",
        DocumentKind.Meals => "meals",
        DocumentKind.Digest => "digest",
        _ => throw DiagnosticsPhrases.Undefined(nameof(kind)),
    };
}
