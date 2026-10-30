using Codex.Domain.Design;
using Constitution.Domain.Notices;

namespace Constitution.Tests.Notices;

/// <summary>
/// C-9.1.1 — the notice model (book p.18; D77, D83, D90, D91; DESIGN.md §13). One fold, wearing the
/// colour of the most serious thing waiting; an undo outranks every notice for its window and hands
/// the corner back; a notice goes when its cause does.
///
/// <para>The expected role table is typed out here on purpose: the guard exists to notice a kind
/// quietly changing colour, so it carries its own record of what each one was. The ordering itself is
/// not restated — it is <c>NoticeSeriousness</c>'s, the one Post and the kit already share.</para>
/// </summary>
public class ConstitutionNoticeTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 14, 7, 30, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int minutes) => Start.AddMinutes(minutes);

    private static readonly (NoticeKind Kind, FoldRole Role)[] Expected =
    [
        (NoticeKind.ReMarkableDelivery, FoldRole.Problem),
        (NoticeKind.HealthConnectRevoked, FoldRole.Problem),
        (NoticeKind.BedOffline, FoldRole.Problem),
        (NoticeKind.BedSilent, FoldRole.Reference),
        (NoticeKind.LowConfidenceRead, FoldRole.Caution),
        (NoticeKind.ScanToCheck, FoldRole.Progress),
        (NoticeKind.BoxUnderTen, FoldRole.Reference),
        (NoticeKind.KeepHandoff, FoldRole.Reference),
        (NoticeKind.NoPenMarks, FoldRole.Caution),
        (NoticeKind.WeekNotPlanned, FoldRole.Problem),
        (NoticeKind.HouseholdFileUnreadable, FoldRole.Problem),
    ];

    // ---- EachKindHasItsRole -------------------------------------------------------------------

    [Fact]
    public void EachKindHasItsRole()
    {
        Assert.NotEmpty(Expected);
        foreach (var (kind, role) in Expected)
        {
            Assert.Equal(role, NoticeKinds.RoleOf(kind));
        }
    }

    [Fact]
    public void EveryKindIsCoveredByTheTableAndTheMapping()
    {
        var kinds = Enum.GetValues<NoticeKind>();
        Assert.NotEmpty(kinds);
        Assert.Equal(kinds.Length, NoticeKinds.All.Count);
        Assert.Equal(kinds.Order(), NoticeKinds.All.Order());
        Assert.Equal(kinds.Order(), Expected.Select(row => row.Kind).Order());
    }

    [Fact]
    public void NoKindIsResolvedOrAction()
    {
        // Resolved is settled, and Action pine belongs to the undo alone (D77): a waiting notice
        // wearing either would read as a decision or as nothing at all.
        Assert.NotEmpty(NoticeKinds.All);
        foreach (var kind in NoticeKinds.All)
        {
            Assert.NotEqual(FoldRole.Resolved, NoticeKinds.RoleOf(kind));
            Assert.NotEqual(FoldRole.Action, NoticeKinds.RoleOf(kind));
        }
    }

    [Fact]
    public void AnUnknownKindIsRefusedRatherThanGivenARole()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NoticeKinds.RoleOf((NoticeKind)(-1)));
    }

    [Fact]
    public void TheUndoIsPineTheActionRole()
    {
        Assert.Equal(FoldRole.Action, NoticeBoard.UndoRole);
    }

    [Fact]
    public void TheUndoWindowIsTheDesignTokensSixSeconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(DesignTokens.Motion.UndoWindowMs), NoticeBoard.UndoWindow);
        Assert.Equal(TimeSpan.FromSeconds(6), NoticeBoard.UndoWindow);
    }

    // ---- MostSeriousWins ----------------------------------------------------------------------

    [Fact]
    public void AFlatCornerWhenNothingIsWaiting()
    {
        Assert.Null(new NoticeBoard().Fold(At(0)));
    }

    [Fact]
    public void ALoneNoticeWearsItsRole()
    {
        foreach (var (kind, role) in Expected)
        {
            var board = new NoticeBoard();
            board.Raise(kind, "cause", At(0));

            var fold = board.Fold(At(1));

            Assert.NotNull(fold);
            Assert.Equal(role, fold!.Role);
            Assert.False(fold.IsUndo);
            Assert.Null(fold.Expires);
            Assert.Equal(kind, fold.Kind);
        }
    }

    [Fact]
    public void MostSeriousWins()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BoxUnderTen, "box", At(0));
        board.Raise(NoticeKind.BedOffline, "bed", At(1));
        board.Raise(NoticeKind.KeepHandoff, "keep", At(2));
        board.Raise(NoticeKind.ScanToCheck, "scan", At(3));

        // The bed was raised second, not last: seriousness decides, not the newest.
        Assert.Equal(FoldRole.Problem, board.Fold(At(4))!.Role);
        Assert.Equal(NoticeKind.BedOffline, board.Fold(At(4))!.Kind);
    }

    [Fact]
    public void CautionBeatsProgressBeatsReference()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.KeepHandoff, "keep", At(0));
        Assert.Equal(FoldRole.Reference, board.Fold(At(1))!.Role);

        board.Raise(NoticeKind.ScanToCheck, "scan", At(1));
        Assert.Equal(FoldRole.Progress, board.Fold(At(2))!.Role);

        board.Raise(NoticeKind.LowConfidenceRead, "read", At(2));
        Assert.Equal(FoldRole.Caution, board.Fold(At(3))!.Role);
    }

    [Fact]
    public void ATieGoesToTheNewest()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.ReMarkableDelivery, "remarkable", At(0));
        board.Raise(NoticeKind.BedOffline, "bed", At(5));
        board.Raise(NoticeKind.HealthConnectRevoked, "health", At(2));

        Assert.Equal(NoticeKind.BedOffline, board.Fold(At(6))!.Kind);
    }

    [Fact]
    public void TheFoldAgreesWithNoticeSeriousnessForEveryPair()
    {
        // The ordering is reused, not restated: whichever of two kinds NoticeSeriousness ranks higher
        // is the one the fold wears, whichever was raised first.
        Assert.NotEmpty(NoticeKinds.All);
        foreach (var first in NoticeKinds.All)
        {
            foreach (var second in NoticeKinds.All)
            {
                var board = new NoticeBoard();
                board.Raise(first, "first", At(0));
                board.Raise(second, "second", At(1));

                var worn = board.Fold(At(2))!.Role;
                var best = new[] { NoticeKinds.RoleOf(first), NoticeKinds.RoleOf(second) }
                    .MinBy(role => NoticeSeriousness.Rank(role));
                Assert.Equal(best, worn);
            }
        }
    }

    [Fact]
    public void WaitingListsNewestFirstWhateverTheSeriousness()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BedOffline, "bed", At(0));
        board.Raise(NoticeKind.BoxUnderTen, "box", At(10));
        board.Raise(NoticeKind.LowConfidenceRead, "read", At(5));

        Assert.Equal(["box", "read", "bed"], board.Waiting().Select(notice => notice.CauseKey));
    }

    [Fact]
    public void NoticesRaisedAtTheSameMomentListLatestRaisedFirst()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BedOffline, "first", At(0));
        board.Raise(NoticeKind.BoxUnderTen, "second", At(0));

        Assert.Equal(["second", "first"], board.Waiting().Select(notice => notice.CauseKey));
    }

    [Fact]
    public void ANoticeKeepsItsKindCauseAndTime()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.KeepHandoff, "groceries-week-11", At(3));

        var notice = Assert.Single(board.Waiting());
        Assert.Equal(NoticeKind.KeepHandoff, notice.Kind);
        Assert.Equal("groceries-week-11", notice.CauseKey);
        Assert.Equal(At(3), notice.CreatedAt);
        Assert.Equal(FoldRole.Reference, notice.Role);
    }

    // ---- Raise is idempotent per cause --------------------------------------------------------

    [Fact]
    public void RaisingTheSameCauseAgainChangesNothing()
    {
        var board = new NoticeBoard();
        Assert.True(board.Raise(NoticeKind.BedOffline, "bed", At(0)));
        Assert.False(board.Raise(NoticeKind.BedOffline, "bed", At(30)));

        var notice = Assert.Single(board.Waiting());
        Assert.Equal(At(0), notice.CreatedAt);
    }

    [Fact]
    public void ARepeatedRaiseDoesNotReshuffleTheList()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BedOffline, "bed", At(0));
        board.Raise(NoticeKind.BoxUnderTen, "box", At(5));
        board.Raise(NoticeKind.BedOffline, "bed", At(9));

        Assert.Equal(["box", "bed"], board.Waiting().Select(notice => notice.CauseKey));
    }

    [Fact]
    public void ACauseThatChangesKindIsANewNotice()
    {
        // A read that was a Caution and is now a broken connection under the same cause: the old
        // judgement must not linger, and the new one is dated from when it turned.
        var board = new NoticeBoard();
        board.Raise(NoticeKind.LowConfidenceRead, "scan-7", At(0));
        Assert.True(board.Raise(NoticeKind.ReMarkableDelivery, "scan-7", At(20)));

        var notice = Assert.Single(board.Waiting());
        Assert.Equal(NoticeKind.ReMarkableDelivery, notice.Kind);
        Assert.Equal(At(20), notice.CreatedAt);
        Assert.Equal(FoldRole.Problem, board.Fold(At(21))!.Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ACauseNeedsAKey(string key)
    {
        Assert.Throws<ArgumentException>(() => new NoticeBoard().Raise(NoticeKind.BedOffline, key, At(0)));
        Assert.Throws<ArgumentException>(() => new NoticeBoard().Resolve(key));
    }

    [Fact]
    public void ANullCauseKeyIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new NoticeBoard().Raise(NoticeKind.BedOffline, null!, At(0)));
    }

    // ---- UndoOutranks -------------------------------------------------------------------------

    [Fact]
    public void UndoOutranks()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.ReMarkableDelivery, "remarkable", At(0));

        board.BeginUndo("skip-tuesday", At(1));
        var fold = board.Fold(At(1).AddSeconds(2));

        Assert.NotNull(fold);
        Assert.True(fold!.IsUndo);
        Assert.Equal(FoldRole.Action, fold.Role);
        Assert.Equal(At(1) + NoticeBoard.UndoWindow, fold.Expires);
        Assert.Null(fold.Kind);
    }

    [Fact]
    public void AnUndoOutranksEvenTheMostSeriousNotice()
    {
        Assert.NotEmpty(NoticeKinds.All);
        foreach (var kind in NoticeKinds.All)
        {
            var board = new NoticeBoard();
            board.Raise(kind, "cause", At(0));
            board.BeginUndo("act", At(1));

            Assert.Equal(FoldRole.Action, board.Fold(At(1))!.Role);
        }
    }

    [Fact]
    public void AnUndoTakesAnOtherwiseFlatCorner()
    {
        var board = new NoticeBoard();
        board.BeginUndo("act", At(0));

        var fold = board.Fold(At(0).AddSeconds(5));
        Assert.True(fold!.IsUndo);
    }

    [Fact]
    public void TheCornerIsHandedBackWhenTheWindowCloses()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.LowConfidenceRead, "read", At(0));
        board.BeginUndo("act", At(1));

        var justBefore = At(1) + NoticeBoard.UndoWindow - TimeSpan.FromMilliseconds(1);
        var atClose = At(1) + NoticeBoard.UndoWindow;

        Assert.True(board.Fold(justBefore)!.IsUndo);

        var handedBack = board.Fold(atClose);
        Assert.False(handedBack!.IsUndo);
        Assert.Equal(FoldRole.Caution, handedBack.Role);
        Assert.Equal(NoticeKind.LowConfidenceRead, handedBack.Kind);
    }

    [Fact]
    public void AClosedUndoLeavesAFlatCornerWhenNothingWaits()
    {
        var board = new NoticeBoard();
        board.BeginUndo("act", At(0));

        Assert.Null(board.Fold(At(0) + NoticeBoard.UndoWindow));
    }

    [Fact]
    public void NoticesAreNotDiscardedWhileTheUndoHoldsTheCorner()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BedOffline, "bed", At(0));
        board.BeginUndo("act", At(1));

        Assert.Single(board.Waiting());
    }

    [Fact]
    public void ANoticeRaisedDuringTheWindowWaitsBehindIt()
    {
        var board = new NoticeBoard();
        board.BeginUndo("act", At(0));
        board.Raise(NoticeKind.BedOffline, "bed", At(0).AddSeconds(2));

        Assert.True(board.Fold(At(0).AddSeconds(3))!.IsUndo);
        Assert.Equal(NoticeKind.BedOffline, board.Fold(At(0) + NoticeBoard.UndoWindow)!.Kind);
    }

    [Fact]
    public void TurningTheFoldInTimeReturnsTheActToReverse()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BoxUnderTen, "box", At(0));
        board.BeginUndo("skip-tuesday", At(1));

        var reversed = board.TurnUndo(At(1).AddSeconds(3));

        Assert.Equal("skip-tuesday", reversed);
        Assert.False(board.Fold(At(1).AddSeconds(3))!.IsUndo);
        Assert.Equal(NoticeKind.BoxUnderTen, board.Fold(At(1).AddSeconds(3))!.Kind);
    }

    [Fact]
    public void TurningAfterTheWindowReversesNothing()
    {
        var board = new NoticeBoard();
        board.BeginUndo("act", At(0));

        Assert.Null(board.TurnUndo(At(0) + NoticeBoard.UndoWindow));
    }

    [Fact]
    public void TurningWithNoUndoReversesNothing()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BedOffline, "bed", At(0));

        Assert.Null(board.TurnUndo(At(1)));
        Assert.Equal(NoticeKind.BedOffline, board.Fold(At(1))!.Kind);
    }

    [Fact]
    public void ATurnedUndoCannotBeTurnedTwice()
    {
        var board = new NoticeBoard();
        board.BeginUndo("act", At(0));

        Assert.Equal("act", board.TurnUndo(At(0).AddSeconds(1)));
        Assert.Null(board.TurnUndo(At(0).AddSeconds(2)));
    }

    [Fact]
    public void ANewActReplacesAFoldStillShowingAndTheOldOneBecomesPermanent()
    {
        var board = new NoticeBoard();
        board.BeginUndo("first", At(0));
        board.BeginUndo("second", At(0).AddSeconds(2));

        Assert.Equal("second", board.TurnUndo(At(0).AddSeconds(3)));
        Assert.Null(board.TurnUndo(At(0).AddSeconds(4)));
    }

    [Fact]
    public void TheNewActsWindowRunsFromItsOwnStart()
    {
        var board = new NoticeBoard();
        board.BeginUndo("first", At(0));
        var secondStart = At(0).AddSeconds(4);
        board.BeginUndo("second", secondStart);

        Assert.Equal(secondStart + NoticeBoard.UndoWindow, board.Fold(secondStart.AddSeconds(5))!.Expires);
        Assert.True(board.Fold(secondStart.AddSeconds(5))!.IsUndo);
    }

    // ---- GoesWithItsCause ---------------------------------------------------------------------

    [Fact]
    public void GoesWithItsCause()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.ReMarkableDelivery, "remarkable", At(0));
        Assert.NotNull(board.Fold(At(1)));

        Assert.True(board.Resolve("remarkable"));

        Assert.Empty(board.Waiting());
        Assert.Null(board.Fold(At(2)));
    }

    [Fact]
    public void ResolvingTheMostSeriousHandsTheFoldToTheNext()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BoxUnderTen, "box", At(0));
        board.Raise(NoticeKind.LowConfidenceRead, "read", At(1));
        board.Raise(NoticeKind.BedOffline, "bed", At(2));

        board.Resolve("bed");
        Assert.Equal(FoldRole.Caution, board.Fold(At(3))!.Role);

        board.Resolve("read");
        Assert.Equal(FoldRole.Reference, board.Fold(At(4))!.Role);

        board.Resolve("box");
        Assert.Null(board.Fold(At(5)));
    }

    [Fact]
    public void ResolvingOnlyRemovesThatCause()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BedOffline, "bed", At(0));
        board.Raise(NoticeKind.HealthConnectRevoked, "health", At(1));

        board.Resolve("bed");

        Assert.Equal(["health"], board.Waiting().Select(notice => notice.CauseKey));
    }

    [Fact]
    public void ResolvingACauseThatIsNotWaitingIsHarmless()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BedOffline, "bed", At(0));

        Assert.False(board.Resolve("nothing-here"));
        Assert.Single(board.Waiting());
    }

    [Fact]
    public void AResolvedCauseCanComeBackAsAFreshNotice()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BedOffline, "bed", At(0));
        board.Resolve("bed");

        Assert.True(board.Raise(NoticeKind.BedOffline, "bed", At(90)));

        Assert.Equal(At(90), Assert.Single(board.Waiting()).CreatedAt);
    }

    [Fact]
    public void ResolvingDuringAnUndoLeavesNothingBehindWhenItCloses()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.LowConfidenceRead, "read", At(0));
        board.BeginUndo("act", At(1));

        board.Resolve("read");

        Assert.True(board.Fold(At(1).AddSeconds(1))!.IsUndo);
        Assert.Null(board.Fold(At(1) + NoticeBoard.UndoWindow));
    }

    [Fact]
    public void ResolvingDoesNotCancelAnUndo()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BoxUnderTen, "box", At(0));
        board.BeginUndo("act", At(1));
        board.Resolve("box");

        Assert.Equal("act", board.TurnUndo(At(1).AddSeconds(1)));
    }
}
