using System.Xml.Linq;
using Constitution.Domain;

namespace Constitution.Tests.Contracts;

/// <summary>
/// Constitution's half of the budget contract, read from the head's source and, once a Release build has been made, from the
/// merged manifest the phone installs. A receiver that is exported without its permission would take a budget from any app on
/// the phone, and a permission nobody declares is granted to nobody, so a signed sender would be refused: both are silent.
/// </summary>
public class BudgetManifestTests
{
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    private static string ReceiverSource()
    {
        var path = Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "Budgets", "BudgetReceiver.cs");
        Assert.True(File.Exists(path), "Constitution.Android/Budgets/BudgetReceiver.cs was not found.");
        return File.ReadAllText(path);
    }

    private static IEnumerable<string> HeadSources() =>
        ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Android/", StringComparison.Ordinal))
            .Select(File.ReadAllText);

    [Fact]
    public void TheReceiverIsRegisteredByTheFrozenNames()
    {
        var source = ReceiverSource();

        Assert.Contains("Name = ConstitutionIdentity.BudgetReceiverName", source, StringComparison.Ordinal);
        Assert.Contains("Permission = ConstitutionIdentity.BudgetPermission", source, StringComparison.Ordinal);
        Assert.Contains("[IntentFilter([ConstitutionIdentity.BudgetAction])]", source, StringComparison.Ordinal);
        Assert.Contains(": BroadcastReceiver", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ItIsExportedOnlyBecauseThePermissionProtectsIt()
    {
        var source = ReceiverSource();

        Assert.Contains("Exported = true", source, StringComparison.Ordinal);
        Assert.Contains("Permission = ConstitutionIdentity.BudgetPermission", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePermissionIsDeclaredAtSignatureLevel()
    {
        var declared = HeadSources().Where(text => text.Contains("[assembly: Permission(", StringComparison.Ordinal)).ToList();

        var one = Assert.Single(declared);
        Assert.Contains("Name = ConstitutionIdentity.BudgetPermission", one, StringComparison.Ordinal);
        Assert.Contains("ProtectionLevel = Protection.Signature", one, StringComparison.Ordinal);
    }

    [Fact]
    public void ItIsDeclaredNeverRequestedAsAUse()
    {
        // The 'no unexpected permissions' rule is about uses-permission. The app defines this one; it asks for nothing new.
        Assert.DoesNotContain(HeadSources(), text =>
            text.Contains("UsesPermission", StringComparison.Ordinal)
            && text.Contains("BudgetPermission", StringComparison.Ordinal));
        Assert.DoesNotContain(HeadSources(), text => text.Contains("RECEIVE_BUDGET", StringComparison.Ordinal));
    }

    [Fact]
    public void TheMergedManifestCarriesBoth()
    {
        var path = Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "obj", "Release", "net10.0-android", "android", "AndroidManifest.xml");
        if (!File.Exists(path))
        {
            // No Release build here: the source half above is what runs. The recipe makes one and reads this file.
            return;
        }

        var merged = XDocument.Load(path);
        var receiver = merged.Descendants("receiver")
            .SingleOrDefault(element => (string?)element.Attribute(Android + "name") == ConstitutionIdentity.BudgetReceiverName);
        Assert.True(receiver is not null, "The merged Release manifest has no budget receiver.");
        Assert.Equal("true", (string?)receiver!.Attribute(Android + "exported"));
        Assert.Equal(ConstitutionIdentity.BudgetPermission, (string?)receiver.Attribute(Android + "permission"));
        Assert.Contains(receiver.Elements("intent-filter").Elements("action"),
            action => (string?)action.Attribute(Android + "name") == ConstitutionIdentity.BudgetAction);

        var permission = merged.Descendants("permission")
            .SingleOrDefault(element => (string?)element.Attribute(Android + "name") == ConstitutionIdentity.BudgetPermission);
        Assert.True(permission is not null, "The merged Release manifest does not declare the budget permission.");
        Assert.Equal("signature", (string?)permission!.Attribute(Android + "protectionLevel"));

        Assert.DoesNotContain(merged.Descendants("uses-permission"),
            element => (string?)element.Attribute(Android + "name") == ConstitutionIdentity.BudgetPermission);
    }
}
