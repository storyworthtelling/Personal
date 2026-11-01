using System.Text.RegularExpressions;

namespace Storyworthtelling.Maui.Tests.Diagnostics;

// LOG-LOOP-PLAN.md §4 for the MAUI heads, read by content (the heads need the Android workload, this suite does
// not): each installs the tap watch and the log loop, feeds every touch from MainActivity, runs its worker through
// the worker trail and uploads from it, offers Send logs now, and declares each of its key actions through its
// constant — the list of which is fixed here, so a new one is a deliberate edit. Only taps are key actions:
// Index's refresh and worksheet run on their own and Foreword's mapping saves as it is typed, so none is one.
public partial class MauiTapWatchInstalledTests
{
    public sealed record Head(string App, string Folder, string KeyActionsFile, string KeyActionsClass, string Worker, string[] KeyActions);

    private static readonly Head[] Heads =
    [
        new("index", "Ledger/Ledger.Android", "Ledger/Ledger.Domain/Diagnostics/IndexKeyActions.cs", "IndexKeyActions", "RefreshWorker.cs",
            ["index.connect-bank", "index.send-logs-now"]),
        new("folio", "Storyboarder/Folio.Android", "Storyboarder/Folio.Domain/Diagnostics/FolioKeyActions.cs", "FolioKeyActions", "PipelineWorker.cs",
            ["folio.add-manuscript", "folio.retry", "folio.open-report", "folio.send-logs-now"]),
        new("foreword", "Foreword/Foreword.Android", "Foreword/Foreword.Domain/Diagnostics/ForewordKeyActions.cs", "ForewordKeyActions", "DeliveryWorker.cs",
            ["foreword.deliver-now", "foreword.pair-remarkable", "foreword.send-logs-now"]),
    ];

    public static TheoryData<string> Apps() => [.. Heads.Select(h => h.App)];

    private static Head For(string app) => Heads.Single(h => h.App == app);

    [Theory]
    [MemberData(nameof(Apps))]
    public void MainActivityFeedsEveryTouchAndAttachesTheWatch(string app)
    {
        var main = Read(For(app).Folder, "MainActivity.cs");

        Assert.Contains("DispatchTouchEvent(", main, StringComparison.Ordinal);
        Assert.Contains("MauiTapWatch.Touch(this", main, StringComparison.Ordinal);
        Assert.Contains("MauiTapWatch.Attach(this)", main, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Apps))]
    public void MauiProgramInstallsTheWatchAndTheLoop(string app)
    {
        var program = Read(For(app).Folder, "MauiProgram.cs");

        Assert.Contains("MauiTapWatch.Install(MauiLogLoop.Tap)", program, StringComparison.Ordinal);
        Assert.Contains("MauiLogLoop.Install(", program, StringComparison.Ordinal);
        Assert.Contains($"\"{app}\"", program, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Apps))]
    public void TheWorkerIsTrailedAndUploadsInTheWindow(string app)
    {
        var worker = Read(For(app).Folder, For(app).Worker);

        Assert.Contains("MauiLogLoop.Worker(", worker, StringComparison.Ordinal);
        Assert.Contains("MauiLogLoop.UploadIfDueAsync(", worker, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Apps))]
    public void SendLogsNowIsReachable(string app)
    {
        var text = HeadText(For(app));

        Assert.Contains("MauiLogLoop.UploadAsync(", text, StringComparison.Ordinal);
        Assert.Contains($"{For(app).KeyActionsClass}.SendLogsNow", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Apps))]
    public void EachHeadDeclaresExactlyItsKeyActions(string app)
    {
        var head = For(app);
        var constants = Constants(head);

        Assert.Equal(head.KeyActions.Order(StringComparer.Ordinal), constants.Values.Order(StringComparer.Ordinal));
        var text = HeadText(head);
        var missing = constants.Keys
            .Where(name => !text.Contains($"MauiTapWatch.Action({head.KeyActionsClass}.{name}", StringComparison.Ordinal))
            .ToList();
        Assert.True(missing.Count == 0, $"{app}: declared but never started, so never watched: " + string.Join(", ", missing));
    }

    // §4.2: the MAUI sign-ins, each trailed: the Google account picker (and its no-return check on resume), Plaid's
    // Hosted Link in Index, and the reMarkable pairing in Folio and Foreword.
    [Theory]
    [MemberData(nameof(Apps))]
    public void TheGooglePickerIsTrailed(string app)
    {
        var signIn = Read(For(app).Folder, "DeviceAccountSignIn.cs");

        foreach (var step in new[] { "DeviceAccountTrail.Begin(", "DeviceAccountTrail.LeftApp()", "DeviceAccountTrail.Chosen()", "DeviceAccountTrail.Cancelled()", "DeviceAccountTrail.Token(", "DeviceAccountTrail.Failed(" })
        {
            Assert.Contains(step, signIn, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheKitChecksForANoReturnOnEveryResume() =>
        Assert.Contains("DeviceAccountTrail.CheckPending()", File.ReadAllText(Path.Combine(RepoRoot(), "Storyworthtelling.Maui", "Diagnostics", "MauiTapWatch.cs")), StringComparison.Ordinal);

    [Fact]
    public void IndexTrailsPlaidHostedLink()
    {
        var page = Read("Ledger/Ledger.Android", "PlaidLinkPage.xaml.cs");

        foreach (var step in new[] { "PlaidLinkTrail.Start(", ".LinkTokenFailed(", ".ChromeFailed(", ".CameBack(", ".NoPublicToken(", ".ExchangeFailed(", ".Connected(", "MauiLogLoop.SignIn(" })
        {
            Assert.Contains(step, page, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("folio")]
    [InlineData("foreword")]
    public void TheRemarkablePairingIsTrailed(string app)
    {
        var setup = Read(For(app).Folder, "SetupPage.xaml.cs");

        Assert.Contains("PairingTrail.Start()", setup, StringComparison.Ordinal);
        Assert.Contains("PairingTrail.Paired(", setup, StringComparison.Ordinal);
        Assert.Contains("PairingTrail.Failed(", setup, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> Constants(Head head) =>
        Constant().Matches(File.ReadAllText(Path.Combine(RepoRoot(), head.KeyActionsFile)))
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["id"].Value, StringComparer.Ordinal);

    private static string HeadText(Head head) => string.Concat(
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), head.Folder), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));

    private static string Read(string folder, string name)
    {
        var path = Directory.EnumerateFiles(Path.Combine(RepoRoot(), folder), name, SearchOption.AllDirectories)
            .First(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        return File.ReadAllText(path);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Storyworthtelling.Maui")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("The repository root was not found above the test output.");
    }

    [GeneratedRegex(@"public const string (?<name>\w+) = ""(?<id>[^""]+)"";")]
    private static partial Regex Constant();
}
