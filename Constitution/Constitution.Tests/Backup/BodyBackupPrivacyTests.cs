using System.Reflection;
using Constitution.Domain.Body;
using Constitution.Infrastructure.Backup;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Tests.Backup;

/// <summary>
/// C-7.4.1 — what the backup says about itself says nothing about the person. The file's name is a
/// wire name (a rename orphans every backup already in Drive); and no result and no log line ever
/// carries a reading, a signal, a date or the passphrase, whichever way a call ends.
///
/// <para>The passphrase is built at run time, so no literal here is one; and the failures are made to
/// <i>echo</i> it, as a careless HTTP stack might, to show the backup takes it back out.</para>
/// </summary>
public class BodyBackupPrivacyTests
{
    private static string NewPassphrase() => "example-" + Guid.NewGuid().ToString("N");

    // ---- the file ----

    [Fact]
    public void TheFileNameIsAWireName()
    {
        Assert.Equal("storyworthtelling-constitution-body.json", BodyBackup.FileName);
    }

    [Fact]
    public void TheFileIsNotAnyOtherFileTheAppKeeps()
    {
        string[] others =
        [
            DrivePersonalVaultStore.FileName,
            "storyworthtelling-secrets.json",
            "storyworthtelling-constitution-household.json",
        ];

        Assert.DoesNotContain(BodyBackup.FileName, others);
    }

    // ---- results and log lines ----

    public static IEnumerable<object[]> Scenarios() =>
    [
        ["restored"],
        ["restore skipped"],
        ["restore no backup"],
        ["restore wrong passphrase"],
        ["restore corrupt"],
        ["restore bad shape"],
        ["restore unreachable"],
        ["restore no passphrase"],
        ["backed up"],
        ["unchanged"],
        ["nothing to back up"],
        ["refused unreadable"],
        ["refused bad shape"],
        ["refused newer"],
        ["unreachable"],
        ["write failed"],
        ["write put back"],
        ["no passphrase"],
    ];

    // Valid JSON in the right envelope, with a reading whose value is wrong and looks like one of the invented ones.
    private const string BadShape =
        """{"version":1,"installDay":"2026-11-01","readings":{"weight":[{"day":"2026-11-02","value":"100.25 lb"}]}}""";

