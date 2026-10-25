using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Tests.RecipeDocuments;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.RecipeRating;

/// <summary>
/// C-2.5.5: the recipe reaches the cook's tablet an hour before the cook block, and never once the block has begun.
/// Expected instants are written out with their UTC offsets; the clock is the invented US-Eastern zone.
/// </summary>
public class RecipeDeliveryTests
{
    private static readonly DateTimeOffset Block = Est(11, 17, 17, 15);

    [Fact]
    public void AnHourBeforeTheBlock()
    {
        var slot = RecipeDelivery.Slot(Block, Est(11, 17, 8));

        Assert.Equal(RecipeSend.Later, slot.Send);
        Assert.Equal(Est(11, 17, 16, 15), slot.At);
        Assert.Equal(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(15), slot.Delay);
    }

    [Fact]
    public void TheHourIsAnHourOfElapsedTimeAcrossAClockChange()
    {
        // 3:30 am is the first half hour after the clocks go forward on 8 March; an hour earlier is 1:30 am, before the gap.
        var spring = RecipeDelivery.Slot(Edt(3, 8, 3, 30), Est(3, 7, 20));
        Assert.Equal(Est(3, 8, 1, 30), spring.At);
        Assert.Equal(TimeSpan.FromHours(5.5), spring.Delay);

        // 1:30 am the second time round on 1 November (UTC-5); an hour earlier is the first 1:30 am (UTC-4).
        var fall = RecipeDelivery.Slot(Est(11, 1, 1, 30), Edt(10, 31, 20));
        Assert.Equal(Edt(11, 1, 1, 30), fall.At);
        Assert.Equal(TimeSpan.FromHours(5.5), fall.Delay);
    }

    [Fact]
    public void ExactlyAnHourBeforeIsNow()
    {
        var slot = RecipeDelivery.Slot(Block, Block - RecipeDelivery.Lead);

        Assert.Equal(RecipeSend.Now, slot.Send);
        Assert.Equal(TimeSpan.Zero, slot.Delay);
    }

    [Fact]
    public void LateButBeforeTheBlockSendsNow()
    {
        var slot = RecipeDelivery.Slot(Block, Est(11, 17, 16, 50));

        Assert.Equal(RecipeSend.Now, slot.Send);
        Assert.Equal(Est(11, 17, 16, 50), slot.At);
        Assert.Equal(TimeSpan.Zero, slot.Delay);
        Assert.Equal(RecipeSend.Now, RecipeDelivery.Slot(Block, Block.AddSeconds(-1)).Send);
    }

    [Fact]
    public void NeverOnceTheBlockHasBegun()
    {
        Assert.Equal(RecipeSend.Never, RecipeDelivery.Slot(Block, Block).Send);
        Assert.Equal(RecipeSend.Never, RecipeDelivery.Slot(Block, Est(11, 17, 17, 40)).Send);
        Assert.Null(RecipeDelivery.Slot(Block, Est(11, 17, 17, 40)).At);
    }

    [Fact]
    public void NeverAfterTheDinnerWhateverTheBlockSays()
    {
        var dinner = Est(11, 17, 18, 15);

        Assert.Equal(RecipeSend.Never, RecipeDelivery.Slot(Block, Est(11, 17, 18, 15), dinner).Send);
        Assert.Equal(RecipeSend.Never, RecipeDelivery.Slot(Block, Est(11, 17, 19), dinner).Send);
        Assert.Equal(RecipeSend.Never, RecipeDelivery.Slot(Est(11, 17, 18, 40), Est(11, 17, 18, 20), dinner).Send);
        Assert.Equal(RecipeSend.Later, RecipeDelivery.Slot(Block, Est(11, 17, 8), dinner).Send);
    }

    [Fact]
    public void AnHourBeforeTheBlockForTheBooksChicken()
    {
        var model = RecipeSamples.Timed();

        // The book's steps run 60 minutes back from dinner at 6:15, so the cook block starts at 5:15.
        Assert.Equal(Est(11, 17, 17, 15), RecipeDelivery.CookBlockStart(model, Zone));
    }

