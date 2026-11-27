using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// The owner's report (P14 B7): the call note on the phone did not work. Post offers a note once a call has ended, by
/// reading the newest call-log row when the phone goes back to idle; it judged that row "just ended" by how long ago
/// the call log's DATE was, and DATE is when the call began (placed, or began to ring), not when it ended. So every call
/// longer than the fifteen-second window, which is every real conversation, was never offered. The row is now judged by
/// its end: when it began, plus the ring before it connected, plus how long it was connected.
/// </summary>
public class JustEndedCallTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 16, 20, 0, TimeSpan.Zero);

    // CallLog.Calls.TYPE.
    private const int Incoming = 1;
    private const int Outgoing = 2;
    private const int Missed = 3;
    private const int Voicemail = 4;
    private const int Rejected = 5;
    private const int Blocked = 6;
    private const int AnsweredElsewhere = 7;

    private static CallLogRow Row(TimeSpan beganAgo, TimeSpan connected, int type = Incoming) => new(Now - beganAgo, connected, type);

    [Fact]
    public void ASixMinuteCallIsJustEnded()
    {
        // Began six minutes and ten seconds ago, rang for ten seconds, talked for six minutes: it ended just now.
        var row = Row(TimeSpan.FromSeconds(370), TimeSpan.FromMinutes(6));

        Assert.True(JustEndedCall.Fresh(row, Now));
        Assert.Equal(CallNoteVerdict.Offered, JustEndedCall.Judge(row, Now));
    }

    [Fact]
    public void AnHourLongCallIsJustEnded()
    {
        Assert.Equal(CallNoteVerdict.Offered, JustEndedCall.Judge(Row(TimeSpan.FromMinutes(61), TimeSpan.FromMinutes(60)), Now));
    }

    [Fact]
    public void TheRingBeforeItConnectedIsAllowed()
    {
        // An outgoing call can ring for most of a minute before it is answered; the log's duration is the talk alone.
        var row = Row(TimeSpan.FromSeconds(75 + 120), TimeSpan.FromMinutes(2), Outgoing);

        Assert.True(JustEndedCall.Fresh(row, Now));
    }

    [Fact]
    public void ACallThatEndedLongAgoIsNot()
    {
        var row = Row(TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(6));

        Assert.False(JustEndedCall.Fresh(row, Now));
        Assert.Equal(CallNoteVerdict.TooOld, JustEndedCall.Judge(row, Now));
    }

    [Fact]
    public void ACallStillGoingIsNot()
    {
        // A row whose connected time runs past now is not the call that just ended.
        Assert.False(JustEndedCall.Fresh(Row(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3)), Now));
    }

    [Fact]
    public void AShortCallIsStillJustEnded()
    {
        Assert.True(JustEndedCall.Fresh(Row(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(8)), Now));
    }

    [Theory]
    [InlineData(Missed)]
    [InlineData(Voicemail)]
    [InlineData(Rejected)]
    [InlineData(Blocked)]
    [InlineData(99)]
    public void ACallNobodyWasOnIsNotAnswered(int type)
    {
        var row = Row(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(30), type);

        Assert.False(JustEndedCall.Answered(row));
        Assert.Equal(CallNoteVerdict.NotAnswered, JustEndedCall.Judge(row, Now));
    }

    [Fact]
    public void AnOutgoingCallNobodyAnsweredIsNotAnswered()
    {
        // Dialling goes off hook at once, so the end of a call nobody picked up looks like any other.
        Assert.False(JustEndedCall.Answered(Row(TimeSpan.FromSeconds(40), TimeSpan.Zero, Outgoing)));
    }

    [Theory]
    [InlineData(Incoming)]
    [InlineData(Outgoing)]
    public void AConversationIsAnswered(int type)
    {
        Assert.True(JustEndedCall.Answered(Row(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1), type)));
    }

    [Fact]
    public void ACallAnsweredElsewhereIsAnswered()
    {
        // Rung on the phone and taken on the watch: the phone's log may say it lasted nothing at all.
        Assert.True(JustEndedCall.Answered(Row(TimeSpan.FromSeconds(30), TimeSpan.Zero, AnsweredElsewhere)));
    }

    [Fact]
    public void NoRowIsNoCall()
    {
        Assert.Equal(CallNoteVerdict.NoCall, JustEndedCall.Judge(null, Now));
    }

    [Theory]
    [InlineData(CallNoteVerdict.NoCall, true)]
    [InlineData(CallNoteVerdict.TooOld, true)]
    [InlineData(CallNoteVerdict.Offered, false)]
    [InlineData(CallNoteVerdict.NotAnswered, false)]
    [InlineData(CallNoteVerdict.Withheld, false)]
    public void OnlyAMissingRowIsReadAgain(CallNoteVerdict verdict, bool again)
    {
        // The call log is written a moment after the phone goes idle: a row not there yet, or the one before it, is read
        // again; one that says what the call was is the answer.
        Assert.Equal(again, JustEndedCall.ReadAgain(verdict));
    }
}
