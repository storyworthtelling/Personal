using System.Security.Cryptography;
using System.Text;
using Pulp.Domain.Children;
using Pulp.Domain.Core;

namespace Pulp.Domain.Voice;

/// <summary>
/// Everything a seat's composer may be told, and nothing else: a few sentences already reduced to
/// words, and — on a child's seat alone — the child's age band, which sets the reading level (D233).
/// Built by <see cref="VoiceFactBuilder"/> and <see cref="ChildVoiceFacts"/>.
/// </summary>
public sealed record VoiceFacts(VoiceSeat Seat, IReadOnlyList<string> Lines, AgeBand? ReadingLevel)
{
    public VoicedOccasion Occasion => VoicedOccasions.For(Seat);

    public bool HasFacts => Lines.Count > 0;

    /// <summary>The context handed to the composer: the facts, then the reading level when a child hears it.</summary>
    public string Context
    {
        get
        {
            var parts = new List<string>(Lines);
            if (ReadingLevel is { } band)
            {
                parts.Add(Voice.ReadingLevel.Guidance(band));
            }

            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// A short, stable fingerprint of the seat, its facts and its reading level — what the line plan
    /// keys on. Not <c>string.GetHashCode</c>, which is randomised per process.
    /// </summary>
    public string Hash
    {
        get
        {
            var text = $"{Seat}\n{ReadingLevel}\n{string.Join('\n', Lines)}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
        }
    }
}

/// <summary>Who is looking at a title page, as far as the why-this line cares (D23, D41).</summary>
public enum ViewerStanding
{
    /// <summary>The device owner, whose taste chose the title and whose own history may be read back.</summary>
    DeviceOwner,

    /// <summary>The other adult: shown the reason, never told a past title (D41, D195).</summary>
    OtherAdult,

    /// <summary>A child: no taste profile (D23), so no why-this at all.</summary>
    Child,
}

/// <summary>A title a viewer finished that is like the one on the page; the caller's taste decides "like".</summary>
public sealed record PastTitle(ViewerId Viewer, string Name, DateTimeOffset FinishedAt);

/// <summary>A title as a child's seat may know it: its filter facts and its display name.</summary>
public sealed record NamedTitle(TitleFacts Facts, string Name);