    [Fact]
    public void TheBlockIsWhereTheStepsPutItNotWhereTheModelSaysItStarts()
    {
        // A longer card: the steps add up to 90 minutes, so the first begins at 4:45 whatever StartAt says.
        var longer = RecipeSamples.Timed(steps: [.. RecipeSamples.TimedSteps(), new("Let it rest.", 30, Active: false)]) with { StartAt = RecipeSamples.Tuesday.AddHours(17).AddMinutes(15) };

        Assert.Equal(Est(11, 17, 15, 45), RecipeDelivery.Slot(RecipeDelivery.CookBlockStart(longer, Zone), Est(11, 17, 8)).At);
        Assert.Equal(Est(11, 17, 16, 45), RecipeDelivery.CookBlockStart(longer, Zone));
    }

    [Fact]
    public void SlowCookerArrivesBeforeTheMorningBlock()
    {
        var model = RecipeSamples.SlowCooker();
        var start = RecipeDelivery.CookBlockStart(model, Zone);

        Assert.Equal(Est(11, 18, 7, 30), start);
        Assert.Equal(Est(11, 18, 6, 30), RecipeDelivery.Slot(start, Est(11, 17, 20)).At);

        // Past the morning block the recipe is not sent, though dinner is hours off.
        Assert.Equal(RecipeSend.Never, RecipeDelivery.Slot(start, Est(11, 18, 9)).Send);
    }

    [Fact]
    public void ABlockOnAClockChangeDayIsFoundOnTheOwnersClock()
    {
        var model = RecipeSamples.Timed() with
        {
            Day = new DateTime(2026, 3, 8), DinnerAt = new DateTime(2026, 3, 8, 18, 15, 0), StartAt = new DateTime(2026, 3, 8, 17, 15, 0),
        };

        // After the clocks went forward the evening is on daylight time.
        Assert.Equal(Edt(3, 8, 17, 15), RecipeDelivery.CookBlockStart(model, Zone));
        Assert.Equal(Edt(3, 8, 16, 15), RecipeDelivery.Slot(RecipeDelivery.CookBlockStart(model, Zone), Est(3, 7, 12)).At);
    }

    [Fact]
    public void OnlyTheCooksTablet()
    {
        var plan = RecipeDelivery.Plan("Lee", ["Sam", "Lee"], RecipeSamples.Timed(), Zone, Est(11, 17, 8));

        Assert.Equal(["Lee"], plan.Tablets);
        Assert.Equal(RecipeSend.Later, plan.Slot.Send);
        Assert.Equal(Est(11, 17, 16, 15), plan.Slot.At);
        Assert.Equal(["Sam"], RecipeDelivery.Plan("Sam", ["Sam", "Lee"], RecipeSamples.Timed(), Zone, Est(11, 17, 8)).Tablets);
    }

    [Fact]
    public void ACookWhoIsNotAnAdultOnTheRosterHasNoTabletToSendTo()
    {
        Assert.Throws<ArgumentException>(() => RecipeDelivery.Plan("Robin", ["Sam", "Lee"], RecipeSamples.Timed(), Zone, Est(11, 17, 8)));
        Assert.Throws<ArgumentException>(() => RecipeDelivery.Plan(" ", ["Sam", "Lee"], RecipeSamples.Timed(), Zone, Est(11, 17, 8)));
    }

    [Fact]
    public void TheCooksNameIsFoundWhateverItsCase()
    {
        Assert.Equal(["Lee"], RecipeDelivery.Plan("lee", ["Sam", "Lee"], RecipeSamples.Timed(), Zone, Est(11, 17, 8)).Tablets);
    }

    [Fact]
    public void TheRecipeIsNamedByItsOwnKindAndDay()
    {
        Assert.Equal("Constitution — Recipe — 2026-11-17 — Sheet-pan chicken", DeliveredName.For(DeliveryKind.Recipe, new DateOnly(2026, 11, 17), "Sheet-pan chicken"));
        Assert.Equal("Recipe", DeliveryKind.Recipe.ToString());
    }
}
