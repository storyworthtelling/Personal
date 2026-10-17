using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Constitution.Domain.Voice;

namespace Constitution.Infrastructure.Voice;

/// <summary>
/// What a stored line is filed under: the seat's <b>stored id</b> (never an enum name, so a rename
/// cannot orphan the day's lines), a fingerprint of the seat's ordered facts, and the local date.
/// The same shape as Index's seat cache (<c>seat|facts|day</c>).
///
/// <para>The fingerprint is a SHA-256 of the seat id and the facts, one per line, cut to 16 hex
/// characters. It is not <c>string.GetHashCode</c>, which is randomised per process and would make
/// every launch a cache miss. The fallback is not in it: it is built from the same facts, and
/// rewording it must not cost a model call. Order matters, because the facts are told in order.</para>
/// </summary>
public sealed record SeatLineKey(string SeatId, string FactsHash, DateOnly Day)
{
    public static SeatLineKey For(SeatRequest request, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(request);
        var seatId = VoicedOccasions.For(request.Seat).Id;
        var text = seatId + "\n" + string.Join('\n', request.Facts);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
        return new SeatLineKey(seatId, hash, day);
    }

    public override string ToString() =>
        $"{SeatId}|{FactsHash}|{Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
}
