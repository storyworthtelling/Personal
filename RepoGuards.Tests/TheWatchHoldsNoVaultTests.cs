using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// What secret material may be on the watch (P14 W-4.1.2, chunk B3): one handed-over, read-only Drive token in memory,
/// and nothing else. Read by content across both ends, since no single app's workflow watches the phone and the watch.
///
/// <para><b>The vault stays off the watch.</b> The plan has the vault passphrase typed once on the watch (A38, plan §2.4,
/// W-5.1.8). B3 did not build it and stopped to ask the owner: one passphrase opens the whole shared vault (every app's
/// keys, the Gmail app password, the Sleep Number login) and the personal one, and since A61 hands the Google tokens over
/// from the phone, A62 plays TIDAL through the phone and PC6 takes the Rolodex from Post, nothing on the watch needs to
/// open either. So no watch project names the vault, reaches the library that holds it, or receives its passphrase, and
/// the phone's watch halves never name it. Building W-5.1.8 is a decision made here, by the owner, not a drift.</para>
///
/// <para><b>Dash holds no token.</b> Post fetches the mail and relays the lines (<c>dash/mail</c>), so no Gmail token is
/// handed to the wrist, and nothing on Post's side names the keys contract.</para>
///
/// <para><b>The token leaves only as a header</b>, and is held no longer than the phone itself reuses one.</para>
/// </summary>
public partial class TheWatchHoldsNoVaultTests
{
    // The watch's shipping folders: everything under Watch/ but the tests, the mockups, the spikes and the docs.
    private static readonly string[] NotShipped = ["Watch/Watch.Tests/", "Watch/design/", "Watch/Spikes/"];

    private static readonly string[] CodeExtensions = [".cs", ".csproj", ".props", ".targets", ".xml", ".axml", ".json"];

    // The phone's ends of the watch contracts: Post's relay and listener, and Flyleaf's watch half.
    private static readonly string[] PhoneHalves =
        ["FocusPhone/Post.Android/Dash", "FocusPhone/Codex.Infrastructure/Dash", "FocusPhone/Flyleaf.Android/WatchUpdates.cs"];

    // Post's whole side: nothing there hands the wrist a token.
    private static readonly string[] PostSide = ["FocusPhone/Post.Android", "FocusPhone/Codex.Infrastructure/Dash", "FocusPhone/Codex.Domain"];

    // Where a token may be read as a header: its own folder, and the tests that check it. Release C (Wave's streaming,
    // W-6.1.2) adds its one HTTP seam here, deliberately.
    private static readonly string[] HeaderReaders = ["Watch/Watch.Domain/Keys/", "Watch/Watch.Tests/", "RepoGuards.Tests/"];

    [Fact]
    public void ThePassphraseNeverReachesTheWatch()
    {
        var files = WatchCode().ToList();
        Assert.Contains(files, file => file.Path == "Watch/Watch.Domain/Keys/KeyCodec.cs");
        Assert.Contains(files, file => file.Path == "Watch/Point.Installer/Point.Installer.csproj");

        var named = files.Where(file => VaultWord().IsMatch(file.Code)).Select(file => file.Path).ToList();

        Assert.True(named.Count == 0, "The vault and its passphrase stay off the watch until the owner rules on A38 (P14 B3):\n" + string.Join("\n", named));
    }

    [Fact]
    public void NoWatchProjectReachesTheVaultsLibrary()
    {
        var projects = WatchCode().Where(file => file.Path.EndsWith(".csproj", StringComparison.Ordinal)).Select(file => file.Path).ToList();
        Assert.NotEmpty(projects);

        var reaching = projects
            .Where(project => Closure(project).Any(reached => reached.EndsWith("/Storyworthtelling.Voice.csproj", StringComparison.Ordinal)))
            .ToList();

        Assert.True(reaching.Count == 0, "A watch project whose references reach the vault's library:\n" + string.Join("\n", reaching));
    }

    [Fact]
    public void ThePhonesWatchHalvesNameNoVault()
    {
        var files = PhoneHalves.SelectMany(Sources).ToList();
        Assert.Contains(files, file => file.Path.EndsWith("DashListener.cs", StringComparison.Ordinal));
        Assert.Contains(files, file => file.Path.EndsWith("WatchUpdates.cs", StringComparison.Ordinal));

        var named = files.Where(file => VaultWord().IsMatch(file.Code)).Select(file => file.Path).ToList();

        Assert.True(named.Count == 0, "What the phone sends the watch never comes from the vault:\n" + string.Join("\n", named));
    }

    [Fact]
    public void DashIsHandedNoToken()
    {
        var files = PostSide.SelectMany(Sources).ToList();
        Assert.Contains(files, file => file.Path.EndsWith("DashRelay.cs", StringComparison.Ordinal));

        var handing = files.Where(file => KeysContract().IsMatch(file.Code)).Select(file => file.Path).ToList();

        Assert.True(handing.Count == 0, "Post fetches the mail and relays the lines, so it hands the wrist no token:\n" + string.Join("\n", handing));
    }

    [Fact]
    public void ATokenLeavesOnlyAsAHeader()
    {
        var root = GuardScan.RepoRoot();
        var readers = GuardScan.Files(root)
            .Where(file => file.EndsWith(".cs", StringComparison.Ordinal))
            .Select(file => (Path: Relative(root, file), Code: Code(File.ReadAllText(file))))
            .Where(file => file.Code.Contains(".BearerHeader(", StringComparison.Ordinal))
            .Where(file => !HeaderReaders.Any(reader => file.Path.StartsWith(reader, StringComparison.Ordinal)))
            .Select(file => file.Path)
            .ToList();

        Assert.True(readers.Count == 0, "A handed-over token read as a header outside its own folder:\n" + string.Join("\n", readers));
    }

