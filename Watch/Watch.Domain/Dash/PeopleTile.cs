using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Watch.Domain.Design;

namespace Watch.Domain.Dash;

/// <summary>Where a name stands on the tile: the newest in full, then two earlier ones, fading (A72).</summary>
public enum TileRank
{
    Newest,
    Earlier,
    Earliest,
}

/// <summary>A name the tile shows, and how far back it is.</summary>
public sealed record TileName(string Name, TileRank Rank);

/// <summary>
/// What the People waiting tile shows (W-5.1.7, A72; mock: Tile: People waiting, Tile: nobody waiting): the names waiting,
/// newest first, at most three; or, when the phone could not be asked, the quiet line that says why. Nobody waiting is
/// neither: the faint dash and nothing else. Never a count, a line of anyone's words, or a time.
/// </summary>
public sealed record TileFace(IReadOnlyList<TileName> Names, string? Quiet)
{
    /// <summary>Nobody is waiting: the faint dash alone.</summary>
    public bool Nobody => Names.Count == 0 && Quiet is null;

    /// <summary>The tile as TalkBack reads it.</summary>
    public string Description => Quiet ?? DashWords.Waiting([.. Names.Select(name => name.Name)]);
}

/// <summary>
/// The People waiting tile's pure half (P14 B8, W-5.1.7). It is People waiting's own answer from Post, drawn glyph-led: the
/// dash, the newest name in full and the two before it fading, and no count however many wait (A72). The tile holds
/// nothing of its own: it asks Post when the system asks it, as People waiting does.
/// </summary>
public static class PeopleTile
{
    /// <summary>The most names the tile shows: the newest and two earlier (the mockup draws three).</summary>
    public const int MostNames = 3;

    /// <summary>How long the system may keep the tile before it asks again; a text arriving asks sooner.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(10);

    private static readonly TileRank[] Ranks = [TileRank.Newest, TileRank.Earlier, TileRank.Earliest];

    public static TileFace Of(DashPeople people) =>
        new([.. people.People
            .Select((person, order) => (person, order))
            .OrderByDescending(entry => entry.person.At)
            .ThenBy(entry => entry.order)
            .Take(MostNames)
            .Select((entry, index) => new TileName(entry.person.Name, Ranks[index]))], null);

    /// <summary>The phone couldn't be asked: no names, and the quiet line that says why (WD §6).</summary>
    public static TileFace Unreached(DashTrouble trouble) => new([], DashWords.For(trouble));

    /// <summary>How much of the grey a name of this rank carries: the newest is the ink, the earlier two fade.</summary>
    public static double AlphaOf(TileRank rank) => rank switch
    {
        TileRank.Newest => 1,
        TileRank.Earlier => WatchTokens.Light.Earlier,
        TileRank.Earliest => WatchTokens.Light.Earliest,
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, "Not a rank the tile draws."),
    };

    /// <summary>The dash at full glass with someone waiting, faint otherwise.</summary>
    public static double GlyphAlpha(TileFace face) => face.Names.Count > 0 ? 1 : WatchTokens.Light.Quiet;

    /// <summary>
    /// The picture's version, which the system keeps the drawn tile by: a digest, so it changes with the names, the line,
    /// the dash's colour and the screen's size, and carries none of them.
    /// </summary>
    public static string Version(TileFace face, int glyphArgb, int width, int height)
    {
        var drawn = new StringBuilder();
        foreach (var name in face.Names)
        {
            drawn.Append(name.Rank).Append('\n').Append(name.Name).Append('\n');
        }
        drawn.Append(face.Quiet ?? string.Empty).Append('\n')
            .Append(glyphArgb.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(width.ToString(CultureInfo.InvariantCulture)).Append('x').Append(height.ToString(CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(drawn.ToString()));
        return "t" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
