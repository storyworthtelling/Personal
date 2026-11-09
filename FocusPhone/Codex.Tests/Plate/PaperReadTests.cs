using Codex.Domain.Plate;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// The card and statement reads behind their own door (audit W2, 09 M3). They sat on the errand activity, which is
/// exported with no permission because the camera filters must stay open to every app, so any app on the phone could
/// start a statement read and be handed the balance the owner had just confirmed.
/// </summary>
public class PaperReadTests
{
    [Fact]
    public void TheReadsHaveTheirOwnComponentAndSignaturePermission()
    {
        Assert.Equal(PolicyContext.PlatePackage + ".PaperReadActivity", ReadRequest.PaperActivity);
        Assert.Equal(PolicyContext.PlatePackage + ".READ_PAPER", ReadRequest.PaperPermission);
        Assert.NotEqual(ReadRequest.RecipePermission, ReadRequest.PaperPermission);
    }

    [Fact]
    public void TheErrandActivityNoLongerAnswersAReadButThePaperActivityDoes()
    {
        var errand = Read("ErrandActivity.cs");
        Assert.DoesNotContain("ReadRequest.ReadCard, ReadRequest.ReadStatement", errand, StringComparison.Ordinal);

        var paper = Read("PaperReadActivity.cs");
        Assert.Contains("Name = ReadRequest.PaperActivity,", paper, StringComparison.Ordinal);
        Assert.Contains("Permission = ReadRequest.PaperPermission,", paper, StringComparison.Ordinal);
        Assert.Contains("[IntentFilter([ReadRequest.ReadCard, ReadRequest.ReadStatement], Categories = [Intent.CategoryDefault])]", paper, StringComparison.Ordinal);
        Assert.Contains(": ErrandActivity", paper, StringComparison.Ordinal);
    }

    [Fact]
    public void ACardWithANameAndBothAddressesIsOneContact()
    {
        var contact = CardContact.From("  Ada Speight ", "(503) 555-0142", "ada@harbourline.example");

        Assert.NotNull(contact);
        Assert.Equal("Ada Speight", contact!.Name);
        Assert.Equal(["(503) 555-0142", "ada@harbourline.example"], contact.Addresses);
    }

    [Fact]
    public void ACardWithNoNameIsNamedByItsFirstAddress()
    {
        Assert.Equal("ada@harbourline.example", CardContact.From(null, "", "ada@harbourline.example")!.Name);
        Assert.Equal("(503) 555-0142", CardContact.From(" ", "(503) 555-0142", null)!.Name);
    }

    [Fact]
    public void ACardWithNoWayToReachAnyoneIsNotAContact()
    {
        // A name alone would be a Rolodex row Post can do nothing with.
        Assert.Null(CardContact.From("Ada Speight", null, " "));
        Assert.Null(CardContact.From(null, null, null));
    }

    private static string Read(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "FocusPhone", "Plate.Android", file));
    }
}
