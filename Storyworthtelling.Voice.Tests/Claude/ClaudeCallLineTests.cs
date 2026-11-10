using System.Net;
using Storyworthtelling.Voice.Claude;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Claude;

/// <summary>
/// Audit 08 §12: Score's taste, discovery and hidden-track calls returned "nothing learned yet" for a rejected key, a
/// 400, an empty balance and a Drive scope never granted alike. One line per failed call says which, without the body.
/// </summary>
public class ClaudeCallLineTests
{
    [Fact]
    public void ARefusedCallNamesItsKindStatusAndRequest()
    {
        var refused = new ClaudeRequestException(
            ClaudeFailure.KeyRejected, "invalid x-api-key", HttpStatusCode.Unauthorized, null, "{\"error\":\"invalid x-api-key\"}", "req_01example");

        Assert.Equal("CLAUDE taste-refine failed: KeyRejected 401 (request req_01example)", ClaudeCallLine.Failed("taste-refine", refused));
    }

    [Fact]
    public void AnyOtherFailureIsItsTypesOnly()
    {
        var failure = new InvalidOperationException("Drive said: file 1AbCexample not found", new HttpRequestException("403"));

        Assert.Equal(
            "CLAUDE discovery failed: System.InvalidOperationException < System.Net.Http.HttpRequestException",
            ClaudeCallLine.Failed("discovery", failure));
    }

    [Fact]
    public void NoKeyHasNoStatus() =>
        Assert.Equal(
            "CLAUDE taste-refine failed: NoKey",
            ClaudeCallLine.Failed("taste-refine", new ClaudeRequestException(ClaudeFailure.NoKey, "There is no Claude key on this phone.")));
}
