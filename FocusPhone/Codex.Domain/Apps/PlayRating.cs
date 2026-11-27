using System.Net;
using System.Text.RegularExpressions;
using Codex.Domain.Policy;

namespace Codex.Domain.Apps;

/// <summary>
/// Reads an app's content rating off its US Google Play listing. Android gives one app no way to read
/// another's Play rating, so Flyleaf asks the listing page itself (owner's ruling): an unofficial source,
/// read first from the page's structured data and then from the visible label, so a redesign of one does
/// not lose both. A page that carries neither reads as <see cref="ContentRating.Unknown"/>.
/// </summary>
public static class PlayRating
{
    private static readonly Regex StructuredData = new(
        "\"contentRating\"\\s*:\\s*\"(?<label>[^\"]+)\"", RegexOptions.Compiled);

    private static readonly Regex VisibleLabel = new(
        "itemprop=\"contentRating\"[^>]*>\\s*(?:<span[^>]*>\\s*)?(?<label>[^<]+)<", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    // The Play store category slug ("ENTERTAINMENT", "GAME", …), read from the listing's structured data
    // first and then from the category link, so a redesign of one does not lose both. Unofficial, like the
    // rating: the first device run confirms it against a live listing.
    private static readonly Regex StructuredCategory = new(
        "\"applicationCategory\"\\s*:\\s*\"(?<genre>[A-Za-z_]+)\"", RegexOptions.Compiled);

    private static readonly Regex CategoryLink = new(
        "/store/apps/category/(?<genre>[A-Za-z_]+)", RegexOptions.Compiled);

    /// <summary>The Play category the Entertainment block reads. Streaming apps declare it.</summary>
    public const string EntertainmentGenre = "ENTERTAINMENT";

    /// <summary>
    /// The listing's Play category, upper-cased, or null when the page names none. An empty string is never
    /// returned: a page read with no category is a page read, so a caller can tell it from one never read.
    /// </summary>
    public static string? ReadGenre(string html)
    {
        foreach (var pattern in new[] { StructuredCategory, CategoryLink })
        {
            var match = pattern.Match(html);
            if (match.Success)
            {
                return match.Groups["genre"].Value.ToUpperInvariant();
            }
        }
        return null;
    }

    /// <summary>True for the Play Entertainment category, which the lockdown never shows.</summary>
    public static bool IsEntertainment(string? genre) =>
        string.Equals(genre, EntertainmentGenre, StringComparison.OrdinalIgnoreCase);

    /// <summary>The US labels (ESRB through IARC). Anything else, another region's label included, is Unknown.</summary>
    public static ContentRating FromLabel(string? label)
    {
        var words = Whitespace.Replace(WebUtility.HtmlDecode(label ?? string.Empty), " ").Trim().ToLowerInvariant();
        return words switch
        {
            "everyone" or "everyone 10+" => ContentRating.Everyone,
            "teen" => ContentRating.Teen,
            "mature 17+" => ContentRating.Mature,
            "adults only 18+" => ContentRating.AdultsOnly,
            _ => ContentRating.Unknown,
        };
    }

    /// <summary>Rated M or above: the ratings Flyleaf never shows.</summary>
    public static bool IsMature(ContentRating rating) => rating is ContentRating.Mature or ContentRating.AdultsOnly;

    public static ContentRating ReadListing(string html)
    {
        foreach (var pattern in new[] { StructuredData, VisibleLabel })
        {
            var match = pattern.Match(html);
            if (match.Success)
            {
                var rating = FromLabel(match.Groups["label"].Value);
                if (rating is not ContentRating.Unknown)
                {
                    return rating;
                }
            }
        }
        return ContentRating.Unknown;
    }

    /// <summary>
    /// Never judged by rating: the owner's suite (not on Play), the cutover allowlist, and the streaming
    /// services Pulp hands off to (checked Teen on Play in Nov 2026; exempt by name so a rating change can
    /// never take Pulp's hand-off away).
    /// </summary>
    public static bool IsExempt(string package) =>
        package.StartsWith(PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal)
        || PolicyContext.AllowlistedPackages.Contains(package)
        || LockedOpenCatalogue.Packages.Contains(package);
}

public enum RatingLookupOutcome
{
    Rated,
    NotListed,
    Unreadable,
    Failed,
}

/// <summary>What one look at a listing found.</summary>
public record PlayLookup(RatingLookupOutcome Outcome, ContentRating Rating, string? Genre = null);

/// <summary>
/// An app's last known rating, the version it was read for, and when. <see cref="Genre"/> is the Play
/// category read with it: null when the app was read before categories were, "" when the page named none.
/// </summary>
public record RatingEntry(
    ContentRating Rating, long VersionCode, DateTimeOffset CheckedAt, RatingLookupOutcome Outcome, string? Genre = null);

/// <summary>Every installed app's last known Play rating, kept on the phone.</summary>
public sealed record RatingBook(IReadOnlyDictionary<string, RatingEntry> Entries)
{
    public static RatingBook Empty { get; } = new(new Dictionary<string, RatingEntry>());

