using Constitution.Domain.Diagnostics;
using Constitution.Domain.Notices;

namespace Constitution.Tests.Diagnostics;

/// <summary>
/// C-8.3.2 — the Diagnostics export is safe to share: it carries counts, outcomes, document names and
/// where they went, notice kinds and credential states, and no reading, weight, calorie, name,
/// birthdate or recipe text (CLAUDE.md: the repository is public; plan §7: diagnostics carry shapes
/// and outcomes, never values or names).
///
/// <para>The export is rendered from a seeded, invented household (Sam, Lee, Robin, Juniper; invented
/// dishes and readings) and scanned. The household's private words go in through the one door the
/// real app has for them — a <see cref="NoticeBoard"/> whose cause keys name a person, a dish and a
/// reading — and the test proves none comes out. The scan is shown to bite by planting each kind of
/// private thing, assembled at run time, onto the clean export.</para>
///
/// <para><b>What this cannot catch:</b> a private fact that has no shape — a name or dish nobody
/// listed, a number small enough to look like a count — because nothing structural separates "3
/// workouts" from "3 kg". That is why the snapshot type has no member that can hold one
/// (<see cref="DiagnosticsSnapshotShapeTests"/>); the scan is the second line, not the first.</para>
/// </summary>
public class DiagnosticsPrivacyTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 7, 30, 0, TimeSpan.Zero);

    /// <summary>Private words as they would arrive on a notice's cause key, assembled from parts so no source line is a real-looking fact.</summary>
    private static readonly string[] PrivateCauseKeys =
    [
        "recipe:Lentil soup:Sam",
        "weight:81" + ".3 kg:Lee",
        "pulse:62 bpm:Robin",
        "kcal:1840:Juniper",
        "birth:1987" + "-03-09",
    ];

    internal static DiagnosticsSnapshot SeededSnapshot()
    {
        var board = new NoticeBoard();
        var kinds = new[]
        {
            NoticeKind.ReMarkableDelivery, NoticeKind.LowConfidenceRead, NoticeKind.LowConfidenceRead,
            NoticeKind.BoxUnderTen, NoticeKind.BedOffline,
        };
        for (var index = 0; index < PrivateCauseKeys.Length; index++)
        {
            board.Raise(kinds[index], PrivateCauseKeys[index], Start.AddMinutes(index));
        }

        return new DiagnosticsSnapshot(
            AppVersion.Parse("1.0.57"),
            InstallRole.Owner,
            new DiagnosticsCounts(workoutsPlanned: 3, workoutsDone: 2, recipesInBox: 14, NoticeCount.Tally(board.Waiting())),
            [
                new ComponentOutcome(DiagnosticsComponent.MorningDelivery, DiagnosticsOutcome.Succeeded),
                new ComponentOutcome(DiagnosticsComponent.ReadBack, DiagnosticsOutcome.FoundNothingRead),
                new ComponentOutcome(DiagnosticsComponent.BodySync, DiagnosticsOutcome.Unreachable),
            ],
            [
                new DocumentDelivery(DocumentFile.For(DocumentKind.Workout, new DateOnly(2026, 11, 17)), DeliveryTarget.ReMarkable, DiagnosticsOutcome.Succeeded),
                new DocumentDelivery(DocumentFile.For(DocumentKind.Meals, new DateOnly(2026, 11, 15)), DeliveryTarget.Drive, DiagnosticsOutcome.Refused),
            ],
            [
                new CredentialStatus(ConnectionId.Parse(ConnectionIds.SleepNumber), CredentialState.Rejected),
                new CredentialStatus(ConnectionId.Parse(ConnectionIds.ReMarkable), CredentialState.Working),
                new CredentialStatus(ConnectionId.Parse(ConnectionIds.Claude), CredentialState.Working),
                new CredentialStatus(ConnectionId.Parse(ConnectionIds.Gemini), CredentialState.Missing),
                new CredentialStatus(ConnectionId.Parse(ConnectionIds.Google), CredentialState.Working),
            ]);
    }

    // ---- NoReadingsNamesOrRecipes -------------------------------------------------------------

    [Fact]
    public void NoReadingsNamesOrRecipes()
    {
        var export = DiagnosticsRenderer.Render(SeededSnapshot());

        Assert.False(string.IsNullOrWhiteSpace(export));
        Assert.Empty(PrivacyScan.Find(export));
    }

    [Fact]
    public void TheExportCarriesTheFieldsItPromises()
    {
        var export = DiagnosticsRenderer.Render(SeededSnapshot());

        Assert.Contains("Version: 1.0.57", export);
        Assert.Contains("Install role: owner", export);
        Assert.Contains("Workouts planned: 3", export);
        Assert.Contains("Workouts done: 2", export);
        Assert.Contains("Recipes in the box: 14", export);
        Assert.Contains("ReMarkableDelivery: 1", export);
        Assert.Contains("LowConfidenceRead: 2", export);
        Assert.Contains("workout-2026-11-17.pdf", export);
        Assert.Contains("meals-2026-11-15.pdf", export);
        Assert.Contains("sleep-number: Rejected", export);
        Assert.Contains("remarkable: Working", export);
        Assert.Contains("gemini: Missing", export);
        Assert.Contains("Morning delivery:", export);
        Assert.Contains("Read-back:", export);
    }

    [Fact]
    public void ANoticesCauseKeyNeverReachesTheExport()
    {
        var export = DiagnosticsRenderer.Render(SeededSnapshot());

        foreach (var key in PrivateCauseKeys)
        {
            // The first segment is only a tag ("recipe" is a word the export's own count line uses);
            // the rest is the private value that rode in on the cause key.
            foreach (var part in key.Split(':', StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                // Whole words: "Lee" is not found in "sleep-number".
                Assert.False(
                    System.Text.RegularExpressions.Regex.IsMatch(
                        export, $"(?i)(?<![A-Za-z0-9]){System.Text.RegularExpressions.Regex.Escape(part)}(?![A-Za-z0-9])"),
                    "A piece of a notice's cause key reached the export.");
            }
        }
    }

    [Fact]
    public void EveryClosedValueRendersCleanly()
    {
        // Every enum value, in every slot, in one export: a fixed phrase that grew a digit or a
        // health word later would fail here rather than in the one state a seeded export missed.
        var outcomes = Enum.GetValues<DiagnosticsOutcome>();
        var components = Enum.GetValues<DiagnosticsComponent>();
        var snapshot = new DiagnosticsSnapshot(
            AppVersion.Parse("1.0.1"),
            InstallRole.Partner,
            new DiagnosticsCounts(0, 0, 0, Enum.GetValues<NoticeKind>().Select(kind => new NoticeCount(kind, 1)).ToList()),
            components.Select((component, index) => new ComponentOutcome(component, outcomes[index % outcomes.Length])).ToList(),
            Enum.GetValues<DocumentKind>().SelectMany(kind => Enum.GetValues<DeliveryTarget>().Select(target =>
                new DocumentDelivery(DocumentFile.For(kind, new DateOnly(2026, 1, 2)), target, outcomes[(int)kind % outcomes.Length]))).ToList(),
            ConnectionIds.All.Select((id, index) => new CredentialStatus(ConnectionId.Parse(id), Enum.GetValues<CredentialState>()[index % 3])).ToList());

        var export = DiagnosticsRenderer.Render(snapshot);

        Assert.Empty(PrivacyScan.Find(export));
        foreach (var outcome in outcomes)
        {
            Assert.Empty(PrivacyScan.Find(DiagnosticsPhrases.Of(outcome)));
            Assert.False(string.IsNullOrWhiteSpace(DiagnosticsPhrases.Of(outcome)));
        }
        foreach (var component in components)
        {
            Assert.Empty(PrivacyScan.Find(DiagnosticsPhrases.Of(component)));
        }
    }

    [Fact]
    public void AnInstallRoleOfUnknownStillRenders()
    {
        var snapshot = new DiagnosticsSnapshot(
            AppVersion.Parse("1.0.9"), InstallRole.Unknown, new DiagnosticsCounts(0, 0, 0, []), [], [], []);

        var export = DiagnosticsRenderer.Render(snapshot);

        Assert.Contains("Install role: not known yet", export);
        Assert.Empty(PrivacyScan.Find(export));
    }

    // ---- the scan is not vacuous --------------------------------------------------------------

    public static IEnumerable<object[]> PlantedPrivateThings()
    {
        yield return ["household name", "Su" + "mmary of " + "Sa" + "m"];
        yield return ["household name", "owner: " + "Jun" + "iper"];
        yield return ["dish", "box: Len" + "til so" + "up"];
        yield return ["dish", "box: Pad " + "thai"];
        yield return ["forbidden word", "We" + "ight today"];
        yield return ["forbidden word", "Cal" + "orie target"];
        yield return ["forbidden word", "Bir" + "thday soon"];
        yield return ["reading with a unit", "last: 81" + " kg"];
        yield return ["reading with a unit", "last: 62" + "bpm"];
        yield return ["reading with a unit", "last: 1840" + " kcal"];
        yield return ["decimal number", "last: 81" + ".3"];
        yield return ["ratio", "pressure 120" + "/80"];
        yield return ["date", "born 1987" + "-03-09"];
        yield return ["date", "born 03" + "/09/87"];
        yield return ["long number", "id 12" + "345"];
    }

    [Theory]
    [MemberData(nameof(PlantedPrivateThings))]
    public void TheScanFiresOnAPlantedPrivateThing(string expectedKind, string planted)
    {
        var clean = DiagnosticsRenderer.Render(SeededSnapshot());
        Assert.Empty(PrivacyScan.Find(clean));

        var kinds = PrivacyScan.Find(clean + "\n" + planted + "\n");

        Assert.Contains(expectedKind, kinds);
    }

    [Fact]
    public void ADateOfBirthIsNotExcusedBecauseTheExportAllowsDocumentDates()
    {
        var clean = DiagnosticsRenderer.Render(SeededSnapshot());
        Assert.Contains("workout-2026-11-17.pdf", clean);

        var kinds = PrivacyScan.Find(clean + "\nborn on 1987" + "-03-09\n");

        Assert.Contains("date", kinds);
    }

    [Theory]
    [InlineData("1.0.1987")]
    [InlineData("1.0.2026")]
    [InlineData("1.0.12345")]
    [InlineData("1.0.987654321")]
    public void ALargeCiRunNumberStillExportsAndStillScansClean(string version)
    {
        // CI stamps 1.0.<run number>; the export must not start failing the day that passes 1900 or 99999.
        var snapshot = new DiagnosticsSnapshot(
            AppVersion.Parse(version), InstallRole.Owner, new DiagnosticsCounts(0, 0, 0, []), [], [], []);

        var export = DiagnosticsRenderer.Render(snapshot);

        Assert.Contains("Version: " + version, export);
        Assert.Empty(PrivacyScan.Find(export));
    }

    [Fact]
    public void TheVersionLineIsExcusedOnlyInTheShapeTheBuildProduces()
    {
        // 1.0.<run number> up to nine digits; a longer number on that line is a long number like any other.
        Assert.Contains("long number", PrivacyScan.Find("Version: 1.0." + "1234567890\n"));
    }

    [Fact]
    public void ADocumentFileNameIsTheOnlyPlaceADateIsAllowed()
    {
        // The same date as a document line passes; as a free sentence it does not.
        Assert.Empty(PrivacyScan.Find("  workout-2026-11-17.pdf: reMarkable, delivered\nVersion: 1.0.57\n"));
        Assert.NotEmpty(PrivacyScan.Find("on 2026-11-17 something happened"));
    }
}
