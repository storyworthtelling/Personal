using Constitution.Domain.Meals;

namespace Constitution.Tests.AteSomethingElse;

/// <summary>The reply is parsed strictly: an unusable one is "could not estimate", never zero calories, and the numbers are bounded.</summary>
public class AteReplyTests
{
    private const string Good =
        "{\"is_meal\":true,\"food\":\"pasta with tomato sauce\",\"kcal_low\":750,\"kcal_high\":1050,\"protein_g_low\":25,\"protein_g_high\":45,\"confidence\":\"fair\"}";

    private static AteReading ReadOf(string? text) => Assert.IsType<AteOutcome.Read>(AteReply.Parse(text)).Reading;

    [Fact]
    public void AGoodReplyIsARangeNotAPoint()
    {
        var reading = ReadOf(Good);

        Assert.Equal(750, reading.KcalLow);
        Assert.Equal(1050, reading.KcalHigh);
        Assert.Equal(25, reading.ProteinLow);
        Assert.Equal(45, reading.ProteinHigh);
        Assert.Equal(AteConfidence.Fair, reading.Confidence);
        Assert.Equal("pasta with tomato sauce", reading.What);
        Assert.Equal(900, reading.KcalAbout);
    }

    [Fact]
    public void TheCentreRoundsToTheNearestFifty()
    {
        Assert.Equal(850, ReadOf(Good.Replace("750", "700").Replace("1050", "1020")).KcalAbout);
        Assert.Equal(300, ReadOf(Good.Replace("750", "260").Replace("1050", "340")).KcalAbout);
    }

    [Fact]
    public void AFencedReplyWithProseAroundItStillReads()
    {
        var reading = ReadOf("Here you go:\n```json\n" + Good + "\n```\nHope that helps.");

        Assert.Equal(750, reading.KcalLow);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I'm not able to help with that.")]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{}")]
    [InlineData("{\"is_meal\":true}")]
    [InlineData("{\"is_meal\":true,\"food\":\"soup\"}")]
    public void ASenselessReplyIsUnreadableNeverZero(string? text)
    {
        Assert.IsType<AteOutcome.Unreadable>(AteReply.Parse(text));
    }

    [Theory]
    [InlineData("\"kcal_low\":750,\"kcal_high\":1050", "\"kcal_low\":0,\"kcal_high\":0")]
    [InlineData("\"kcal_low\":750,\"kcal_high\":1050", "\"kcal_low\":-200,\"kcal_high\":400")]
    [InlineData("\"kcal_low\":750,\"kcal_high\":1050", "\"kcal_low\":1050,\"kcal_high\":750")]
    [InlineData("\"kcal_low\":750,\"kcal_high\":1050", "\"kcal_low\":20,\"kcal_high\":30")]
    [InlineData("\"kcal_low\":750,\"kcal_high\":1050", "\"kcal_low\":900,\"kcal_high\":90000")]
    [InlineData("\"kcal_low\":750,\"kcal_high\":1050", "\"kcal_low\":100,\"kcal_high\":3000")]
    [InlineData("\"kcal_low\":750,\"kcal_high\":1050", "\"kcal_low\":\"a lot\",\"kcal_high\":\"more\"")]
    [InlineData("\"kcal_low\":750,\"kcal_high\":1050", "\"kcal_low\":null,\"kcal_high\":null")]
    public void ImpossibleEnergyIsUnreadable(string from, string to)
    {
        Assert.IsType<AteOutcome.Unreadable>(AteReply.Parse(Good.Replace(from, to)));
    }

    [Fact]
    public void AnImplausibleProteinIsDroppedAndTheEnergyKept()
    {
        var reading = ReadOf(Good.Replace("\"protein_g_low\":25,\"protein_g_high\":45", "\"protein_g_low\":25,\"protein_g_high\":9000"));

        Assert.Null(reading.ProteinLow);
        Assert.Null(reading.ProteinHigh);
        Assert.Equal(750, reading.KcalLow);
    }

    [Fact]
    public void AMissingProteinIsNotAnError()
    {
        var reading = ReadOf("{\"is_meal\":true,\"food\":\"soup\",\"kcal_low\":200,\"kcal_high\":300,\"confidence\":\"good\"}");

        Assert.Null(reading.ProteinLow);
        Assert.Equal(AteConfidence.Good, reading.Confidence);
    }

    [Fact]
    public void ANoMealReplyIsSaidAsSuch()
    {
        Assert.IsType<AteOutcome.NoMeal>(AteReply.Parse("{\"is_meal\":false}"));
    }

    [Fact]
    public void ANoMealReplyWithEnergyIsStillNoMeal()
    {
        Assert.IsType<AteOutcome.NoMeal>(AteReply.Parse(Good.Replace("\"is_meal\":true", "\"is_meal\":false")));
    }

    [Fact]
    public void AnUnknownConfidenceIsTheCautiousOne()
    {
        Assert.Equal(AteConfidence.Low, ReadOf(Good.Replace("fair", "certain")).Confidence);
        Assert.Equal(AteConfidence.Low, ReadOf(Good.Replace(",\"confidence\":\"fair\"", string.Empty)).Confidence);
    }

    [Fact]
    public void TheDescriptionIsOneBoundedPlainLine()
    {
        var long80 = new string('x', 300);
        var reading = ReadOf(Good.Replace("pasta with tomato sauce", "pasta\\nwith\\tsauce " + long80));

        Assert.DoesNotContain('\n', reading.What);
        Assert.DoesNotContain('\t', reading.What);
        Assert.True(reading.What.Length <= AteReading.LongestWhat);
    }

    [Fact]
    public void ADescriptionIsNotRequiredToBeKindButIsRequiredToExist()
    {
        Assert.IsType<AteOutcome.Unreadable>(AteReply.Parse(Good.Replace("pasta with tomato sauce", "  ")));
    }

    [Fact]
    public void DecimalFiguresAreRoundedNotRefused()
    {
        var reading = ReadOf(Good.Replace("\"kcal_low\":750,\"kcal_high\":1050", "\"kcal_low\":749.6,\"kcal_high\":1050.2"));

        Assert.Equal(750, reading.KcalLow);
        Assert.Equal(1050, reading.KcalHigh);
    }

    [Fact]
    public void TheCreatorRefusesWhatTheParserWould()
    {
        Assert.False(AteReading.TryCreate("soup", 0, 0, null, null, AteConfidence.Low, out _));
        Assert.False(AteReading.TryCreate("soup", 400, 300, null, null, AteConfidence.Low, out _));
        Assert.False(AteReading.TryCreate("", 300, 400, null, null, AteConfidence.Low, out _));
        Assert.True(AteReading.TryCreate("soup", AteReading.MinKcal, AteReading.MinKcal * 2, null, null, AteConfidence.Low, out _));
        Assert.False(AteReading.TryCreate("soup", AteReading.MaxKcal, AteReading.MaxKcal + 1, null, null, AteConfidence.Low, out _));
    }
}