    public ContentRating RatingOf(string package) =>
        Entries.TryGetValue(package, out var entry) ? entry.Rating : ContentRating.Unknown;

    /// <summary>The Play category last read for the app; null when none has been read.</summary>
    public string? GenreOf(string package) =>
        Entries.TryGetValue(package, out var entry) ? entry.Genre : null;

    public IReadOnlySet<string> MaturePackages() =>
        Entries.Where(e => PlayRating.IsMature(e.Value.Rating)).Select(e => e.Key).ToHashSet();

    /// <summary>
    /// Records a lookup. A failed or unreadable one keeps the rating and category the app already had, so a
    /// flaky network or a redesigned page never releases an app that was rated M or Entertainment; an app
    /// not on Play is Unknown.
    /// </summary>
    public RatingBook Record(string package, long versionCode, PlayLookup lookup, DateTimeOffset now)
    {
        var entries = new Dictionary<string, RatingEntry>(Entries);
        entries[package] = lookup.Outcome switch
        {
            RatingLookupOutcome.Rated => new RatingEntry(lookup.Rating, versionCode, now, RatingLookupOutcome.Rated, lookup.Genre ?? string.Empty),
            RatingLookupOutcome.NotListed => new RatingEntry(ContentRating.Unknown, versionCode, now, RatingLookupOutcome.NotListed),
            _ => FailedEntry(package, versionCode, now),
        };
        return new RatingBook(entries);
    }

    private RatingEntry FailedEntry(string package, long versionCode, DateTimeOffset now)
    {
        var prior = Entries.TryGetValue(package, out var entry) ? entry : null;
        return new RatingEntry(
            RatingOf(package),
            prior?.VersionCode ?? versionCode,
            now,
            RatingLookupOutcome.Failed,
            prior?.Genre);
    }

    public RatingBook RetainOnly(IEnumerable<string> installed)
    {
        var keep = installed.ToHashSet();
        return new RatingBook(Entries.Where(e => keep.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value));
    }
}

/// <summary>Which installed apps to look up this run.</summary>
public static class RatingRefresh
{
    /// <summary>A rating read this long ago is read again, since a developer can re-rate an app.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromDays(30);

    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromDays(1);

    /// <summary>At most this many listings a run, so a first run on a full phone is spread out.</summary>
    public const int PerRun = 40;

    public static IReadOnlyList<string> Due(
        IEnumerable<(string Package, long VersionCode)> installed, RatingBook book, DateTimeOffset now) =>
        installed
            .Where(app => !PlayRating.IsExempt(app.Package))
            .DistinctBy(app => app.Package)
            .Select(app => (app.Package, Entry: book.Entries.TryGetValue(app.Package, out var entry) ? entry : null,
                app.VersionCode))
            .Where(app => app.Entry is null || IsDue(app.Entry, app.VersionCode, now))
            .OrderBy(app => app.Entry?.CheckedAt ?? DateTimeOffset.MinValue)
            .Take(PerRun)
            .Select(app => app.Package)
            .ToList();

    // An app rated before its category was read is read again once, so the Entertainment block can see it.
    private static bool IsDue(RatingEntry entry, long versionCode, DateTimeOffset now) =>
        entry.Outcome is RatingLookupOutcome.Failed
            ? now - entry.CheckedAt >= RetryAfterFailure
            : entry.VersionCode != versionCode
              || now - entry.CheckedAt >= Fresh
              || (entry.Outcome is RatingLookupOutcome.Rated && entry.Genre is null);
}
