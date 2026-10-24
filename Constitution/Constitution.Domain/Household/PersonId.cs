using System.Text.RegularExpressions;

namespace Constitution.Domain.Household;

/// <summary>
/// A person's stable id on the roster: lower-case words and dashes, never the name, so a rename moves no
/// record. It is the id half of the record's key, which the library lower-cases; allowing only what
/// survives that unchanged keeps one person one key.
/// </summary>
public readonly record struct PersonId
{
    public const int MaxLength = 64;

    private static readonly Regex Shape = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public PersonId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaxLength || !Shape.IsMatch(value))
        {
            throw new ArgumentException("A person id is lower-case letters and digits joined by single dashes, at most " + MaxLength + " long.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>Whether a person on the roster is an adult or a child.</summary>
public enum MemberRole
{
    Adult,
    Child,
}

/// <summary>
/// A birthdate, sealed. It exists as its own type so that nothing which should never hold one can: a
/// list row, a label or a Diagnostics line has no member of this type, and a guard walks the Domain to
/// prove it (<c>BirthdateShapeTests</c>). Its <see cref="ToString"/> is a redaction, because
/// interpolation is the one hole a type cannot close — C# calls it with no cast and no warning.
/// </summary>
public readonly record struct Birthdate(DateOnly Date)
{
    public const string Redacted = "[birthdate]";

    public override string ToString() => Redacted;
}
