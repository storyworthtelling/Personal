namespace Storyworthtelling.Diagnostics.Tests;

// Logging audit (08) and the privacy report: the tap watch named a tapped list row by its first visible
// text, which on Post's Mailbox and Rolodex is a contact name or a message preview and on Index an account
// name or an amount. Those went into tap verdicts and the digest. A row in a list is now named by its id,
// or as a row of its type, and never by what it shows.
public class TapWatchRowTextTests
{
    [Theory]
    [InlineData("Invented Contact Name", "row_title", "LinearLayout", "list row #row_title")]
    [InlineData("A preview of a message", null, "TextView", "list row (TextView)")]
    [InlineData("", null, "LinearLayout", "list row (LinearLayout)")]
    public void ARowInAListIsNeverNamedByItsText(string? text, string? id, string type, string expected)
    {
        var name = TapWatchCore.ControlName(text, id, type, inList: true);

        Assert.Equal(expected, name);
        if (!string.IsNullOrEmpty(text))
        {
            Assert.DoesNotContain(text, name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AControlOutsideAListKeepsItsLabel()
    {
        Assert.Equal("Save #save", TapWatchCore.ControlName("Save", "save", "Button", inList: false));
    }
}