    private static async Task<(List<string> Said, RecordingLog Log)> Run(string scenario, string passphrase)
    {
        var said = new List<string>();
        var phone = new BackupRig();
        void Say(object result) => said.Add(result.ToString()!);

        switch (scenario)
        {
            case "restored":
            case "restore skipped":
                phone.WithHistory();
                await phone.BackUp(passphrase);
                var fresh = new BackupRig(phone.Drive);
                if (scenario == "restore skipped")
                {
                    fresh.WithHistory();
                }

                Say(await fresh.Restore(passphrase));
                return (said, fresh.Log);

            case "restore no backup":
                Say(await phone.Restore(passphrase));
                break;

            case "restore wrong passphrase":
                phone.Drive.Content = VaultEnvelope.Write("{}", NewPassphrase());
                Say(await phone.Restore(passphrase));
                break;

            case "restore corrupt":
                phone.Drive.Content = "{ not an envelope";
                Say(await phone.Restore(passphrase));
                break;

            case "restore bad shape":
                phone.Drive.Content = VaultEnvelope.Write(BadShape, passphrase);
                Say(await phone.Restore(passphrase));
                break;

            case "refused bad shape":
                phone.WithHistory();
                phone.Drive.Content = VaultEnvelope.Write(BadShape, passphrase);
                Say(await phone.BackUp(passphrase));
                break;

            case "restore unreachable":
                phone.Drive.ReadFailure = new HttpRequestException("drive said no to " + passphrase);
                Say(await phone.Restore(passphrase));
                break;

            case "restore no passphrase":
                Say(await phone.Restore(null));
                break;

            case "backed up":
                phone.WithHistory();
                Say(await phone.BackUp(passphrase));
                break;

            case "unchanged":
                phone.WithHistory();
                await phone.BackUp(passphrase);
                Say(await phone.BackUp(passphrase));
                break;

            case "nothing to back up":
                Say(await phone.BackUp(passphrase));
                break;

            case "refused unreadable":
                phone.WithHistory();
                phone.Drive.Content = VaultEnvelope.Write("{}", NewPassphrase());
                Say(await phone.BackUp(passphrase));
                break;

            case "refused newer":
                phone.WithHistory();
                phone.Drive.Content = VaultEnvelope.Write("""{"version":2,"readings":{}}""", passphrase);
                Say(await phone.BackUp(passphrase));
                break;

            case "unreachable":
                phone.WithHistory();
                phone.Drive.ReadFailure = new HttpRequestException("drive said no to " + passphrase);
                Say(await phone.BackUp(passphrase));
                break;

            case "write failed":
                phone.WithHistory();
                phone.Drive.WriteFailure = new HttpRequestException("drive rejected " + passphrase);
                Say(await phone.BackUp(passphrase));
                break;

            case "write put back":
                phone.WithHistory();
                phone.Drive.OnWrite = (number, sent) => number == 1 ? sent[..(sent.Length / 2)] : sent;
                Say(await phone.BackUp(passphrase));
                break;

            case "no passphrase":
                phone.WithHistory();
                Say(await phone.BackUp(null));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Not a scenario this sweep knows.");
        }

        return (said, phone.Log);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task NoResultOrLogLineHoldsAReadingASignalOrThePassphrase(string scenario)
    {
        var passphrase = NewPassphrase();

        var (said, log) = await Run(scenario, passphrase);

        var everything = said.Concat(log.Lines).ToList();
        Assert.NotEmpty(everything);
        var forbidden = BackupInvented.EveryValueText()
            .Concat(BodySignals.All.Select(BodySignals.Id))
            .Concat(BodySignals.All.Select(signal => signal.ToString()))
            .Concat([passphrase, "2026-11", BackupInvented.InstallDay.ToString("O")]);
        foreach (var text in everything)
        {
            foreach (var secret in forbidden)
            {
                Assert.False(
                    text.Contains(secret, StringComparison.OrdinalIgnoreCase),
                    $"A {scenario} result or log line holds something private.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task EveryLogLineIsOneShortPhraseAboutBody(string scenario)
    {
        var (_, log) = await Run(scenario, NewPassphrase());

        Assert.NotEmpty(log.Lines);
        Assert.All(log.Lines, line =>
        {
            Assert.InRange(line.Length, 1, 80);
            Assert.DoesNotContain('\n', line);
            Assert.StartsWith("body ", line, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AFailureNamesItselfButNeverThePassphraseItsMessageEchoed()
    {
        var passphrase = NewPassphrase();
        var phone = new BackupRig().WithHistory();
        phone.Drive.ReadFailure = new HttpRequestException("could not reach drive " + passphrase);

        var backup = await phone.BackUp(passphrase);
        var restore = await new BackupRig(phone.Drive).Restore(passphrase);

        Assert.Contains("HttpRequestException", backup.Detail);
        Assert.Contains("could not reach drive", backup.Detail);
        Assert.DoesNotContain(passphrase, backup.Detail, StringComparison.Ordinal);
        Assert.Contains("could not reach drive", restore.Detail);
        Assert.DoesNotContain(passphrase, restore.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AResultCanOnlyHoldAnOutcomeAndAShortDetail()
    {
        foreach (var type in new[] { typeof(BodyRestoreResult), typeof(BodyBackupResult) })
        {
            var members = type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(property => property.Name != "EqualityContract")
                .Select(property => (property.Name, property.PropertyType))
                .OrderBy(pair => pair.Name)
                .ToArray();

            Assert.Contains(members, pair => pair.Name == "Outcome" && pair.PropertyType.IsEnum);
            Assert.Contains(members, pair => pair.Name == "Detail" && pair.PropertyType == typeof(string));
            Assert.All(members, pair => Assert.True(
                pair.Name is "Outcome" or "Detail" or "NeedsAttention",
                $"{type.Name}.{pair.Name} could carry something other than an outcome."));
        }
    }

    [Fact]
    public async Task ThePassphraseIsNotRememberedBetweenCalls()
    {
        var phone = new BackupRig().WithHistory();

        Assert.Equal(BodyBackupOutcome.BackedUp, (await phone.BackUp()).Outcome);

        // Another phrase is a wrong one; no phrase is none; the first is not kept to fall back on.
        phone.Store.Upsert([BackupInvented.One(BodySignal.Steps, new DateOnly(2026, 11, 20), 9000)]);
        Assert.Equal(BodyBackupOutcome.RefusedUnreadable, (await phone.BackUp(BackupInvented.OtherPassphrase)).Outcome);
        Assert.Equal(BodyBackupOutcome.NoPassphrase, (await phone.BackUp(null)).Outcome);

        var fresh = new BackupRig(phone.Drive);
        Assert.Equal(BodyRestoreOutcome.NoPassphrase, (await fresh.Restore(null)).Outcome);
    }

    [Fact]
    public void NeedsAttentionIsWhatTheOwnerCanFix()
    {
        Assert.True(new BodyRestoreResult(BodyRestoreOutcome.WrongPassphrase).NeedsAttention);
        Assert.True(new BodyRestoreResult(BodyRestoreOutcome.Corrupt).NeedsAttention);
        Assert.False(new BodyRestoreResult(BodyRestoreOutcome.Restored).NeedsAttention);
        Assert.False(new BodyRestoreResult(BodyRestoreOutcome.NoBackup).NeedsAttention);
        Assert.False(new BodyRestoreResult(BodyRestoreOutcome.Unreachable).NeedsAttention);

        Assert.True(new BodyBackupResult(BodyBackupOutcome.RefusedUnreadable).NeedsAttention);
        Assert.True(new BodyBackupResult(BodyBackupOutcome.Failed).NeedsAttention);
        Assert.False(new BodyBackupResult(BodyBackupOutcome.BackedUp).NeedsAttention);
        Assert.False(new BodyBackupResult(BodyBackupOutcome.Unchanged).NeedsAttention);
        Assert.False(new BodyBackupResult(BodyBackupOutcome.NothingToBackup).NeedsAttention);
        Assert.False(new BodyBackupResult(BodyBackupOutcome.Unreachable).NeedsAttention);
    }
}
