using Constitution.Tests.Diagnostics;

namespace Constitution.Tests.Contracts;

/// <summary>D163: a failed budget broadcast is logged by exception type name only. A whole exception's text can carry a path or a value, and this receiver handles money.</summary>
public class BudgetReceiverLogTests
{
    private static string Receiver() =>
        File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => file.EndsWith("Budgets/BudgetReceiver.cs", StringComparison.Ordinal)));

    [Fact]
    public void OnlyTheTypeNameIsLogged()
    {
        var source = Receiver();

        Assert.DoesNotContain("e.ToString()", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Message", source, StringComparison.Ordinal);
        Assert.Contains("Log.Error(\"Budget\", e.GetType().Name)", source, StringComparison.Ordinal);
    }
}
