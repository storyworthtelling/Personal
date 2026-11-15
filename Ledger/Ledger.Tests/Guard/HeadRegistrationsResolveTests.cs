using System.Reflection;
using System.Text.RegularExpressions;
using Index.Infrastructure.Worksheet;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Every Infrastructure type the head registers by type alone (<c>AddScoped&lt;T&gt;()</c>,
/// <c>AddSingleton&lt;I, T&gt;()</c>, <c>AddHttpClient&lt;I, T&gt;()</c>) is built by the container, and the
/// container can only build it when each of its interface-typed <c>Index.*</c> constructor parameters is
/// registered too. A missing one is not a build error and not a test failure anywhere else: it is an
/// exception the first time the type is resolved on the phone — inside a background worker, a failure
/// every cycle that the worker's own best-effort catch reports as a finished run.
///
/// <para>The head is <c>net10.0-android</c> and cannot be loaded here, so <c>MauiProgram.cs</c> is read by
/// content; the registered types are reflected from the Infrastructure assembly. A type registered through
/// a factory lambda is left alone, since the factory chooses its own arguments.</para>
/// </summary>
public class HeadRegistrationsResolveTests
{
    private static readonly Regex ByType = new(
        @"\.Add(?:Singleton|Scoped|Transient|HttpClient)<\s*(?:[\w.]+\s*,\s*)?([\w.]+)\s*>\(\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex Registered = new(
        @"\.Add(?:Singleton|Scoped|Transient|HttpClient)<\s*([\w.]+)\s*[,>]",
        RegexOptions.Compiled);

    [Fact]
    public void EveryInterfaceAnInfrastructureRegistrationNeeds_IsRegisteredInTheHead()
    {
        var source = MauiProgramSource();
        var registered = Registered.Matches(source).Select(m => Simple(m.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);
        var infrastructure = typeof(WorksheetPlanBuilder).Assembly;
        var checkedTypes = 0;
        var missing = new List<string>();

        foreach (var name in ByType.Matches(source).Select(m => Simple(m.Groups[1].Value)).Distinct())
        {
            var type = infrastructure.GetTypes().SingleOrDefault(t => t.Name == name && t.Namespace?.StartsWith("Index.", StringComparison.Ordinal) == true);
            if (type is null)
            {
                continue; // a head, kit or library type: not this assembly's to reflect
            }

            checkedTypes++;
            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            var gaps = constructors.Select(c => Unregistered(c, registered)).ToList();
            if (gaps.Count > 0 && gaps.All(g => g.Count > 0))
            {
                missing.Add($"{type.Name} needs {string.Join(", ", gaps.OrderBy(g => g.Count).First())}");
            }
        }

        Assert.True(checkedTypes > 0, "No Infrastructure registration was found in MauiProgram.cs; the pattern has drifted.");
        Assert.True(missing.Count == 0, "Registered in MauiProgram.cs but not constructible: " + string.Join("; ", missing));
    }

    private static List<string> Unregistered(ConstructorInfo constructor, HashSet<string> registered) =>
        constructor.GetParameters()
            .Where(p => !p.IsOptional)
            .Select(p => p.ParameterType)
            .Where(t => t.IsInterface && t.Namespace?.StartsWith("Index.", StringComparison.Ordinal) == true)
            .Where(t => !registered.Contains(t.Name))
            .Select(t => t.Name)
            .ToList();

    private static string Simple(string name) => name[(name.LastIndexOf('.') + 1)..];

    private static string MauiProgramSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Ledger", "Ledger.Android", "MauiProgram.cs"));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