    [Fact]
    public void TheTokensValueStaysInItsAssembly()
    {
        // AccessToken's value is internal; an InternalsVisibleTo on the watch's domain would hand it to another assembly.
        var granting = WatchCode().Where(file => file.Code.Contains("InternalsVisibleTo", StringComparison.Ordinal)).Select(file => file.Path).ToList();

        Assert.True(granting.Count == 0, "The watch's domain shares its internals:\n" + string.Join("\n", granting));
    }

    [Fact]
    public void TheWristKeepsATokenNoLongerThanThePhone()
    {
        // The phone reuses a device-account token for 45 minutes of Google's hour; the wrist holds a handed one no longer.
        var watch = Minutes(Read("Watch/Watch.Domain/Keys/KeyLimits.cs"), "LongestHold");
        var phone = Minutes(Read("Storyworthtelling.Google/CachedAccessToken.cs"), "DefaultLifetime");

        Assert.Equal(phone, watch);
    }

    [Theory]
    [InlineData("var passphrase = store.Passphrase;")]
    [InlineData("new VaultPassphraseStore(context)")]
    [InlineData("SecretsVault.Empty")]
    [InlineData("using Storyworthtelling.Voice.Secrets;")]
    [InlineData("<ProjectReference Include=\"..\\..\\Storyworthtelling.Voice\\Storyworthtelling.Voice.csproj\" />")]
    [InlineData("PersonalVaultAccess.LoadAsync()")]
    [InlineData("const string Alias = \"vault-key\";")]
    public void TheGuardSeesTheVault(string sample)
    {
        Assert.Matches(VaultWord(), sample);
    }

    [Theory]
    [InlineData("KeyCodec.Decode(path, bytes)")]
    [InlineData("var token = hold.Current(now);")]
    [InlineData("WatchIdentity.DashMailPath")]
    public void TheGuardLeavesTheWatchAlone(string sample)
    {
        Assert.DoesNotMatch(VaultWord(), sample);
    }

    [Theory]
    [InlineData("WatchIdentity.GoogleKeysPath")]
    [InlineData("using Watch.Domain.Keys;")]
    [InlineData("new TokenHandoff(scope, token, life)")]
    [InlineData("\"/keys/google\"")]
    public void TheGuardSeesAHandoff(string sample)
    {
        Assert.Matches(KeysContract(), sample);
    }

    [Fact]
    public void CommentsAreNotCode()
    {
        Assert.DoesNotMatch(VaultWord(), Code("// the vault stays on the phone\n/// <summary>No passphrase here.</summary>\n<!-- nor the vault -->"));
    }

    // ---- reading ----

    private static IEnumerable<(string Path, string Code)> WatchCode()
    {
        var root = GuardScan.RepoRoot();
        return GuardScan.Files(Path.Combine(root, "Watch"))
            .Where(file => CodeExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Select(file => (Path: Relative(root, file), File: file))
            .Where(file => !NotShipped.Any(folder => file.Path.StartsWith(folder, StringComparison.Ordinal)))
            .Select(file => (file.Path, Code(File.ReadAllText(file.File))));
    }

    private static IEnumerable<(string Path, string Code)> Sources(string relative)
    {
        var root = GuardScan.RepoRoot();
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        IEnumerable<string> files = File.Exists(path)
            ? [path]
            : GuardScan.Files(path).Where(file => CodeExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
        return files.Select(file => (Relative(root, file), Code(File.ReadAllText(file))));
    }

    // Every project a project reaches through its ProjectReferences, itself included, as repository paths.
    private static IReadOnlyCollection<string> Closure(string project)
    {
        var root = GuardScan.RepoRoot();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var waiting = new Stack<string>([Path.GetFullPath(Path.Combine(root, project))]);
        while (waiting.Count > 0)
        {
            var next = waiting.Pop();
            if (!seen.Add(Relative(root, next)) || !File.Exists(next))
            {
                continue;
            }
            foreach (Match reference in ProjectReference().Matches(Code(File.ReadAllText(next))))
            {
                var included = reference.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar);
                waiting.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(next)!, included)));
            }
        }
        return seen;
    }

    private static int Minutes(string code, string name)
    {
        var match = Regex.Match(code, $@"\b{name}\s*=\s*TimeSpan\.FromMinutes\((?<minutes>\d+)\)");
        Assert.True(match.Success, $"{name} isn't set in minutes.");
        return int.Parse(match.Groups["minutes"].Value);
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    // A source without its comments: C#'s line comments and XML's.
    private static string Code(string source) => Regex.Replace(Regex.Replace(source, @"<!--.*?-->", string.Empty, RegexOptions.Singleline), @"///.*|//.*", string.Empty);

    // The vault, its passphrase, or the library that holds them.
    [GeneratedRegex(@"(?i)vault|passphrase|Storyworthtelling\.Voice\b")]
    private static partial Regex VaultWord();

    // The handed-over token's contract, by any of its names.
    [GeneratedRegex(@"\bGoogleKeysPath\b|\bWatch\.Domain\.Keys\b|\bTokenHandoff\b|""/keys/")]
    private static partial Regex KeysContract();

    [GeneratedRegex(@"<ProjectReference\s+Include=""(?<path>[^""]+)""")]
    private static partial Regex ProjectReference();
}
