using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// The face book survives being closed, or every confirmation the owner made is asked again
/// tomorrow — which is the fastest way to make them stop answering.
/// </summary>
public class FaceBookCodecTests
{
    [Fact]
    public void AKnownPersonComesBackKnown()
    {
        var book = FaceBook.Empty
            .Learn("c1", "Jordan", [1, 0, 0])
            .Learn("c1", "Jordan", [0.2f, 0.98f, 0])
            .Learn("c2", "Ruth", [0, 0, 1]);

        var back = FaceBookCodec.Read(FaceBookCodec.Write(book));

        Assert.Equal(2, back.People.Count);
        Assert.Equal("Jordan", back.Nearest([1, 0, 0]).Name);
        Assert.Equal("Jordan", back.Nearest([0.2f, 0.98f, 0]).Name);
        Assert.Equal("Ruth", back.Nearest([0, 0, 1]).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"people\":\"wrong shape\"}")]
    public void GarbledOrAbsentReadsAsAnEmptyBook(string? stored)
    {
        // Losing it costs a few confirmations the owner can make again. A head that will not
        // launch costs everything.
        Assert.Empty(FaceBookCodec.Read(stored).People);
    }

    [Fact]
    public void AnEmptyBookRoundTrips()
    {
        Assert.Empty(FaceBookCodec.Read(FaceBookCodec.Write(FaceBook.Empty)).People);
    }
}
