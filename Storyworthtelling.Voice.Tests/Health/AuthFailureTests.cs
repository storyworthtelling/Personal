using Storyworthtelling.Voice.Health;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Health;

public class AuthFailureTests
{
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void Unauthorized_And_Forbidden_AreCredentialRejections(int status)
    {
        Assert.True(AuthFailure.IsHttpCredentialRejection(status, "whatever"));
    }

    [Fact]
    public void Gemini_InvalidKey_400_IsACredentialRejection()
    {
        var body = "{\"error\":{\"code\":400,\"status\":\"INVALID_ARGUMENT\",\"message\":\"API key not valid. API_KEY_INVALID\"}}";
        Assert.True(AuthFailure.IsHttpCredentialRejection(400, body));
    }

    [Fact]
    public void Plain400_IsNotACredentialRejection()
    {
        Assert.False(AuthFailure.IsHttpCredentialRejection(400, "bad request, missing field"));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void Throttling_And_ServerErrors_AreNotCredentialRejections(int status)
    {
        Assert.False(AuthFailure.IsHttpCredentialRejection(status, "overloaded"));
    }

    [Fact]
    public void NullBody_OnPlain400_IsHandled()
    {
        Assert.False(AuthFailure.IsHttpCredentialRejection(400, null));
    }
}
