using Index.Domain.Handoff;
using Xunit;

namespace Index.Tests.Handoff;

/// <summary>
/// Index's end of Plate's statement read (audit W2, 09 M3). Index cannot reference Codex.Domain, so its constants are
/// its own and are held equal to Plate's by reading <c>ReadRequest.cs</c>, <c>PolicyContext.cs</c> and Post's
/// <c>MoneyMail.cs</c> (whose extras Plate's answer uses) by content.
/// </summary>
public class PlateStatementReadTests
{
    [Fact]
    public void TheConstantsAreThePlateContract()
    {
        var request = Read("FocusPhone", "Codex.Domain", "Plate", "ReadRequest.cs");
        var policy = Read("FocusPhone", "Codex.Domain", "Policy", "PolicyContext.cs");
        var money = Read("FocusPhone", "Codex.Domain", "Post", "MoneyMail.cs");

        Assert.Contains($"PlatePackage = \"{PlateStatementRead.PlatePackage}\";", policy, StringComparison.Ordinal);
        Assert.Contains($"ReadStatement = \"{PlateStatementRead.Action}\";", request, StringComparison.Ordinal);
        Assert.Equal(PlateStatementRead.PlatePackage + ".PaperReadActivity", PlateStatementRead.Activity);
        Assert.Contains("PaperActivity = PolicyContext.PlatePackage + \".PaperReadActivity\";", request, StringComparison.Ordinal);
        Assert.Equal(PlateStatementRead.PlatePackage + ".READ_PAPER", PlateStatementRead.Permission);
        Assert.Contains("PaperPermission = PolicyContext.PlatePackage + \".READ_PAPER\";", request, StringComparison.Ordinal);
        Assert.Contains($"BalanceExtra = \"{PlateStatementRead.BalanceExtra}\";", money, StringComparison.Ordinal);
        Assert.Contains($"AsOfExtra = \"{PlateStatementRead.AsOfExtra}\";", money, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnswerReadsAsExactDecimalAndADate()
    {
        var answer = PlateStatementRead.Parse("10500.25", "2026-09-12");

        Assert.NotNull(answer);
        Assert.Equal(10_500.25m, answer!.Balance);
        Assert.Equal(new DateOnly(2026, 9, 12), answer.AsOf);
    }

    [Fact]
    public void AnAnswerWithoutADateIsStillABalance()
    {
        Assert.Null(PlateStatementRead.Parse("12.00", null)!.AsOf);
        Assert.Null(PlateStatementRead.Parse("12.00", "Sept 12")!.AsOf);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("twelve")]
    public void AnAnswerWithNoReadableBalanceIsNothing(string? balance)
    {
        // A guessed figure would be believed; Plate already refuses to send one, and Index refuses to read one.
        Assert.Null(PlateStatementRead.Parse(balance, "2026-09-12"));
    }

    [Fact]
    public void EachWayAReadCanEndHasItsOwnWordsAndACancelSaysNothing()
    {
        Assert.Null(PlateStatementRead.Describe(PlateReadOutcome.Read));
        Assert.Null(PlateStatementRead.Describe(PlateReadOutcome.Cancelled));

        var sentences = new[] { PlateReadOutcome.NotInstalled, PlateReadOutcome.NotPermitted, PlateReadOutcome.Unreadable }
            .Select(PlateStatementRead.Describe)
            .ToList();
        Assert.All(sentences, Assert.NotNull);
        Assert.Equal(3, sentences.Distinct().Count());
        Assert.All(sentences, sentence => Assert.DoesNotMatch(@"\d", sentence!));
    }

    [Fact]
    public void TheHeadStartsTheReadAndAppliesItToTheAccountThatAsked()
    {
        var reader = Read("Ledger", "Ledger.Android", "PlateStatementReader.cs");
        var main = Read("Ledger", "Ledger.Android", "Platforms", "Android", "MainActivity.cs");
        var page = Read("Ledger", "Ledger.Android", "Pages", "MainPage.xaml.cs");

        Assert.Contains("SetClassName(PlateStatementRead.PlatePackage, PlateStatementRead.Activity)", reader, StringComparison.Ordinal);
        Assert.Contains("new global::Android.Content.Intent(PlateStatementRead.Action)", reader, StringComparison.Ordinal);
        Assert.Contains("PlateStatementReader.HandleResult(requestCode, resultCode, data)", main, StringComparison.Ordinal);
        Assert.Contains("ApplyStatementReadAsync(account.Id,", page, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
