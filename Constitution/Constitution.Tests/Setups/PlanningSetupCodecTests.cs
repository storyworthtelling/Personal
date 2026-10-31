using Constitution.Domain.Calendar;

namespace Constitution.Tests.Setups;

/// <summary>
/// The household's setup as the phone keeps it. The expected file is written out as the file (a round trip through the code alone would
/// pass with a key renamed at both ends). A file that cannot be used is a plain sentence that quotes nothing from it: it may hold an
/// address, and the sentence goes to a notice and a trail.
/// </summary>
public class PlanningSetupCodecTests
{
    private static PlanningSetup Configured(string json) =>
        Assert.IsType<HouseholdSetupRead.Configured>(PlanningSetupCodec.Read(json)).Setup;

    private static string NotSet(string? json) =>
        Assert.IsType<HouseholdSetupRead.NotSet>(PlanningSetupCodec.Read(json)).Reason;

    [Fact]
    public void ItReadsTheFileThePhoneHolds()
    {
        var setup = Configured(HouseholdData.File);

        Assert.Equal(["Sam", "Lee"], setup.Adults.Select(adult => adult.Name));
        var sam = setup.Adults[0];
        Assert.Equal("sam@example.com", sam.CalendarId);
        Assert.True(sam.OnPickup);
        Assert.Equal(["sam@example.com"], sam.Addresses);
        Assert.Equal(["Dad"], sam.OtherNames);
        Assert.True(sam.DeliversHere);
        var lee = setup.Adults[1];
        Assert.False(lee.OnPickup);
        Assert.False(lee.DeliversHere);
        Assert.Equal("family@example.com", setup.FamilyCalendarId);
        Assert.Equal(["robin@example.com", "juniper@example.com"], setup.ChildrenCalendarIds);
        Assert.Equal("UTC", setup.ZoneId);
        Assert.Equal(new TimeOnly(18, 0), setup.DinnerStart);
        Assert.Equal(TimeSpan.FromMinutes(45), setup.DinnerLength);
        Assert.Equal(new TimeOnly(20, 0), setup.Bedtime);
    }

    [Fact]
    public void WhatItWritesItReadsBack()
    {
        var written = PlanningSetupCodec.Write(HouseholdData.Setup());

        var back = Configured(written);

        Assert.Equal(HouseholdData.Setup().Adults.Select(adult => adult.Name), back.Adults.Select(adult => adult.Name));
        Assert.Equal(HouseholdData.Setup().FamilyCalendarId, back.FamilyCalendarId);
        Assert.Equal(HouseholdData.Setup().ChildrenCalendarIds, back.ChildrenCalendarIds);
        Assert.Equal(HouseholdData.Setup().DinnerStart, back.DinnerStart);
        Assert.Equal(HouseholdData.Setup().DinnerLength, back.DinnerLength);
        Assert.Equal(HouseholdData.Setup().Bedtime, back.Bedtime);
        Assert.Equal(["Sam"], back.DeliverFor);
        Assert.Equal(written, PlanningSetupCodec.Write(back));
    }

