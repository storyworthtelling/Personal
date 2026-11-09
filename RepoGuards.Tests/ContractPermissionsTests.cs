using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// The cross-app contracts (audit W2). Every one of them is a signature permission, and two Android rules
/// make a half-wired one fail with no error anywhere: a broadcast sent with <c>SendBroadcast(intent, P)</c>
/// reaches only a receiver whose app <b>holds</b> P, and a component guarded by P accepts only a caller
/// that holds it. Declaring a permission does not grant it to the declaring app. So until this guard,
/// Flyleaf declared RECEIVE_HIDDEN and never received Pulp's hidden apps, Index declared RECEIVE_BALANCE
/// and never received a balance, and Post never held the permission it sent under.
///
/// <para>The rule now is one file per head, <c>ContractPermissions.cs</c>, that both <b>declares</b>
/// (signature level) and <b>requests</b> every contract permission the head takes part in, from either
/// end. Declaring it in both apps also means the permission exists whichever app is installed first.
/// Each row below names the head, the constant that head uses and the value it must resolve to, so the
/// two ends are checked against one string rather than against each other's copies.</para>
/// </summary>
public partial class ContractPermissionsTests
{
    private const string Balance = "com.storyworthtelling.ledger.RECEIVE_BALANCE";
    private const string Usage = "com.storyworthtelling.ledger.RECEIVE_USAGE";
    private const string Budget = "com.storyworthtelling.constitution.RECEIVE_BUDGET";
    private const string Hidden = "com.storyworthtelling.focusphone.launcher.RECEIVE_HIDDEN";
    private const string LockedOpen = "com.storyworthtelling.focusphone.launcher.RECEIVE_LOCKED_OPEN";
    private const string Sessions = "com.storyworthtelling.pulp.RECEIVE_SESSIONS";
    private const string Recipe = "com.storyworthtelling.focusphone.camera.READ_RECIPE";
    private const string Paper = "com.storyworthtelling.focusphone.camera.READ_PAPER";

    private const string Post = "FocusPhone/Post.Android";
    private const string Flyleaf = "FocusPhone/Flyleaf.Android";
    private const string Plate = "FocusPhone/Plate.Android";
    private const string Index = "Ledger/Ledger.Android";
    private const string Pulp = "Pulp/Pulp.Android";
    private const string Constitution = "Constitution/Constitution.Android";

    /// <summary>Every contract, from both ends: (head, the constant it names, what that constant must be).</summary>
    public static readonly (string Head, string Constant, string Value)[] Contracts =
    [
        (Post, "IndexHandoff.Permission", Balance),
        (Index, "IndexIdentity.BalancePermission", Balance),
        (Pulp, "IndexUsageContract.Permission", Usage),
        (Index, "IndexIdentity.UsagePermission", Usage),
        (Index, "ConstitutionBudgetContract.Permission", Budget),
        (Constitution, "ConstitutionIdentity.BudgetPermission", Budget),
        (Pulp, "HiddenAppsHandOff.Permission", Hidden),
        (Flyleaf, "HiddenAppsHandoff.Permission", Hidden),
        (Pulp, "LockedOpenHandOff.Permission", LockedOpen),
        (Flyleaf, "LockedOpenHandoff.Permission", LockedOpen),
        (Flyleaf, "PlaybackSessionHandoff.Permission", Sessions),
        (Pulp, "SessionHandOff.Permission", Sessions),
        (Constitution, "RecipeReadContract.Permission", Recipe),
        (Plate, "ReadRequest.RecipePermission", Recipe),
        (Plate, "ReadRequest.PaperPermission", Paper),
        (Post, "ReadRequest.PaperPermission", Paper),
        (Index, "PlateStatementRead.Permission", Paper),
    ];

    /// <summary>Heads that ask whether a peer is installed before sending, and the peer they ask about.</summary>
    public static readonly (string Head, string Package)[] Senders =
    [
        (Post, "com.storyworthtelling.ledger"),
        (Pulp, "com.storyworthtelling.ledger"),
        (Pulp, "com.storyworthtelling.focusphone.launcher"),
        (Index, "com.storyworthtelling.constitution"),
        (Flyleaf, "com.storyworthtelling.pulp"),
    ];

    public static IEnumerable<object[]> Heads() => Contracts.Select(row => row.Head).Distinct().Select(head => new object[] { head });

    public static IEnumerable<object[]> Rows() => Contracts.Select(row => new object[] { row.Head, row.Constant, row.Value });

