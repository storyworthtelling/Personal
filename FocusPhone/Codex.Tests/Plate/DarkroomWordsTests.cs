using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// A Darkroom cell said aloud (audit W9.3, report 10 §7). Plate holds the only copy of each picture and a cull ends in
/// a send or a delete, so which ones are chosen has to be heard, not only seen as a wash.
/// </summary>
public class DarkroomWordsTests
{
    private static readonly DateTime Now = new(2026, 3, 4, 15, 0, 0);

    [Fact]
    public void APictureSaysWhatItIsAndWhenItWasTaken() =>
        Assert.Equal("Photo, 2h ago", DarkroomWords.Cell(DarkroomItem.Photo("a", Now.AddHours(-2)), chosen: false, Now));

    [Fact]
    public void AChosenPictureSaysSo() =>
        Assert.EndsWith(", chosen", DarkroomWords.Cell(DarkroomItem.Photo("a", Now.AddHours(-2)), chosen: true, Now));
}
