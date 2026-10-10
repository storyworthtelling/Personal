using Pulp.Domain.Voice;

namespace Pulp.Tests.Voice;

/// <summary>
/// G12 — what Pulp is willing to say in the owner's voice, and the rule that it never says trouble in
/// it (DESIGN.md §14, D38).
///
/// <para>The bugs these reintroduce: a seat added off the list (a new voiced surface arriving as a
/// diff rather than a decision), and an error, a notice or the dog-ear voiced — the copy that feels
/// most improved by sounding human, which is why the list is enforced rather than remembered.</para>
/// </summary>
public class VoicedOccasionTests
{
    /// <summary>
    /// The closed list, written out with the decision behind each seat. Adding a seat means adding it
    /// here too, with its D-number; that is the point.
    /// </summary>
    private static readonly Dictionary<VoiceSeat, string> Approved = new()
    {
        [VoiceSeat.Greeting] = "D38, D118",
        [VoiceSeat.WhyThis] = "D36, D38, D41",
        [VoiceSeat.PlaylistLine] = "D38",
        [VoiceSeat.ChildGreeting] = "D39, D233",
        [VoiceSeat.ChildFavorites] = "D232, D233",
        [VoiceSeat.EmptyContinueWatching] = "D264",
        [VoiceSeat.EmptyPlaylists] = "D264",
        [VoiceSeat.EmptyPlaylist] = "D264",
        [VoiceSeat.EmptyRecentlyWatched] = "D264",
        [VoiceSeat.EmptyChildFavorites] = "D264, D233",
        [VoiceSeat.EmptyChildWatching] = "D294, D233",
        [VoiceSeat.EmptyChildPlaylists] = "D294, D233",
        [VoiceSeat.EmptyCalendar] = "D264",
        [VoiceSeat.EmptySports] = "D264",
        [VoiceSeat.StackUsedUp] = "D112, D264",
    };

    private static readonly string[] Trouble =
    [
        "error", "fail", "problem", "sorry", "unable", "denied", "invalid", "expired", "crash", "refus",
        "warn", "notice", "dogear", "dog-ear", "fold", "undo", "offline", "retry", "wrong", "broken",
        "rejected", "missing",
    ];

    [Fact]
    public void TheSeatListIsClosed()
    {
        var declared = Enum.GetValues<VoiceSeat>();
        Assert.NotEmpty(declared);

        var unapproved = declared.Where(seat => !Approved.ContainsKey(seat)).ToList();
        Assert.True(unapproved.Count == 0,
            "These seats are not on Pulp's approved list (D38): " + string.Join(", ", unapproved)
            + ". A voiced seat is a decision — add it to the approved list here with its D-number.");

        var retired = Approved.Keys.Where(seat => !declared.Contains(seat)).ToList();
        Assert.Empty(retired);
    }

    [Fact]
    public void EverySeatHasExactlyOneOccasionAndEveryOccasionIsASeat()
    {
        Assert.NotEmpty(VoicedOccasions.All);
        Assert.Equal(
            Enum.GetValues<VoiceSeat>().OrderBy(seat => seat),
            VoicedOccasions.All.Select(occasion => occasion.Seat).OrderBy(seat => seat));

        foreach (var seat in Enum.GetValues<VoiceSeat>())
        {
            Assert.Equal(seat, VoicedOccasions.For(seat).Seat);
            Assert.Equal(Approved[seat], VoicedOccasions.For(seat).Decisions);
        }
    }

