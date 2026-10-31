namespace Constitution.Domain.Waiting;

/// <summary>
/// The two readings a low-confidence handwritten number could be, carried in the notice's own cause
/// key (<c>read:subject:first|second</c>) because that key is the only thing a notice has to carry
/// facts and is what resolves it. Until the read-back worker (Release C) raises these, nothing
/// does; this is the shape it will use.
/// </summary>
public sealed record LowConfidenceCause(string Subject, string First, string Second)
{
    private const string Prefix = "read:";

    /// <summary>The cause key for a read of <paramref name="subject"/> that could be either value.</summary>
    public static string Key(string subject, string first, string second)
    {
        Check(subject, nameof(subject), ":|");
        Check(first, nameof(first), ":|");
        Check(second, nameof(second), ":|");
        return $"{Prefix}{subject}:{first}|{second}";
    }

    /// <summary>Reads a key written by <see cref="Key"/>; null for any other cause.</summary>
    public static LowConfidenceCause? TryParse(string causeKey)
    {
        if (causeKey is null || !causeKey.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = causeKey[Prefix.Length..].Split(':');
        if (rest.Length != 2)
        {
            return null;
        }

        var values = rest[1].Split('|');
        if (rest[0].Length == 0 || values.Length != 2 || values[0].Length == 0 || values[1].Length == 0)
        {
            return null;
        }

        return new LowConfidenceCause(rest[0], values[0], values[1]);
    }

    private static void Check(string part, string name, string forbidden)
    {
        if (string.IsNullOrWhiteSpace(part) || part.IndexOfAny(forbidden.ToCharArray()) >= 0)
        {
            throw new ArgumentException(
                $"A low-confidence cause part must be non-empty and free of '{forbidden}' so it reads back; got \"{part}\".", name);
        }
    }
}
