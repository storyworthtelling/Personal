using Index.Domain.Health;
using Xunit;

namespace Index.Tests.Health;

public class PlaidFailureTests
{
    [Theory]
    [InlineData("INVALID_API_KEYS")]
    [InlineData("INVALID_CLIENT_ID")]
    [InlineData("INVALID_SECRET")]
    [InlineData("INVALID_CREDENTIALS")]
    [InlineData("UNAUTHORIZED")]
    public void CredentialCodes_AreCredentialRejections(string code)
    {
        Assert.True(PlaidFailure.IsCredentialRejection(code));
    }

    [Theory]
    [InlineData("ITEM_LOGIN_REQUIRED")]
    [InlineData("INVALID_ACCESS_TOKEN")]
    [InlineData("RATE_LIMIT_EXCEEDED")]
    [InlineData("PRODUCT_NOT_READY")]
    [InlineData("INTERNAL_SERVER_ERROR")]
    public void ItemAndTransientCodes_AreNotCredentialRejections(string code)
    {
        // A bank needing re-login or a transient error must not flag the shared Plaid credentials.
        Assert.False(PlaidFailure.IsCredentialRejection(code));
    }

    [Fact]
    public void NullCode_IsNotACredentialRejection()
    {
        Assert.False(PlaidFailure.IsCredentialRejection(null));
    }

    [Fact]
    public void Matching_IsCaseInsensitive()
    {
        Assert.True(PlaidFailure.IsCredentialRejection("invalid_api_keys"));
    }
}
