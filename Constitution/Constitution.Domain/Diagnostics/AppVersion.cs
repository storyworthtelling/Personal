using System.Globalization;
using System.Text.RegularExpressions;

namespace Constitution.Domain.Diagnostics;

/// <summary>
/// The app's display version ("1.0.57": major, minor, then the CI run number). Three dotted numbers
/// and nothing else.
///
/// <para>CI stamps <c>1.0.&lt;run number&gt;</c>, and a run number grows without limit, so that shape
/// is carried whatever the number: a date of birth cannot take it (there is no month 0), so it is the
/// one shape that cannot be a person's data however large the build gets. Any other shape is held to
/// the stricter rule that kept this type honest before: a build number of up to four digits that
/// looks like a year (1900 to 2100) is refused, because "9.3.1987" is also how a date of birth is
/// written.</para>
///
/// <para><b>What this cannot tell apart:</b> a date written with a two-digit year and no zero
/// padding, "9.3.87". The version comes from the build, not from data, so the caller is the build.</para>
/// </summary>
public sealed record AppVersion
{
    private static readonly Regex CiShape = new(
        "^1\\.0\\.[0-9]{1,9}\\z", RegexOptions.CultureInvariant);

    private static readonly Regex Shape = new(
        "^[1-9]\\.[0-9]{1,2}\\.([0-9]{1,4})\\z", RegexOptions.CultureInvariant);

    private AppVersion(string value)
    {
        Value = value;
    }

    /// <summary>The version string.</summary>
    public string Value { get; }

    /// <summary>The version for this text, which must be exactly major.minor.build. A refusal does not repeat the text.</summary>
    public static AppVersion Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (CiShape.IsMatch(text))
        {
            return new AppVersion(text);
        }

        var match = Shape.Match(text);
        if (!match.Success)
        {
            throw new ArgumentException("That is not a build version Diagnostics can carry.", nameof(text));
        }

        var build = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        if (build is >= 1900 and <= 2100)
        {
            throw new ArgumentException("That build number is shaped like a year, which Diagnostics will not carry.", nameof(text));
        }

        return new AppVersion(text);
    }
}