    [Fact]
    public void NoSeatIsAnErrorANoticeOrTheDogEar()
    {
        var checkedCount = 0;
        foreach (var occasion in VoicedOccasions.All)
        {
            foreach (var text in new[] { occasion.Seat.ToString(), occasion.Purpose })
            {
                var offending = Trouble.FirstOrDefault(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
                Assert.True(offending is null,
                    $"The seat {occasion.Seat} reads as trouble (\"{offending}\" in \"{text}\"). Errors, notices "
                    + "and the dog-ear are told in plain copy, never in the owner's voice (DESIGN.md §14).");
                checkedCount++;
            }
        }

        Assert.True(checkedCount >= 2 * Approved.Count, "The guard checked fewer seats than are approved.");
    }

    [Fact]
    public void ThePurposesAreDistinct() =>
        Assert.Equal(VoicedOccasions.All.Count, VoicedOccasions.All.Select(o => o.Purpose).Distinct().Count());

    /// <summary>
    /// Each empty page falls back to a neutral Serif italic line (D264); every other seat is hidden when
    /// there is no profile or key (D36, D118, D232).
    /// </summary>
    [Fact]
    public void EmptyPagesFallBackToANeutralLineAndEverythingElseHides()
    {
        var empties = VoicedOccasions.All.Where(o => o.Seat is VoiceSeat.StackUsedUp || o.Seat.ToString().StartsWith("Empty", StringComparison.Ordinal)).ToList();
        Assert.Equal(10, empties.Count);
        Assert.All(empties, o => Assert.Equal(VoiceFallback.NeutralLine, o.Fallback));
        Assert.All(VoicedOccasions.All.Except(empties), o => Assert.Equal(VoiceFallback.Hide, o.Fallback));
    }

    [Fact]
    public void OnlyTheChildSeatsAreSpokenToAChild()
    {
        var childSeats = VoicedOccasions.All.Where(o => o.Audience == VoiceAudience.Child).Select(o => o.Seat).ToHashSet();
        Assert.Equal(
            new HashSet<VoiceSeat>
            {
                VoiceSeat.ChildGreeting, VoiceSeat.ChildFavorites, VoiceSeat.EmptyChildFavorites,
                VoiceSeat.EmptyChildWatching, VoiceSeat.EmptyChildPlaylists,
            },
            childSeats);
    }

    /// <summary>
    /// Two seats the decisions rule out for P12: the Friday stack opener (D38 — "not the Friday stack
    /// opener") and the watching-ahead line (D99, deferred with "together" to the TV phase by D194).
    /// The closed-list test would catch them too; this one says why they are absent.
    /// </summary>
    [Fact]
    public void TheSeatsTheDecisionsRuleOutAreAbsent()
    {
        var names = Enum.GetNames<VoiceSeat>();
        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, name => name.Contains("Opener", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Ahead", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Cheat", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Together", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every call that asks for a voiced line names its purpose from <see cref="VoicedOccasions"/>, so a
    /// bare string cannot quietly voice an error. Source-level, across Pulp's own files. Nothing in
    /// Pulp calls the messenger yet (the heads wire it in Release C/D), so today this checks that the
    /// walk reached the Voice folder and found the list it enforces; it bites from the first call site.
    /// </summary>
    [Fact]
    public void EveryVoicedRequestNamesAnOccasionFromTheList()
    {
        var files = PulpSource.Files(".cs")
            .Where(file => !PulpSource.Relative(file).StartsWith("Pulp.Tests/", StringComparison.Ordinal))
            .ToList();
        Assert.Contains(files, file => PulpSource.Relative(file) == "Pulp.Domain/Voice/VoicedOccasions.cs");

        const string call = "new MessageRequest(";
        var offenders = new List<string>();
        foreach (var file in files)
        {
            foreach (var line in File.ReadAllLines(file))
            {
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal) || line.TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                var at = line.IndexOf(call, StringComparison.Ordinal);
                if (at < 0)
                {
                    continue;
                }

                var argument = line[(at + call.Length)..].TrimStart();
                if (!argument.StartsWith("VoicedOccasions.", StringComparison.Ordinal)
                    && !argument.StartsWith("facts.Occasion.Purpose", StringComparison.Ordinal))
                {
                    offenders.Add($"{PulpSource.Relative(file)}: {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These voice a message without naming an occasion from VoicedOccasions:\n  " + string.Join("\n  ", offenders));
    }
}
