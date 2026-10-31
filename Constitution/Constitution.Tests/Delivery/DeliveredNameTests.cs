using Constitution.Domain.Delivery;

namespace Constitution.Tests.Delivery;

public class DeliveredNameTests
{
    private static readonly DateOnly Day = new(2026, 3, 14);

    [Fact]
    public void NamesTheKindAndTheDay()
    {
        Assert.Equal("Constitution — Workout — 2026-03-14", DeliveredName.For(DeliveryKind.Workout, Day));
    }

    [Fact]
    public void TheSameDocumentHasOneName()
    {
        Assert.Equal(DeliveredName.For(DeliveryKind.Meals, Day), DeliveredName.For(DeliveryKind.Meals, Day));
        Assert.NotEqual(DeliveredName.For(DeliveryKind.Meals, Day), DeliveredName.For(DeliveryKind.Digest, Day));
        Assert.NotEqual(DeliveredName.For(DeliveryKind.Meals, Day), DeliveredName.For(DeliveryKind.Meals, Day.AddDays(1)));
    }

    [Fact]
    public void ASubjectJoinsTheName()
    {
        Assert.Equal(
            "Constitution — Workout — 2026-03-14 — Assessment",
            DeliveredName.For(DeliveryKind.Workout, Day, "Assessment"));
    }

    [Theory]
    [InlineData("Legs / Core", "Legs - Core")]
    [InlineData("  Upper   body ", "Upper body")]
    [InlineData("a:b*c?", "a-b-c-")]
    public void ASubjectCannotBreakTheTitle(string subject, string cleaned)
    {
        Assert.EndsWith(" — " + cleaned, DeliveredName.For(DeliveryKind.Workout, Day, subject));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankSubjectIsLeftOut(string? subject)
    {
        Assert.Equal("Constitution — Meals — 2026-03-14", DeliveredName.For(DeliveryKind.Meals, Day, subject));
    }
}
