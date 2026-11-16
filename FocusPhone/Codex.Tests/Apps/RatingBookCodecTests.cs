using Codex.Domain.Apps;
using Codex.Infrastructure.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class RatingBookCodecTests
{
    [Fact]
    public void RoundTrips()
    {
        var now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var book = RatingBook.Empty
            .Record("com.example.forum", 7, new PlayLookup(RatingLookupOutcome.Rated, ContentRating.Mature), now)
            .Record("com.example.notes", 2, new PlayLookup(RatingLookupOutcome.Failed, ContentRating.Unknown), now);

        var decoded = RatingBookCodec.Decode(RatingBookCodec.Encode(book));

        Assert.Equal(book.Entries["com.example.forum"], decoded.Entries["com.example.forum"]);
        Assert.Equal(book.Entries["com.example.notes"], decoded.Entries["com.example.notes"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    public void AnAbsentOrGarbledFile_IsAnEmptyBook(string? json)
    {
        Assert.Empty(RatingBookCodec.Decode(json).Entries);
    }
}
