using Pulp.Domain.Core;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Library;
using Pulp.Domain.Services;
using Pulp.Domain.Sports;
using static Pulp.Tests.HandOff.HandOffFixtures;

namespace Pulp.Tests.Sports;

/// <summary>
/// What a game's Play does (D311, G8): resolve the channel to a ticked service and go through the one door,
/// or keep the honest card and name what to tick. A played game is never marked watched by the hand-off.
/// </summary>
public class GamePlayTests
{
    private static readonly GameId Game = new("g-501");
    private static readonly ServiceId Peacock = new("peacock");

    private static GamePlayDecision Decide(string? channel, params ServiceId[] ticked) =>
        GamePlay.Decide(channel, ticked, BroadcasterTable.Public, ServiceCatalogue.Public);

    [Fact]
    public void ATickedBroadcasterOpensItsServicesHomeThroughTheDoor()
    {
        var decision = Decide("NBC · Peacock", Peacock);

        Assert.True(decision.Opens);
        Assert.Equal(Peacock, decision.Service);
        Assert.Null(decision.Note);
        Assert.Equal(Uri.UriSchemeHttps, decision.Home!.Scheme);
    }

    [Fact]
    public void AnUntickedBroadcasterKeepsTheCardAndNamesTheServiceToTick()
    {
        var decision = Decide("Peacock", new ServiceId("netflix"));

        Assert.False(decision.Opens);
        Assert.Contains("Peacock", decision.Note, StringComparison.Ordinal);
        Assert.Contains("Settings", decision.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Gridline TV · channel 7")]
    [InlineData(null)]
    public void AnUnknownBroadcasterKeepsTheHonestCardAndNeverOpensAnything(string? channel)
    {
        var decision = Decide(channel, Peacock);

        Assert.False(decision.Opens);
        Assert.Equal(SportsScreenText.PlayNote(SportsScreenText.ChannelLine(channel)), decision.Note);
    }

    [Fact]
    public void TheRequestForAGameGoesThroughThePlannerToTheServicesPackageForAnAdult()
    {
        var service = ServiceCatalogue.Public.ById(Peacock)!;
        var decision = Decide("Peacock", Peacock);
        var request = GamePlay.Request(Sam, Game, service, decision.Home!, "Foxes at Gulls");
        var planner = new HandOffPlanner(Roster, [WrenRules, MossRules], ServiceCatalogue.Public, new Pulp.Domain.Children.AttestedServices([]));

        var plan = planner.Plan(request, At);

        Assert.NotNull(plan.Intent);
        Assert.Equal(service.PrimaryPackage, plan.Intent.Package);
        Assert.Equal(decision.Home, plan.Intent.Data);
        Assert.Equal(Peacock, plan.Pending!.Service);
    }

    [Fact]
    public void ARequestWithAChildOnTheSofaIsRefusedAtTheGate()
    {
        var service = ServiceCatalogue.Public.ById(Peacock)!;
        var request = GamePlay.Request(Wren, Game, service, BroadcasterTable.HomeLink(service)!, "Foxes at Gulls");
        var planner = new HandOffPlanner(Roster, [WrenRules, MossRules], ServiceCatalogue.Public, new Pulp.Domain.Children.AttestedServices([]));

        Assert.Null(planner.Plan(request, At).Intent);
    }

    [Fact]
    public void TheUsageSittingCarriesTheServiceAndTheMomentAsAHandOffOfAGame()
    {
        var pending = new PendingHandOff(new TitleId(Game.Value), Peacock, "com.peacocktv.peacockandroid", [Sam], [], new Pulp.Domain.Watching.HandedOff(At));

        var sitting = GamePlay.UsageSitting(Sam, Game, pending);

        Assert.Equal(Sam, sitting.Viewer);
        Assert.Equal(WatchedKind.Game, sitting.Sitting.Kind);
        Assert.Equal(WatchSource.HandOff, sitting.Sitting.Evidence);
        Assert.Equal(Peacock, sitting.Sitting.Service);
        Assert.Equal(At, sitting.Sitting.At);
        Assert.Equal(Game.Value, sitting.Sitting.Title.Value);
    }
}
