using System.Reflection;
using System.Text.RegularExpressions;
using Constitution.Domain.Diagnostics;

namespace Constitution.Tests.Groceries;

/// <summary>
/// The grocery budget is real money. It is shown on the sheet and nowhere else: no diagnostics or trail type has a member that can
/// hold an amount, and none of their source mentions the budget or the grocery types. Groceries are also exact (no double or float).
/// </summary>
public class NoMoneyInTheTrailTests
{
    private const BindingFlags AllInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly Regex Approximate = new(@"\b(double|float)\b|Math\.(Pow|Round)\(|\(double\)|\(float\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Mentions = new(@"\bGrocery\w*|\bBudgetEntry\b|\bMonthlyBudgets\b|\bBudgetPayload\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static IEnumerable<Type> DiagnosticTypes() =>
        typeof(DiagnosticsSnapshot).Assembly.GetTypes()
            .Where(type => type.Namespace is { } space && (space == "Constitution.Domain.Diagnostics" || space.StartsWith("Constitution.Domain.Diagnostics.", StringComparison.Ordinal)));

    internal static IReadOnlyList<string> MoneyMembers(IEnumerable<Type> types)
    {
        var offences = new List<string>();
        foreach (var type in types)
        {
            foreach (var field in type.GetFields(AllInstance))
            {
                Check(type, field.Name, field.FieldType, offences);
            }

            foreach (var property in type.GetProperties(AllInstance))
            {
                Check(type, property.Name, property.PropertyType, offences);
            }

            foreach (var constructor in type.GetConstructors(AllInstance))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    Check(type, parameter.Name ?? "?", parameter.ParameterType, offences);
                }
            }
        }

        return offences;
    }

    private static void Check(Type owner, string name, Type member, List<string> offences)
    {
        var inner = Nullable.GetUnderlyingType(member) ?? (member.IsGenericType ? member.GetGenericArguments().FirstOrDefault() : null) ?? member;
        if (member == typeof(decimal) || inner == typeof(decimal))
        {
            offences.Add(owner.Name + "." + name);
        }
    }

    [Fact]
    public void NoDiagnosticsTypeHoldsAnAmount()
    {
        var types = DiagnosticTypes().ToList();

        Assert.True(types.Count >= 10, $"The guard found only {types.Count} diagnostics types; it is not reading the real namespace.");
        Assert.Empty(MoneyMembers(types));
    }

    [Fact]
    public void TheGuardSeesAnAmountWhenOneIsAdded()
    {
        Assert.Contains("LeakyForTesting.Spent", MoneyMembers([typeof(LeakyForTesting)]));
        Assert.Contains("LeakyForTesting.Maybe", MoneyMembers([typeof(LeakyForTesting)]));
        Assert.Contains("LeakyForTesting.Many", MoneyMembers([typeof(LeakyForTesting)]));
    }

    [Fact]
    public void NoDiagnosticsSourceNamesTheBudgetOrTheGroceryTypes()
    {
        var folder = Path.Combine(ConstitutionSource.Root(), "Constitution.Domain", "Diagnostics");
        var files = ConstitutionSource.Files(".cs").Where(file => file.StartsWith(folder, StringComparison.Ordinal)).ToList();

        Assert.True(files.Count >= 8, $"The guard found only {files.Count} diagnostics files.");
        var offences = files.Where(file => Mentions.IsMatch(File.ReadAllText(file))).Select(ConstitutionSource.Relative).ToList();
        Assert.Empty(offences);
    }

    [Fact]
    public void TheMentionDetectorSeesTheBudget()
    {
        Assert.Matches(Mentions, "var left = GroceryBudget.Read(budgets, month);");
        Assert.Matches(Mentions, "public BudgetEntry Entry { get; }");
    }

    [Fact]
    public void GroceriesAreExactDecimalsNeverApproximate()
    {
        var folder = Path.Combine(ConstitutionSource.Root(), "Constitution.Domain", "Groceries");
        var files = ConstitutionSource.Files(".cs").Where(file => file.StartsWith(folder, StringComparison.Ordinal)).ToList();

        Assert.True(files.Count >= 6, $"The guard found only {files.Count} grocery files.");
        var offences = files
            .Where(file => Approximate.IsMatch(StripComments(File.ReadAllText(file))))
            .Select(ConstitutionSource.Relative)
            .ToList();
        Assert.Empty(offences);
    }

    [Fact]
    public void TheApproximateDetectorSeesADouble()
    {
        Assert.Matches(Approximate, "var share = (double)spent / budget;");
        Assert.Matches(Approximate, "float total = 0;");
        Assert.DoesNotMatch(Approximate, StripComments("decimal total = 0m; // not a double of anything"));
    }

    private static string StripComments(string source) =>
        Regex.Replace(source, @"//[^\r\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private sealed class LeakyForTesting
    {
        public decimal Spent { get; init; }

        public decimal? Maybe { get; init; }

        public IReadOnlyList<decimal> Many { get; init; } = [];
    }
}
