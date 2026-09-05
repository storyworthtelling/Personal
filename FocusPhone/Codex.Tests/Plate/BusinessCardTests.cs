using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// Reading a business card. Plate is asked to look — by Post's Rolodex — and returns what
/// it found as text; it never decides what a piece of paper is <i>for</i>.
///
/// <para>The owner corrects the result before it goes back, so this errs toward leaving a
/// field empty rather than guessing. A wrong phone number that looks right is worse than a
/// blank one, because nobody re-reads a field that is already filled.</para>
/// </summary>
public class BusinessCardTests
{
    [Fact]
    public void AnOrdinaryCardGivesUpAllThree()
    {
        var reading = BusinessCard.Read([
            "HARBOURLINE",
            "Marguerite Okonjo",
            "Principal Surveyor",
            "(503) 555-0142",
            "m.okonjo@harbourline.com",
        ]);

        Assert.Equal("Marguerite Okonjo", reading.Name);
        Assert.Equal("(503) 555-0142", reading.Phone);
        Assert.Equal("m.okonjo@harbourline.com", reading.Email);
    }

    [Fact]
    public void ThePhoneIsReturnedExactlyAsPrinted()
    {
        // Not normalised. The owner is about to read it back against the card in their
        // hand, and a number reshaped into some other convention is one they cannot check.
        var reading = BusinessCard.Read(["Ada Speight", "+1 503-555-0142 ext. 12"]);

        Assert.Equal("+1 503-555-0142 ext. 12", reading.Phone);
    }

    [Fact]
    public void AShoutedCompanyNameIsNotAPersonsName()
    {
        // A card almost always leads with the firm. Taking it as the name is the single
        // most likely wrong answer, and it looks right enough that nobody would fix it.
        var reading = BusinessCard.Read(["HARBOURLINE MARINE LTD", "Ada Speight"]);

        Assert.Equal("Ada Speight", reading.Name);
    }

    [Theory]
    [InlineData("www.harbourline.com")]
    [InlineData("https://harbourline.com")]
    [InlineData("41 Quay Street, Portland OR 97204")]
    [InlineData("Principal Surveyor")]
    public void AddressesUrlsAndTitlesAreNotNames(string noise)
    {
        var reading = BusinessCard.Read([noise, "Ada Speight"]);

        Assert.Equal("Ada Speight", reading.Name);
    }

    [Fact]
    public void ACardWithNothingRecognisableComesBackEmptyRatherThanWrong()
    {
        var reading = BusinessCard.Read(["", "   "]);

        Assert.Equal(string.Empty, reading.Name);
        Assert.Equal(string.Empty, reading.Phone);
        Assert.Equal(string.Empty, reading.Email);
    }

    [Fact]
    public void NoLinesAtAllIsNotAnError()
    {
        // A frame with no text in it is the ordinary result of a bad angle, not a fault.
        var reading = BusinessCard.Read([]);

        Assert.Equal(string.Empty, reading.Name);
    }

    [Fact]
    public void TheFirstEmailWinsAndTheRestAreLeftAlone()
    {
        var reading = BusinessCard.Read([
            "Ada Speight",
            "ada@harbourline.com",
            "accounts@harbourline.com",
        ]);

        Assert.Equal("ada@harbourline.com", reading.Email);
    }

    [Fact]
    public void ANumberTooShortToBeAPhoneIsIgnored()
    {
        // Suite numbers, postcodes and years all sit on cards. Ten digits is the shortest
        // thing anyone can actually dial.
        var reading = BusinessCard.Read(["Ada Speight", "Suite 412", "Est. 1998"]);

        Assert.Equal(string.Empty, reading.Phone);
    }

    [Fact]
    public void TheEmailLineIsNeverAlsoTheName()
    {
        var reading = BusinessCard.Read(["ada@harbourline.com"]);

        Assert.Equal(string.Empty, reading.Name);
        Assert.Equal("ada@harbourline.com", reading.Email);
    }
}
