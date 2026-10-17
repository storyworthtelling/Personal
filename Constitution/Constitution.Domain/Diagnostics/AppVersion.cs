using System.Globalization;
using System.Text.RegularExpressions;

namespace Constitution.Domain.Diagnostics;

/// <summary>
/// The app's display version ("1.0.57": major, minor, then the CI run number). Three dotted numbers
/// and nothing else, and a build number that looks like a year is refused, because the three-part
/// shape is also how a date of birth is written ("9.3.1987") and the build step has no reason to
/// produce a four-digit number between 1900 and 2100 that a person's birth year could be.
///
/// <para><b>What this cannot tell apart:</b> a date written with a two-digit year and no zero
/// padding, "9.3.87". The version comes from the build, not from data, so the caller is the build.</para>
/// </summary>
public sealed record AppVersion
{
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
