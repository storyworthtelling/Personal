using Constitution.Domain.Delivery;
using Constitution.Domain.Meals.Pipeline;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.MealWeekDocuments;

/// <summary>When a meal document is sent, replaced or left alone: only before its slot, and an unchanged page is never sent twice.</summary>
public class MealRedeliveryRulesTests
{
    private static readonly DateTimeOffset Slot = Edt(10, 25, 17, 15);

    private static DeliveredMealDocument Sent(string fingerprint = "aaa", DateTimeOffset? slot = null) =>
        new("name", DeliveryKind.Recipe, Sam, Sunday, slot ?? Slot, fingerprint);

    private static CurrentMealDocument Now(string fingerprint = "aaa", DateTimeOffset? slot = null, DateTimeOffset? from = null) =>
        new(fingerprint, slot ?? Slot, from);

    [Fact]
    public void ANewDocumentBeforeItsSlotIsSent() =>
        Assert.Equal(MealRedelivery.Send, MealRedeliveryRules.Decide(null, Now(), Slot.AddMinutes(-1)));

    [Fact]
    public void ANewDocumentAtItsSlotIsTooLate() =>
        Assert.Equal(MealRedelivery.TooLate, MealRedeliveryRules.Decide(null, Now(), Slot));

    [Fact]
    public void ANewDocumentAfterItsSlotIsTooLate() =>
        Assert.Equal(MealRedelivery.TooLate, MealRedeliveryRules.Decide(null, Now(), Slot.AddHours(3)));

    [Fact]
    public void ANewRecipeBeforeItsHourWaits() =>
        Assert.Equal(MealRedelivery.NotYet, MealRedeliveryRules.Decide(null, Now(from: Slot.AddHours(-1)), Slot.AddMinutes(-61)));

    [Fact]
    public void ANewRecipeAtItsHourIsSent() =>
        Assert.Equal(MealRedelivery.Send, MealRedeliveryRules.Decide(null, Now(from: Slot.AddHours(-1)), Slot.AddHours(-1)));

    [Fact]
    public void AnUnchangedPageIsLeftAloneAtAnyTime()
    {
        Assert.Equal(MealRedelivery.LeaveAlone, MealRedeliveryRules.Decide(Sent(), Now(), Slot.AddMinutes(-30)));
        Assert.Equal(MealRedelivery.LeaveAlone, MealRedeliveryRules.Decide(Sent(), Now(), Slot.AddHours(5)));
    }

    [Fact]
    public void AChangedPageBeforeTheSlotIsReplaced() =>
        Assert.Equal(MealRedelivery.Replace, MealRedeliveryRules.Decide(Sent(), Now("bbb"), Slot.AddMinutes(-1)));

    [Fact]
    public void AChangedPageOnceTheSlotHasBegunIsNeverSent()
    {
        Assert.Equal(MealRedelivery.TooLate, MealRedeliveryRules.Decide(Sent(), Now("bbb"), Slot));
        Assert.Equal(MealRedelivery.TooLate, MealRedeliveryRules.Decide(Sent(), Now("bbb"), Slot.AddHours(1)));
    }

    [Fact]
    public void TheEarlierOfTheTwoSlotsDecides()
    {
        // The paper on the tablet names the earlier start; the owner reads the paper.
        var earlier = Sent(slot: Slot.AddHours(-1));

        Assert.Equal(MealRedelivery.TooLate, MealRedeliveryRules.Decide(earlier, Now("bbb"), Slot.AddMinutes(-30)));
        Assert.Equal(MealRedelivery.Replace, MealRedeliveryRules.Decide(earlier, Now("bbb"), Slot.AddHours(-2)));
    }

    [Fact]
    public void TheDecisionAgreesWithTheRecipeDeliveryRule()
    {
        for (var minutes = -300; minutes <= 60; minutes += 7)
        {
            var now = Slot.AddMinutes(minutes);
            var slot = RecipeDelivery.Slot(Slot, now);
            var decided = MealRedeliveryRules.Decide(null, Now(from: Slot - RecipeDelivery.Lead), now);

            var expected = slot.Send switch
            {
                RecipeSend.Never => MealRedelivery.TooLate,
                RecipeSend.Later => MealRedelivery.NotYet,
                _ => MealRedelivery.Send,
            };
            Assert.Equal(expected, decided);
        }
    }
}