    [Fact]
    public void TheWrittenFileUsesTheFrozenKeys()
    {
        var written = PlanningSetupCodec.Write(HouseholdData.Setup());

        foreach (var key in new[] { "version", "zone", "adults", "name", "calendar", "pickup", "addresses", "otherNames", "deliversHere", "familyCalendar", "childrenCalendars", "dinner", "dinnerMinutes", "bedtime" })
        {
            Assert.Contains($"\"{key}\"", written, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NothingSavedIsNotSet()
    {
        Assert.Equal("No household has been set up on this phone.", NotSet(null));
        Assert.Equal("No household has been set up on this phone.", NotSet("   "));
    }

    [Fact]
    public void GarbledJsonIsNotSetAndQuotesNothing()
    {
        var reason = NotSet("{\"adults\":[{\"name\":\"Sam\",\"calendar\":\"sam@example.com\" oops");

        Assert.StartsWith("The saved household setup is not valid JSON", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("sam@example.com", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Sam", reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"version\":2,\"adults\":[{\"name\":\"Sam\"}]}")]
    [InlineData("{\"adults\":[{\"name\":\"Sam\"}]}")]
    [InlineData("{\"version\":1,\"adults\":\"Sam\"}")]
    [InlineData("{\"version\":1}")]
    public void AnotherShapeIsNotSet(string json)
    {
        Assert.Equal("The saved household setup is not in a shape this version reads.", NotSet(json));
    }

    [Fact]
    public void NoAdultIsNotSet()
    {
        Assert.Equal("The saved household setup names no adult.", NotSet("{\"version\":1,\"adults\":[]}"));
    }

    [Fact]
    public void AnAdultWithNoNameIsNotSet()
    {
        Assert.Equal("An adult in the saved household setup has no name.", NotSet("{\"version\":1,\"adults\":[{\"calendar\":\"x@example.com\"}]}"));
    }

    [Fact]
    public void TwoAdultsWithOneNameIsNotSet()
    {
        var json = "{\"version\":1,\"adults\":[{\"name\":\"Sam\"},{\"name\":\"sam\"}]}";

        Assert.Equal("Two adults in the saved household setup share a name.", NotSet(json));
    }

    [Fact]
    public void AnAdultWithNoCalendarIsKeptWithNone()
    {
        var setup = Configured("{\"version\":1,\"adults\":[{\"name\":\"Sam\"},{\"name\":\"Lee\",\"calendar\":\"  \"}]}");

        Assert.All(setup.Adults, adult => Assert.Null(setup.CalendarOf(adult.Name)));
    }

    [Fact]
    public void WhatIsLeftOutTakesItsDefault()
    {
        var setup = Configured("{\"version\":1,\"adults\":[{\"name\":\"Sam\",\"calendar\":\"sam@example.com\"}]}");

        Assert.Null(setup.FamilyCalendarId);
        Assert.False(setup.NoFamilyCalendar);
        Assert.Empty(setup.ChildrenCalendarIds);
        Assert.False(setup.NoChildren);
        Assert.Null(setup.ZoneId);
        Assert.Null(setup.DinnerStart);
        Assert.Null(setup.DinnerLength);
        Assert.Null(setup.Bedtime);
        Assert.Null(setup.Adults[0].OnPickup);
        Assert.Empty(setup.DeliverFor);
    }

    [Fact]
    public void ADeliberateNoneIsKeptApartFromNotSet()
    {
        var setup = Configured("{\"version\":1,\"adults\":[{\"name\":\"Sam\"}],\"noFamilyCalendar\":true,\"noChildren\":true}");

        Assert.True(setup.NoFamilyCalendar);
        Assert.True(setup.NoChildren);
    }

    [Theory]
    [InlineData("\"dinner\":\"half past five\"")]
    [InlineData("\"dinner\":\"25:00\"")]
    [InlineData("\"dinner\":17")]
    public void ATimeThatCannotBeReadIsNotGiven(string field)
    {
        var setup = Configured("{\"version\":1,\"adults\":[{\"name\":\"Sam\"}]," + field + "}");

        Assert.Null(setup.DinnerStart);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(600)]
    public void ALengthThatMakesNoSenseIsNotGiven(int minutes)
    {
        var setup = Configured("{\"version\":1,\"adults\":[{\"name\":\"Sam\"}],\"dinner\":\"18:00\",\"dinnerMinutes\":" + minutes + "}");

        Assert.Equal(new TimeOnly(18, 0), setup.DinnerStart);
        Assert.Null(setup.DinnerLength);
    }

    [Fact]
    public void KeysAreReadWithoutRegardToCase()
    {
        var setup = Configured("{\"VERSION\":1,\"Adults\":[{\"NAME\":\"Sam\",\"Calendar\":\"sam@example.com\"}]}");

        Assert.Equal("sam@example.com", setup.CalendarOf("sam"));
    }

    [Fact]
    public void ABlankCalendarOrAddressInAListIsDropped()
    {
        var setup = Configured("{\"version\":1,\"adults\":[{\"name\":\"Sam\",\"addresses\":[\"\",\"sam@example.com\"]}],\"childrenCalendars\":[\" \",\"robin@example.com\"]}");

        Assert.Equal(["sam@example.com"], setup.Adults[0].Addresses);
        Assert.Equal(["robin@example.com"], setup.ChildrenCalendarIds);
    }
}
