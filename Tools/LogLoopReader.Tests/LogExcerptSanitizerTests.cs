namespace LogLoopReader.Tests;

// §6.1 layer 3: the only path from a log line to a PR or an issue. Templates, exception types and control
// ids survive; numbers, addresses, phones, amounts, URLs (every OAuth code, state and public_token with
// them) and names do not. A capitalised word survives only if the repository already uses it.
public class LogExcerptSanitizerTests
{
    private static readonly LogExcerptSanitizer Sanitizer = new(new HashSet<string>(StringComparer.Ordinal)
    {
        "Sync", "Mailbox", "Send", "Save", "Settings", "The", "Owed", "Sent", "Filing", "Returned",
    });

    [Fact]
    public void TemplatesExceptionTypesAndControlIdsSurvive()
    {
        Assert.Equal(
            "DEAD TAP — screen Settings · control Save #save_button",
            Sanitizer.Sanitize("DEAD TAP — screen Settings · control Save #save_button"));
        Assert.Equal(
            "System.InvalidOperationException at Post.MailboxArea.LoadOpenThread",
            Sanitizer.Sanitize("System.InvalidOperationException at Post.MailboxArea.LoadOpenThread"));
    }

    [Fact]
    public void RunsOfThreeOrMoreDigitsBecomeNumbers()
    {
        Assert.Equal("Owed delete of <n> failed after 2 tries", Sanitizer.Sanitize("Owed delete of 4471 failed after 2 tries"));
    }

    [Fact]
    public void EmailsBecomeEmailEvenAtExampleDotCom()
    {
        Assert.Equal("Sent to <email>", Sanitizer.Sanitize("Sent to ada.lovelace@example.com"));
        Assert.Equal("Sent to <email>", Sanitizer.Sanitize("Sent to someone@mail.example.org"));
    }

    [Fact]
    public void PhoneShapesBecomePhone()
    {
        Assert.Equal("Sent to <phone>", Sanitizer.Sanitize("Sent to (555) 010-0142"));
        Assert.Equal("Sent to <phone>", Sanitizer.Sanitize("Sent to +1 555-010-0142"));
        Assert.Equal("Sent to <phone>", Sanitizer.Sanitize("Sent to 555.010.0142"));
    }

    [Fact]
    public void AmountsBecomeAmount()
    {
        Assert.Equal("The balance <amount> arrived", Sanitizer.Sanitize("The balance $1,204.57 arrived"));
        Assert.Equal("The balance <amount> arrived", Sanitizer.Sanitize("The balance 12.50 USD arrived"));
    }

    [Fact]
    public void UrlsGoWholeWithEveryOauthParameter()
    {
        var sanitized = Sanitizer.Sanitize(
            "Returned: com.example.app://tidal-auth?code=Zx9AbcDef&state=st8x1 then https://bank.example.com/link?public_token=public-sandbox-1234");

        Assert.Equal("Returned: <url> then <url>", sanitized);
    }

    [Fact]
    public void BareOauthParametersOutsideAUrlAreRedactedToo()
    {
        Assert.Equal("code=<redacted> state=<redacted> public_token=<redacted>",
            Sanitizer.Sanitize("code=Zx9AbcDef state=st8x1 public_token=public-sandbox-abc"));
    }

    [Fact]
    public void CapitalisedWordsTheRepoDoesNotUseAreNames()
    {
        Assert.Equal("Sync failed for <Name> <Name>", Sanitizer.Sanitize("Sync failed for Juniper Halloway"));
        Assert.Equal("<Name> replied", Sanitizer.Sanitize("Robin replied"));
    }

    [Fact]
    public void AllCapsWordsAndLowercaseWordsStay()
    {
        Assert.Equal("MMS DOWNLOAD FAILED: NO_DATA_NETWORK (8)", Sanitizer.Sanitize("MMS DOWNLOAD FAILED: NO_DATA_NETWORK (8)"));
        Assert.Equal("the request was refused", Sanitizer.Sanitize("the request was refused"));
    }

    [Fact]
    public void TheVocabularyCanBeReadFromTheRepository()
    {
        var vocabulary = LogExcerptSanitizer.VocabularyFrom(Support.RepoRoot());

        Assert.Contains("Mailbox", vocabulary);
        Assert.Contains("DiagnosticLog", vocabulary);
        Assert.DoesNotContain("Halloway", vocabulary);
    }
}
