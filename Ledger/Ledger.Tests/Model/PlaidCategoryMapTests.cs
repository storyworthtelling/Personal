using Index.Domain.Model;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Model;

public class PlaidCategoryMapTests
{
    [Theory]
    [InlineData("FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES", "Groceries")]
    [InlineData("FOOD_AND_DRINK", "FOOD_AND_DRINK_RESTAURANT", "Dining out")]
    [InlineData("FOOD_AND_DRINK", "FOOD_AND_DRINK_FAST_FOOD", "Dining out")]
    [InlineData("FOOD_AND_DRINK", "FOOD_AND_DRINK_COFFEE", "Dining out")]
    [InlineData("TRANSPORTATION", "TRANSPORTATION_GAS", "Fuel")]
    [InlineData("HOME_IMPROVEMENT", "HOME_IMPROVEMENT_HARDWARE", "Household")]
    [InlineData("HOME_IMPROVEMENT", null, "Household")]
    public void MapsAPlaidCategoryOntoASeededSpendingCategory(string primary, string? detailed, string expected)
    {
        var decision = PlaidCategoryMap.Decide(primary, detailed);

        Assert.Equal(PlaidCategoryKind.Category, decision.Kind);
        Assert.Equal(expected, decision.CategoryName);
    }

    [Theory]
    [InlineData("INCOME", "INCOME_WAGES")]
    [InlineData("TRANSFER_IN", "TRANSFER_IN_ACCOUNT_TRANSFER")]
    [InlineData("TRANSFER_OUT", "TRANSFER_OUT_SAVINGS")]
    [InlineData("LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT")]
    [InlineData("LOAN_PAYMENTS", "LOAN_PAYMENTS_MORTGAGE_PAYMENT")]
    [InlineData("INCOME", null)]
    public void IncomeTransfersAndLoanAndCardPaymentsAreNotSpending(string primary, string? detailed)
    {
        var decision = PlaidCategoryMap.Decide(primary, detailed);

        Assert.Equal(PlaidCategoryKind.NotSpending, decision.Kind);
        Assert.Null(decision.CategoryName);
        Assert.True(PlaidCategoryMap.IsNotSpending(primary, detailed));
    }

    [Theory]
    [InlineData("GENERAL_MERCHANDISE", "GENERAL_MERCHANDISE_SUPERSTORES")]
    [InlineData("MEDICAL", "MEDICAL_PHARMACIES_AND_SUPPLEMENTS")]
    [InlineData("SOMETHING_PLAID_ADDS_LATER", "SOMETHING_PLAID_ADDS_LATER_OTHER")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void AnythingElseIsUnmapped_AndStillCountsAsSpending(string? primary, string? detailed)
    {
        var decision = PlaidCategoryMap.Decide(primary, detailed);

        Assert.Equal(PlaidCategoryKind.Unmapped, decision.Kind);
        Assert.Null(decision.CategoryName);
        Assert.False(PlaidCategoryMap.IsNotSpending(primary, detailed));
    }

    [Fact]
    public void ADetailedRowBeatsItsPrimary()
    {
        // FOOD_AND_DRINK as a whole is not mapped, but its GROCERIES detail is.
        Assert.Equal(PlaidCategoryKind.Unmapped, PlaidCategoryMap.Decide("FOOD_AND_DRINK", "FOOD_AND_DRINK_VENDING_MACHINES").Kind);
        Assert.Equal(PlaidCategoryKind.Category, PlaidCategoryMap.Decide("FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES").Kind);
    }

    [Fact]
    public void MatchingIgnoresCase()
    {
        Assert.Equal("Fuel", PlaidCategoryMap.Decide("transportation", "transportation_gas").CategoryName);
    }

    [Fact]
    public void EveryTargetIsOneOfTheStarterCategoriesTheAppSeeds()
    {
        // The map names categories; a name that is not one the app seeds could never find a card.
        var settings = File.ReadAllText(Path.Combine(DesignTestHelpers.LedgerRoot(), "Ledger.Android/SettingsStore.cs"));
        foreach (var name in PlaidCategoryMap.TargetCategoryNames)
        {
            Assert.Contains($"\"{name}\"", settings, StringComparison.Ordinal);
        }
    }
}
