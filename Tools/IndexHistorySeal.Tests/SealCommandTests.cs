using System.Text;
using IndexHistorySeal;
using Index.Domain.Model;
using Index.Infrastructure.Snowball;

namespace IndexHistorySeal.Tests;

/// <summary>
/// The sealing console, end to end against the invented history: what it writes is exactly what the app's
/// Drive source opens, it refuses what the app could not read, and nothing it prints or leaves behind
/// carries the passphrase or any of the figures.
/// </summary>
public sealed class SealCommandTests : IDisposable
{
    private const string Passphrase = "an-invented-passphrase-for-tests";

    private readonly string directory = Directory.CreateTempSubdirectory("index-history-seal-tests-").FullName;
    private readonly StringWriter stdout = new();
    private readonly StringWriter stderr = new();

    private string Input => Path.Combine(directory, "history.json");

    private string Output => Path.Combine(directory, "sealed.json");

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static string Fixture()
    {
        using var stream = typeof(SealCommandTests).Assembly.GetManifestResourceStream("synthetic-snowball-history.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private int Run(
        string[] args, string? envPassphrase = Passphrase, Func<string?>? prompt = null)
    {
        return SealCommand.Run(
            args,
            name => name == SealCommand.PassphraseVariable ? envPassphrase : null,
            prompt ?? (() => null),
            stdout, stderr);
    }

    private void GivenTheHistory() => File.WriteAllText(Input, Fixture(), new UTF8Encoding(false));

    private string Everything() => stdout + Environment.NewLine + stderr;

    // ---- the round trip ----

    [Fact]
    public void WhatItWritesIsWhatTheAppsReaderOpens()
    {
        GivenTheHistory();

        var code = Run([Input, Output]);

        Assert.Equal(0, code);
        var opened = SnowballHistorySealer.Open(File.ReadAllText(Output), Passphrase);
        Assert.Equal(SnowballHistoryStatus.Loaded, opened.Status);
        Assert.Equal(70, opened.History!.Weeks.Count);
        Assert.Equal(10, opened.History.Loans.Count);
        Assert.Equal(700, opened.History.Rows.Count);
    }

    [Fact]
    public void AWrongPassphraseDoesNotOpenIt()
    {
        GivenTheHistory();
        Run([Input, Output]);

        var opened = SnowballHistorySealer.Open(File.ReadAllText(Output), "not the passphrase");

        Assert.Equal(SnowballHistoryStatus.WrongPassphrase, opened.Status);
    }

    [Fact]
    public void ItPrintsCountsAndNeverALoanAFigureOrThePassphrase()
    {
        GivenTheHistory();

        Run([Input, Output]);

        var printed = Everything();
        Assert.Contains("70 weeks", printed, StringComparison.Ordinal);
        Assert.Contains("10 loans", printed, StringComparison.Ordinal);
        Assert.DoesNotContain(Passphrase, printed, StringComparison.Ordinal);
        foreach (var name in SnowballHistorySealer.Open(File.ReadAllText(Output), Passphrase).History!.Loans)
        {
            Assert.DoesNotContain(name, printed, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheOutputHoldsNeitherThePassphraseNorAnyPlaintext()
    {
        GivenTheHistory();

        Run([Input, Output]);

        var written = File.ReadAllText(Output);
        Assert.DoesNotContain(Passphrase, written, StringComparison.Ordinal);
        Assert.DoesNotContain("Maple Ridge", written, StringComparison.Ordinal);
        Assert.DoesNotContain("balance", written, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItLeavesNoOtherFileBehind()
    {
        GivenTheHistory();

        Run([Input, Output]);

        Assert.Equal(
            [Path.GetFileName(Input), Path.GetFileName(Output)],
            Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    // ---- the passphrase ----

    [Fact]
    public void WithNoVariableItAsksAndUsesWhatIsTyped()
    {
        GivenTheHistory();

        var code = Run([Input, Output], envPassphrase: null, prompt: () => "typed-at-the-prompt");

        Assert.Equal(0, code);
        Assert.Equal(
            SnowballHistoryStatus.Loaded,
            SnowballHistorySealer.Open(File.ReadAllText(Output), "typed-at-the-prompt").Status);
    }

    [Fact]
    public void TheVariableWinsOverThePrompt()
    {
        GivenTheHistory();

        Run([Input, Output], prompt: () => "should-not-be-used");

        Assert.Equal(SnowballHistoryStatus.Loaded, SnowballHistorySealer.Open(File.ReadAllText(Output), Passphrase).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoPassphraseAnywhereIsRefusedAndNothingIsWritten(string? typed)
    {
        GivenTheHistory();

        var code = Run([Input, Output], envPassphrase: null, prompt: () => typed);

        Assert.NotEqual(0, code);
        Assert.False(File.Exists(Output));
        Assert.Contains(SealCommand.PassphraseVariable, stderr.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--passphrase=hunter2-invented")]
    [InlineData("--password=hunter2-invented")]
    [InlineData("-p=hunter2-invented")]
    public void APassphraseOnTheCommandLineIsRefusedAndNeverEchoed(string option)
    {
        GivenTheHistory();

        var code = Run([Input, Output, option]);

        Assert.NotEqual(0, code);
        Assert.False(File.Exists(Output));
        Assert.DoesNotContain("hunter2-invented", Everything(), StringComparison.Ordinal);
    }

    [Fact]
    public void AThirdPositionalArgumentIsRefusedAndNeverEchoed()
    {
        GivenTheHistory();

        var code = Run([Input, Output, "hunter2-invented"]);

        Assert.NotEqual(0, code);
        Assert.False(File.Exists(Output));
        Assert.DoesNotContain("hunter2-invented", Everything(), StringComparison.Ordinal);
    }

    // ---- the input ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"weeks\":[],\"loans\":[],\"rows\":[]}")]
    [InlineData("{\"weeks\":[\"2024-01-05\"],\"loans\":[\"A\"],\"rows\":[{\"loan\":\"B\",\"week\":\"2024-01-05\",\"balance\":1}]}")]
    public void MalformedOrEmptyInputIsRefusedAndNothingIsWritten(string content)
    {
        File.WriteAllText(Input, content);

        var code = Run([Input, Output]);

        Assert.NotEqual(0, code);
        Assert.False(File.Exists(Output));
        Assert.False(string.IsNullOrWhiteSpace(stderr.ToString()));
        Assert.DoesNotContain(Passphrase, Everything(), StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingInputFileIsRefused()
    {
        var code = Run([Input, Output]);

        Assert.NotEqual(0, code);
        Assert.False(File.Exists(Output));
    }

    [Fact]
    public void TheInputIsNeverTouched()
    {
        GivenTheHistory();
        var before = File.ReadAllBytes(Input);

        Run([Input, Output]);

        Assert.Equal(before, File.ReadAllBytes(Input));
    }

    // ---- the output ----

    [Fact]
    public void ItWillNotOverwriteWithoutForce()
    {
        GivenTheHistory();
        File.WriteAllText(Output, "already here");

        var code = Run([Input, Output]);

        Assert.NotEqual(0, code);
        Assert.Equal("already here", File.ReadAllText(Output));
        Assert.Contains("--force", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ForceOverwrites()
    {
        GivenTheHistory();
        File.WriteAllText(Output, "already here");

        var code = Run([Input, Output, "--force"]);

        Assert.Equal(0, code);
        Assert.Equal(SnowballHistoryStatus.Loaded, SnowballHistorySealer.Open(File.ReadAllText(Output), Passphrase).Status);
    }

    [Fact]
    public void ForceMayComeFirst()
    {
        GivenTheHistory();
        File.WriteAllText(Output, "already here");

        Assert.Equal(0, Run(["--force", Input, Output]));
    }

    [Fact]
    public void TheOutputCanNeverBeTheInput()
    {
        GivenTheHistory();
        var before = File.ReadAllText(Input);

        var code = Run([Input, Input, "--force"]);

        Assert.NotEqual(0, code);
        Assert.Equal(before, File.ReadAllText(Input)); // the plaintext was not replaced, and not "sealed in place"
    }

    // ---- usage ----

    [Theory]
    [InlineData]
    [InlineData("only-one-path.json")]
    public void TooFewArgumentsPrintUsage(params string[] args)
    {
        var code = Run(args);

        Assert.NotEqual(0, code);
        Assert.Contains("Usage", Everything(), StringComparison.Ordinal);
    }

    [Fact]
    public void HelpIsNotAnError()
    {
        var code = Run(["--help"]);

        Assert.Equal(0, code);
        Assert.Contains(SealCommand.PassphraseVariable, stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("storyworthtelling-index-snowball-history.json", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheUsageNamesTheDriveFileTheAppLooksFor() =>
        Assert.Equal(DriveSnowballHistorySource.FileName, SealCommand.DriveFileName);
}
