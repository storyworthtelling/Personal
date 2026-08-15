using Storyworthtelling.Google;

namespace Storyworthtelling.Google.Tests;

public class GoogleAccountBrokerTests
{
    [Fact]
    public void ExposesTheChosenAccountEmail()
    {
        var broker = new GoogleAccountBroker("owner@example.com", (_, _) => Task.FromResult("t"), "App");
        Assert.Equal("owner@example.com", broker.AccountEmail);
    }

    [Fact]
    public async Task GetAccessTokenRequestsTheTokenTypeForTheAskedScopes()
    {
        string? requestedType = null;
        var broker = new GoogleAccountBroker(
            "owner@example.com",
            (type, _) => { requestedType = type; return Task.FromResult("access-token"); },
            "App");

        var token = await broker.GetAccessTokenAsync(GoogleScopes.Drive);

        Assert.Equal("access-token", token);
        Assert.Equal(GoogleScopes.ToTokenType(GoogleScopes.Drive), requestedType);
        // Minimal scopes: a Drive request never widens to calendar/tasks.
        Assert.DoesNotContain("calendar", requestedType);
    }

    [Fact]
    public async Task CreatesADriveServiceFromTheBrokeredToken()
    {
        var broker = new GoogleAccountBroker("owner@example.com", (_, _) => Task.FromResult("access-token"), "App");
        using var service = await broker.CreateDriveServiceAsync(GoogleScopes.Drive);
        Assert.NotNull(service);
        Assert.Equal("App", service.ApplicationName);
    }
}
