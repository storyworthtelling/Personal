using System.Text.RegularExpressions;
using Watch.Domain;
using Watch.Domain.Dash;
using Watch.Domain.Updates;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// Dash's log lines say the contract and the outcome only, never the payload (plan §2.5, §5: never a message, a number,
/// a name or a token). Every message is run through every line with words, names and numbers in it that must not come out.
/// </summary>
public class DashLogLinesTests
{
    private const string SentinelName = "Juniper Sentinel";
    private const string SentinelWords = "the sentinel words";
    private const string SentinelNumber = "+1 212-555-0199";

    private static readonly DashMessage[] Sentinels =
    [
        new DashPeople([new DashPerson(LeeKey, SentinelName, SentinelWords, T0, true, false)]),
        new DashThread(LeeKey, SentinelName, false, [SentinelName],
            [new DashThreadMessage(TextId, T0, true, DashMessageType.Text, SentinelWords, SentinelName, [])]),
        new DashSend("send-1", null, [SentinelNumber], SentinelWords),
        new DashCallNote(null, SentinelNumber, SentinelWords),
        new DashMail([new DashMailLine(TextId, SentinelName, SentinelWords, T0)]),
    ];

    private static readonly Regex Shape = new(
        @"^(CONTRACT dash/[a-z-]+: (received v\d+|refused v\d+, newer than (Post|Dash) reads|refused, unreadable|sent v\d+|not sent, [A-Za-z][A-Za-z0-9.]*)|DASH dash/[a-z-]+: [A-Za-z]+( \d+)?)$");

    [Fact]
    public void TheLinesSayContractAndOutcomeOnly()
    {
        var lines = new List<string>();
        foreach (var message in Sentinels)
        {
            var read = DashCodec.Decode(message.Path, DashCodec.Encode(message));
            Assert.True(read.Accepted, message.GetType().Name);
            foreach (var end in Enum.GetValues<DashEnd>())
            {
                lines.Add(DashLogLines.Receipt(message.Path, read, end));
            }
            lines.Add(DashLogLines.Sent(message.Path));
            lines.Add(DashLogLines.NotSent(message.Path, SentinelWords));
            foreach (var outcome in Enum.GetValues<DashOutcome>())
            {
                lines.Add(DashLogLines.Outcome(message.Path, outcome, 3));
            }
        }
        lines.AddRange(Enum.GetValues<DashSendOutcome>().Select(DashLogLines.Send));

        Assert.All(lines, line =>
        {
            Assert.DoesNotContain(SentinelName, line, StringComparison.Ordinal);
            Assert.DoesNotContain("Juniper", line, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelWords, line, StringComparison.Ordinal);
            Assert.DoesNotContain("0199", line, StringComparison.Ordinal);
            Assert.DoesNotContain(LeeKey, line, StringComparison.Ordinal);
            Assert.Matches(Shape, line);
        });
    }

    [Fact]
    public void AReceiptSaysTheContractAndVersion()
    {
        var read = ContractRead<DashMessage>.Read(new DashUndo("act-1"), 1);

        Assert.Equal("CONTRACT dash/undo: received v1", DashLogLines.Receipt(WatchIdentity.DashUndoPath, read, DashEnd.Post));
    }

    [Fact]
    public void ANewerOneIsRefusedByVersion()
    {
        var read = ContractRead<DashMessage>.Refused(ContractRefusal.Newer, 2);

        Assert.Equal("CONTRACT dash/people: refused v2, newer than Dash reads", DashLogLines.Receipt(WatchIdentity.DashPeoplePath, read, DashEnd.Dash));
        Assert.Equal("CONTRACT dash/send: refused v2, newer than Post reads", DashLogLines.Receipt(WatchIdentity.DashSendPath, read, DashEnd.Post));
    }

    [Fact]
    public void AnUnreadableOneSaysSo()
    {
        var read = ContractRead<DashMessage>.Refused(ContractRefusal.Unreadable, 0);

        Assert.Equal("CONTRACT dash/call-note: refused, unreadable", DashLogLines.Receipt(WatchIdentity.DashCallNotePath, read, DashEnd.Post));
    }

    [Fact]
    public void ASendingSaysTheContract()
    {
        Assert.Equal("CONTRACT dash/mail: sent v1", DashLogLines.Sent(WatchIdentity.DashMailPath));
        Assert.Equal("CONTRACT dash/mail: not sent, ApiException", DashLogLines.NotSent(WatchIdentity.DashMailPath, "ApiException"));
        Assert.Equal("CONTRACT dash/mail: not sent, Java.IO.IOException", DashLogLines.NotSent(WatchIdentity.DashMailPath, "Java.IO.IOException"));
    }

    [Theory]
    [InlineData("Lee said the dentist moved")]
    [InlineData("+12125550101")]
    [InlineData("")]
    public void AFailureThatIsNoTypeNameIsNotWritten(string failure)
    {
        Assert.Equal("CONTRACT dash/mail: not sent, Exception", DashLogLines.NotSent(WatchIdentity.DashMailPath, failure));
    }

    [Fact]
    public void AnOutcomeIsAWordAndACount()
    {
        Assert.Equal("DASH dash/file: Filed 3", DashLogLines.Outcome(WatchIdentity.DashFilePath, DashOutcome.Filed, 3));
        Assert.Equal("DASH dash/read: Marked", DashLogLines.Outcome(WatchIdentity.DashReadPath, DashOutcome.Marked));
        Assert.Equal("DASH dash/send: NoRoute", DashLogLines.Send(DashSendOutcome.NoRoute));
    }

    [Fact]
    public void AMissedAnswerIsACount()
    {
        // Post's watch-unreachable rule (B2): an answer the wrist did not take is counted, and a message that waited too
        // long behind another is said, each by its contract only.
        Assert.Equal("DASH dash/people: Unreached 3", DashLogLines.Outcome(WatchIdentity.DashPeoplePath, DashOutcome.Unreached, 3));
        Assert.Equal("DASH dash/file: Busy", DashLogLines.Outcome(WatchIdentity.DashFilePath, DashOutcome.Busy));
    }

    [Fact]
    public void TheContractIsThePathWithoutItsSlash()
    {
        Assert.Equal("dash/call-note", DashLogLines.Contract(WatchIdentity.DashCallNotePath));
        Assert.All(WatchIdentity.DashPaths, path => Assert.Equal(path[1..], DashLogLines.Contract(path)));
    }

    [Theory]
    [InlineData("Lee")]
    [InlineData("/update/apk")]
    [InlineData("dash/people")]
    [InlineData("")]
    public void OnlyADashPathIsAContract(string path)
    {
        var read = ContractRead<DashMessage>.Read(new DashPeopleAsk(), 1);

        Assert.Throws<ArgumentException>(() => DashLogLines.Contract(path));
        Assert.Throws<ArgumentException>(() => DashLogLines.Receipt(path, read, DashEnd.Post));
        Assert.Throws<ArgumentException>(() => DashLogLines.Sent(path));
        Assert.Throws<ArgumentException>(() => DashLogLines.NotSent(path, "IOException"));
        Assert.Throws<ArgumentException>(() => DashLogLines.Outcome(path, DashOutcome.Answered));
    }
}
