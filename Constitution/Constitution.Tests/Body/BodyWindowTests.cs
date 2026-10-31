using Constitution.Domain.Body;

namespace Constitution.Tests.Body;

public class BodyWindowTests
{
    [Fact]
    public void MonthIsTheDefault()
    {
        Assert.Equal(BodyWindow.Month, default(BodyWindow));
        Assert.Equal(BodyWindow.Month, BodyWindows.Default);
    }

    [Fact]
    public void WindowsHaveDayCounts()
    {
        Assert.Equal(30, BodyWindows.Days(BodyWindow.Month));
        Assert.Equal(90, BodyWindows.Days(BodyWindow.Quarter));
        Assert.Equal(365, BodyWindows.Days(BodyWindow.Year));
    }

    [Fact]
    public void AllListsThreeInSwitchOrder()
    {
        Assert.Equal([BodyWindow.Month, BodyWindow.Quarter, BodyWindow.Year], BodyWindows.All);
    }

    [Fact]
    public void AnUndefinedWindowIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BodyWindows.Days((BodyWindow)7));
    }

    [Fact]
    public void ABodyReadingCarriesOnlyWhatWasMeasured()
    {
        // No name, birthdate or person: the install already is the person (brief, Two installs).
        var members = typeof(BodyReading).GetProperties().Select(property => property.Name).OrderBy(name => name).ToList();
        Assert.Equal(["Day", "Signal", "Value"], members);
        Assert.Empty(typeof(BodyReading).GetFields(System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Where(field => !field.Name.Contains("k__BackingField", StringComparison.Ordinal)));
    }
}
