using Constitution.Domain.Diagnostics;
using Constitution.Domain.Notices;

namespace Constitution.Tests.Diagnostics;

/// <summary>
/// The validated identifiers and counts behind the export. A string reaches the snapshot only as a
/// <see cref="DocumentFile"/>, a <see cref="ConnectionId"/> or an <see cref="AppVersion"/>, each of
/// which accepts a fixed shape and nothing else — and refuses without echoing what it was given,
/// since a refusal is exactly the moment a private string might otherwise be logged.
/// </summary>
public class DiagnosticsModelTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 7, 30, 0, TimeSpan.Zero);

    // ---- DocumentFile -------------------------------------------------------------------------

    [Fact]
    public void ADocumentFileIsBuiltFromAKindAndADate()
    {
        Assert.Equal("workout-2026-11-17.pdf", DocumentFile.For(DocumentKind.Workout, new DateOnly(2026, 11, 17)).Value);
        Assert.Equal("meals-2026-11-15.pdf", DocumentFile.For(DocumentKind.Meals, new DateOnly(2026, 11, 15)).Value);
        Assert.Equal("digest-2026-01-02.pdf", DocumentFile.For(DocumentKind.Digest, new DateOnly(2026, 1, 2)).Value);
    }

    [Fact]
    public void ADocumentFileParsesWhatItBuilt()
    {
        var built = DocumentFile.For(DocumentKind.Workout, new DateOnly(2026, 11, 17));

        Assert.Equal(built, DocumentFile.Parse(built.Value));
    }

    public static IEnumerable<object[]> NotDocumentFiles()
    {
        yield return ["Lentil soup.pdf"];
        yield return ["workout-2026-11-17.pdf.exe"];
        yield return ["workout-Sam-2026-11-17.pdf"];
        yield return ["workout-2026-13-45.pdf"];
        yield return ["workout-1987-03-09.docx"];
        yield return ["../workout-2026-11-17.pdf"];
        yield return ["workout-2026-11-17.pdf\nSam"];
        yield return [""];
        yield return ["   "];
    }

    [Theory]
    [MemberData(nameof(NotDocumentFiles))]
    public void AFreeTextDocumentNameIsRefusedWithoutEchoingIt(string text)
    {
        var failure = Assert.Throws<ArgumentException>(() => DocumentFile.Parse(text));

        if (text.Trim().Length > 0)
        {
            Assert.DoesNotContain(text.Trim(), failure.Message);
        }
    }

    [Fact]
    public void ANullDocumentNameIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => DocumentFile.Parse(null!));
    }

    // ---- ConnectionId -------------------------------------------------------------------------

    [Fact]
    public void TheConnectionIdsAreTheFiveLiterals()
    {
        Assert.Equal(["sleep-number", "remarkable", "claude", "gemini", "google"], ConnectionIds.All);
        Assert.Equal("sleep-number", ConnectionIds.SleepNumber);
        Assert.Equal("remarkable", ConnectionIds.ReMarkable);
        Assert.Equal("claude", ConnectionIds.Claude);
        Assert.Equal("gemini", ConnectionIds.Gemini);
        Assert.Equal("google", ConnectionIds.Google);
    }

    [Fact]
    public void EveryKnownConnectionIdParses()
    {
        foreach (var id in ConnectionIds.All)
        {
            Assert.Equal(id, ConnectionId.Parse(id).Value);
        }
    }

    [Theory]
    [InlineData("sam")]
    [InlineData("Claude")]
    [InlineData("claude ")]
    [InlineData("tidal")]
    [InlineData("")]
    public void AnUnknownConnectionIdIsRefusedWithoutEchoingIt(string text)
    {
        var failure = Assert.Throws<ArgumentException>(() => ConnectionId.Parse(text));

        if (text.Trim().Length > 0)
        {
            Assert.DoesNotContain(text.Trim(), failure.Message);
        }
    }

    [Fact]
    public void TheSameConnectionTwiceIsRefused()
    {
        var id = ConnectionId.Parse(ConnectionIds.Claude);

        Assert.Throws<ArgumentException>(() => new DiagnosticsSnapshot(
            AppVersion.Parse("1.0.1"), InstallRole.Owner, new DiagnosticsCounts(0, 0, 0, []), [], [],
            [new CredentialStatus(id, CredentialState.Working), new CredentialStatus(id, CredentialState.Missing)]));
    }

    // ---- AppVersion ---------------------------------------------------------------------------

    [Theory]
    [InlineData("1.0.57")]
    [InlineData("1.0.512")]
    [InlineData("2.10.9999")]
    // The CI display version is 1.0.<run number>, which will pass 1900 and 99999. A date of birth cannot
    // take that shape (no month 0), so it is carried whatever the number.
    [InlineData("1.0.1987")]
    [InlineData("1.0.2026")]
    [InlineData("1.0.2100")]
    [InlineData("1.0.12345")]
    [InlineData("1.0.123456")]
    [InlineData("1.0.999999999")]
    public void ABuildVersionParses(string text)
    {
        Assert.Equal(text, AppVersion.Parse(text).Value);
    }

    [Theory]
    [InlineData("81.3 kg")]
    [InlineData("1.0")]
    [InlineData("1.0.57-beta")]
    [InlineData("9.3.1987")]
    [InlineData("1.1.1987")]
    [InlineData("12.3.2026")]
    [InlineData("1.0.1234567890")]
    [InlineData("1.0.")]
    [InlineData("1.0.-5")]
    [InlineData("")]
    public void ANonVersionOrAYearShapedOneIsRefusedWithoutEchoingIt(string text)
    {
        var failure = Assert.Throws<ArgumentException>(() => AppVersion.Parse(text));

        if (text.Trim().Length > 0)
        {
            Assert.DoesNotContain(text.Trim(), failure.Message);
        }
    }

    // ---- counts -------------------------------------------------------------------------------

    [Fact]
    public void ACountIsBounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticsCounts(-1, 0, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticsCounts(0, 1000, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticsCounts(0, 0, 1000, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NoticeCount(NoticeKind.BedOffline, 1000));

        var top = new DiagnosticsCounts(999, 999, 999, [new NoticeCount(NoticeKind.BedOffline, 999)]);
        Assert.Equal(999, top.WorkoutsPlanned);
    }

    [Fact]
    public void TallyingNoticesKeepsOnlyTheKindAndTheCount()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.LowConfidenceRead, "cause-one", Start);
        board.Raise(NoticeKind.LowConfidenceRead, "cause-two", Start.AddMinutes(1));
        board.Raise(NoticeKind.BedOffline, "cause-three", Start.AddMinutes(2));

        var tally = NoticeCount.Tally(board.Waiting());

        Assert.Equal(2, tally.Count);
        Assert.Equal(2, tally.Single(entry => entry.Kind == NoticeKind.LowConfidenceRead).Count);
        Assert.Equal(1, tally.Single(entry => entry.Kind == NoticeKind.BedOffline).Count);
        Assert.Equal(
            tally.Select(entry => entry.Kind).OrderBy(kind => (int)kind),
            tally.Select(entry => entry.Kind));
    }

    [Fact]
    public void AnEmptyBoardTalliesToNothing()
    {
        Assert.Empty(NoticeCount.Tally(new NoticeBoard().Waiting()));
    }

    // ---- the fixed phrases --------------------------------------------------------------------

    [Fact]
    public void EveryClosedValueHasAPhrase()
    {
        foreach (var outcome in Enum.GetValues<DiagnosticsOutcome>())
        {
            Assert.False(string.IsNullOrWhiteSpace(DiagnosticsPhrases.Of(outcome)));
        }
        foreach (var component in Enum.GetValues<DiagnosticsComponent>())
        {
            Assert.False(string.IsNullOrWhiteSpace(DiagnosticsPhrases.Of(component)));
        }
        foreach (var target in Enum.GetValues<DeliveryTarget>())
        {
            Assert.False(string.IsNullOrWhiteSpace(DiagnosticsPhrases.Of(target)));
        }
        foreach (var role in Enum.GetValues<InstallRole>())
        {
            Assert.False(string.IsNullOrWhiteSpace(DiagnosticsPhrases.Of(role)));
        }
        foreach (var state in Enum.GetValues<CredentialState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(DiagnosticsPhrases.Of(state)));
        }
    }

    [Fact]
    public void AnUndefinedEnumValueHasNoPhraseRatherThanItsNumber()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticsPhrases.Of((DiagnosticsOutcome)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticsPhrases.Of((DiagnosticsComponent)99));
    }

    [Fact]
    public void TheRenderingIsDeterministic()
    {
        Assert.Equal(
            DiagnosticsRenderer.Render(DiagnosticsPrivacyTests.SeededSnapshot()),
            DiagnosticsRenderer.Render(DiagnosticsPrivacyTests.SeededSnapshot()));
    }
}