    [Fact]
    public void EveryContractHasTwoEnds()
    {
        foreach (var group in Contracts.GroupBy(row => row.Value))
        {
            Assert.True(group.Select(row => row.Head).Distinct().Count() >= 2, $"{group.Key} is named by only one head.");
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void EachEndNamesTheSameString(string head, string constant, string value)
    {
        Assert.True(Resolve(constant) == value, $"{head} names {constant}, which is not {value}.");
    }

    [Theory]
    [MemberData(nameof(Heads))]
    public void EachHeadDeclaresAndRequestsItsContractsInOneFile(string head)
    {
        var path = Path.Combine(Root(head), "ContractPermissions.cs");
        Assert.True(File.Exists(path), $"{head} has no ContractPermissions.cs.");
        var source = File.ReadAllText(path);

        foreach (var (_, constant, _) in Contracts.Where(row => row.Head == head))
        {
            Assert.Contains($"[assembly: Permission(Name = {constant}, ProtectionLevel = Protection.Signature)]", source);
            Assert.Contains($"[assembly: UsesPermission(Name = {constant})]", source);
        }

        var declared = DeclaredCustom().Matches(source).Count;
        Assert.True(declared == Contracts.Count(row => row.Head == head), $"{head}'s ContractPermissions.cs declares a permission the table does not list.");
    }

    [Theory]
    [MemberData(nameof(Heads))]
    public void NoOtherFileInTheHeadDeclaresOrRequestsAContractPermission(string head)
    {
        var constants = Contracts.Where(row => row.Head == head).Select(row => row.Constant).ToList();
        var offenders = Sources(head)
            .Where(file => Path.GetFileName(file) != "ContractPermissions.cs")
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return DeclaredCustom().IsMatch(text)
                    || constants.Any(constant => text.Contains($"UsesPermission({constant})") || text.Contains($"UsesPermission(Name = {constant})"));
            })
            .Select(file => Path.GetRelativePath(GuardScan.RepoRoot(), file))
            .ToList();

        Assert.True(offenders.Count == 0, "Contract permissions belong in ContractPermissions.cs:\n" + string.Join("\n", offenders));

        var manifests = Directory.EnumerateFiles(Root(head), "AndroidManifest.xml", SearchOption.AllDirectories).Where(Kept);
        Assert.DoesNotContain(manifests, manifest => File.ReadAllText(manifest).Contains("<permission ", StringComparison.Ordinal));
    }

    /// <summary>A broadcast is sent under, and a component guarded by, only a permission the head holds.</summary>
    [Theory]
    [MemberData(nameof(Heads))]
    public void EverySendAndEveryGuardNamesAPermissionTheHeadHolds(string head)
    {
        var held = Contracts.Where(row => row.Head == head).Select(row => row.Constant).ToHashSet();
        var named = Sources(head)
            .SelectMany(file => SentUnder().Matches(File.ReadAllText(file)).Concat(GuardedBy().Matches(File.ReadAllText(file))))
            .Select(match => match.Groups["constant"].Value.Trim())
            .Where(constant => !IsPlatform(constant))
            .ToList();

        var unheld = named.Where(constant => !held.Contains(constant)).Distinct().ToList();
        Assert.True(unheld.Count == 0, $"{head} sends under or is guarded by a permission it does not hold: {string.Join(", ", unheld)}");
    }

    [Fact]
    public void EveryContractSendIsInTheTable()
    {
        var heads = Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(Kept)
            .Where(project => Path.GetFileName(Path.GetDirectoryName(project)!).EndsWith(".Android", StringComparison.Ordinal))
            .Select(project => Path.GetRelativePath(GuardScan.RepoRoot(), Path.GetDirectoryName(project)!).Replace('\\', '/'))
            .Where(head => !head.StartsWith("Watch/", StringComparison.Ordinal));

        var missing = heads
            .Where(head => !Contracts.Any(row => row.Head == head))
            .Where(head => Sources(head).Any(file => SentUnder().IsMatch(File.ReadAllText(file))))
            .ToList();

        Assert.True(missing.Count == 0, "A head sends under a permission and is not in the contract table: " + string.Join(", ", missing));
    }

    [Fact]
    public void EverySenderCanSeeThePeerItAsksAbout()
    {
        foreach (var (head, package) in Senders)
        {
            var sources = Sources(head).Select(File.ReadAllText).ToList();
            var manifests = Directory.EnumerateFiles(Root(head), "AndroidManifest.xml", SearchOption.AllDirectories).Where(Kept).Select(File.ReadAllText);
            var seesAll = sources.Any(text => text.Contains("Manifest.Permission.QueryAllPackages", StringComparison.Ordinal));
            var queries = manifests.Any(text => text.Contains($"<package android:name=\"{package}\" />", StringComparison.Ordinal));
            Assert.True(seesAll || queries, $"{head} asks whether {package} is installed but cannot see it (no <queries> entry).");
        }
    }

