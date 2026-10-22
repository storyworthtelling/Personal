using Folio.Domain.Pipeline;
using Xunit;

namespace Folio.Tests.Pipeline;

public class ErrorTextTests
{
    // "Software caused connection abort" is the inner IOException of a generic HttpRequestException; the
    // outer message alone says only that the request failed, which is no help in a diagnostics file.
    [Fact]
    public void Describe_IncludesEveryInnerMessage_OutermostFirst()
    {
        var error = new HttpRequestException(
            "Error while copying content to a stream.",
            new IOException("Unable to read data from the transport connection.",
                new InvalidOperationException("Software caused connection abort")));

        var text = ErrorText.Describe(error);

        Assert.Equal(
            "HttpRequestException: Error while copying content to a stream. ← " +
            "IOException: Unable to read data from the transport connection. ← " +
            "InvalidOperationException: Software caused connection abort",
            text);
    }

    [Fact]
    public void Describe_NamesTheTypeOfAnExceptionWithNoInner()
    {
        Assert.Equal("TimeoutException: too slow", ErrorText.Describe(new TimeoutException("too slow")));
    }
}
