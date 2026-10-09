using Pulp.Domain.Core;
using Xunit;

namespace Pulp.Tests.Core;

public class CoreIdsTests
{
    [Fact]
    public void IdsCompareByValueAndIgnoreCase()
    {
        Assert.Equal(new ServiceId("example-flix"), new ServiceId("Example-Flix"));
        Assert.Equal(new TitleId("show-1"), new TitleId("show-1"));
        Assert.NotEqual(new ViewerId("ada"), new ViewerId("bo"));
    }

    [Fact]
    public void AnIdMustHaveAValue()
    {
        Assert.Throws<ArgumentException>(() => new ServiceId(" "));
        Assert.Throws<ArgumentException>(() => new TitleId(""));
        Assert.Throws<ArgumentException>(() => new ViewerId(""));
    }

    [Fact]
    public void AnIdPrintsAsItsValue() => Assert.Equal("show-1", new TitleId("show-1").ToString());
}