    [Fact]
    public void TheGuardSeesASendAndAGuard()
    {
        Assert.Equal("Thing.Permission", SentUnder().Match("context.SendBroadcast(intent, Thing.Permission);").Groups["constant"].Value);
        Assert.Equal("Thing.Permission", SentUnder().Match("context.SendOrderedBroadcast(\n intent,\n Thing.Permission,\n x)").Groups["constant"].Value);
        Assert.Equal("Thing.Permission", GuardedBy().Match("[BroadcastReceiver(\n    Exported = true,\n    Permission = Thing.Permission)]").Groups["constant"].Value);
        Assert.True(IsPlatform(GuardedBy().Match("[Service(Permission = \"android.permission.BIND_DEVICE_ADMIN\")]").Groups["constant"].Value));
        Assert.Equal("A.Id + \".RECEIVE\"", GuardedBy().Match("[BroadcastReceiver(Permission = A.Id + \".RECEIVE\")]").Groups["constant"].Value.Trim());
        Assert.True(DeclaredCustom().IsMatch("[assembly: Permission(Name = A.B, ProtectionLevel = Protection.Signature)]"));
        Assert.Equal("a.b.c.READ", Resolve("ContractPermissionsTestsSample.Read"));
    }

    // ---- helpers ----

    private static string Resolve(string constant)
    {
        var dot = constant.IndexOf('.');
        return Evaluate(constant[..dot], constant[(dot + 1)..], 0);
    }

    // A const string is a literal, another const, or a sum of them; that is every shape the contracts use.
    private static string Evaluate(string type, string member, int depth)
    {
        Assert.True(depth < 8, $"{type}.{member} does not resolve.");
        var definition = new Regex(@"const string " + Regex.Escape(member) + @"\s*=\s*(?<expr>[^;]+);");
        var expression = AllSources()
            .Select(File.ReadAllText)
            .Where(text => Regex.IsMatch(text, @"\b(class|record|struct)\s+" + Regex.Escape(type) + @"\b"))
            .Select(text => definition.Match(text))
            .FirstOrDefault(match => match.Success)?.Groups["expr"].Value;
        Assert.True(expression is not null, $"No const {type}.{member} found.");

        return string.Concat(expression!.Split('+').Select(part => part.Trim()).Select(part =>
            part.StartsWith('"') ? part.Trim('"')
            : part.Contains('.') ? Evaluate(part[..part.LastIndexOf('.')].Split('.')[^1], part[(part.LastIndexOf('.') + 1)..], depth + 1)
            : Evaluate(type, part, depth + 1)));
    }

    private static List<string>? allSources;

    private static List<string> AllSources() => allSources ??= Directory
        .EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
        .Where(Kept)
        .ToList();

    private static IEnumerable<string> Sources(string head) =>
        Directory.EnumerateFiles(Root(head), "*.cs", SearchOption.AllDirectories).Where(Kept);

    private static string Root(string head) => Path.Combine(GuardScan.RepoRoot(), head.Replace('/', Path.DirectorySeparatorChar));

    // The platform's own permissions (BIND_DEVICE_ADMIN, BROADCAST_SMS, ...) are the platform's to hold.
    private static bool IsPlatform(string constant) =>
        constant.StartsWith("\"android.permission.", StringComparison.Ordinal) || constant.StartsWith("Manifest.Permission.", StringComparison.Ordinal);

    private static bool Kept(string path) =>
        !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj");

    [GeneratedRegex(@"\[assembly:\s*Permission\(Name\s*=")]
    private static partial Regex DeclaredCustom();

    [GeneratedRegex(@"Send(?:Ordered)?Broadcast\(\s*\w+\s*,\s*(?<constant>[^,)]+)")]
    private static partial Regex SentUnder();

    [GeneratedRegex(@"\[(?:BroadcastReceiver|Activity|Service)\([^\]]*?\bPermission\s*=\s*(?<constant>[^,)]+)")]
    private static partial Regex GuardedBy();
}

/// <summary>A sample for <see cref="ContractPermissionsTests.TheGuardSeesASendAndAGuard"/>'s resolver.</summary>
internal static class ContractPermissionsTestsSample
{
    public const string Package = "a.b";
    public const string Read = Package + ".c" + ".READ";
}
