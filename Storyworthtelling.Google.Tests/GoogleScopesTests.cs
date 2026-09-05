using Storyworthtelling.Google;

namespace Storyworthtelling.Google.Tests;

public class GoogleScopesTests
{
    [Fact]
    public void ToTokenTypePrefixesOauth2AndJoinsScopes()
    {
        var type = GoogleScopes.ToTokenType(GoogleScopes.Calendar, GoogleScopes.Tasks);
        Assert.StartsWith("oauth2:", type);
        Assert.Contains(GoogleScopes.Calendar, type);
        Assert.Contains(GoogleScopes.Tasks, type);
    }

    [Fact]
    public void ToTokenTypeIsStableRegardlessOfOrderAndDuplicates()
    {
        var a = GoogleScopes.ToTokenType(GoogleScopes.Tasks, GoogleScopes.Calendar, GoogleScopes.Calendar);
        var b = GoogleScopes.ToTokenType(GoogleScopes.Calendar, GoogleScopes.Tasks);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ToTokenTypeRequestsOnlyWhatIsAsked()
    {
        // Per-app minimal scopes: a Drive-only app's token type never carries calendar/tasks.
        var type = GoogleScopes.ToTokenType(GoogleScopes.Drive);
        Assert.DoesNotContain("calendar", type);
        Assert.DoesNotContain("tasks", type);
    }

    [Fact]
    public void ToTokenTypeThrowsWithoutScopes()
    {
        Assert.Throws<ArgumentException>(() => GoogleScopes.ToTokenType());
        Assert.Throws<ArgumentException>(() => GoogleScopes.ToTokenType("  "));
    }
}
