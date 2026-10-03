using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Remediation 3.8 (mockup 4): Kind decides the Add form. Before this, every Kind showed the identical seven
/// fields, a hand-entered card could never carry a limit, and an account saved with no payment still got
/// "Month" because the segmented control always has an option picked.
/// </summary>
public class ManualAccountFormTests
{
    private const ManualField Always = ManualField.Name | ManualField.Bank | ManualField.Kind | ManualField.Balance;

    // ---- R-G30: the field set per Kind ----

    [Fact]
    public void ALoanIsAsDrawn_NoLimit()
    {
        Assert.Equal(
            Always | ManualField.Apr | ManualField.Payment | ManualField.Every | ManualField.NextDue,
            ManualAccountForm.FieldsFor(AccountKind.Loan));
    }

    [Fact]
    public void ACardAddsItsLimitAndKeepsTheRest()
    {
        Assert.Equal(
            Always | ManualField.CreditLimit | ManualField.Apr | ManualField.Payment | ManualField.Every | ManualField.NextDue,
            ManualAccountForm.FieldsFor(AccountKind.Credit));
    }

    [Fact]
    public void ABankAccountNeedsNoPayment_ItHidesAprPaymentEveryAndNextDue()
    {
        Assert.Equal(Always, ManualAccountForm.FieldsFor(AccountKind.Depository));
    }

    [Theory]
    [InlineData("Loan", AccountKind.Loan)]
    [InlineData("Card", AccountKind.Credit)]
    [InlineData("Bank account", AccountKind.Depository)]
    public void TheSegmentedOptionsMapToTheirKinds(string option, AccountKind kind)
    {
        Assert.Equal(kind, ManualAccountForm.KindOf(option));
    }

    // ---- R-G31: a hidden field's value is never saved ----

    private static readonly DateTimeOffset Due = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ABankAccountSavesNoAprPaymentLimitCadenceOrDueDateEvenIfTypedBeforeSwitchingKind()
    {
        var saved = ManualAccountForm.Resolve(
            AccountKind.Depository, apr: 6.25m, payment: 250m, creditLimit: 5000m, PaymentCadence.Week, Due);

        Assert.Equal(new ManualAccountValues(null, null, null, null, null), saved);
    }

    [Fact]
    public void ALoanSavesNoLimitEvenIfOneWasTypedWhileItWasACard()
    {
        var saved = ManualAccountForm.Resolve(
            AccountKind.Loan, apr: 6.25m, payment: 250m, creditLimit: 5000m, PaymentCadence.Month, Due);

        Assert.Null(saved.CreditLimit);
        Assert.Equal(6.25m, saved.Apr);
        Assert.Equal(250m, saved.Payment);
        Assert.Equal(PaymentCadence.Month, saved.Cadence);
        Assert.Equal(Due, saved.NextDue);
    }

    [Fact]
    public void ACardKeepsEverything()
    {
        var saved = ManualAccountForm.Resolve(
            AccountKind.Credit, apr: 22.49m, payment: 35m, creditLimit: 5000m, PaymentCadence.Month, Due);

        Assert.Equal(new ManualAccountValues(22.49m, 35m, 5000m, PaymentCadence.Month, Due), saved);
    }

    // ---- R-G32: a cadence nobody chose is not stored ----

    [Fact]
    public void WithNoPaymentAndEveryUntouched_NoCadenceIsStored()
    {
        Assert.Null(ManualAccountForm.CadenceToStore(payment: null, touched: false, PaymentCadence.Month));
    }

    [Fact]
    public void WithNoPaymentButEveryTapped_TheChoiceIsStored()
    {
        Assert.Equal(PaymentCadence.Week, ManualAccountForm.CadenceToStore(payment: null, touched: true, PaymentCadence.Week));
    }

    [Theory]
    [InlineData(PaymentCadence.Month)]
    [InlineData(PaymentCadence.Week)]
    public void WithAPayment_TheShownCadenceIsStoredEvenIfNeverTapped(PaymentCadence shown)
    {
        Assert.Equal(shown, ManualAccountForm.CadenceToStore(payment: 250m, touched: false, shown));
    }

    // ---- R-G33: the examples are placeholders, and they are in the shape a blur writes ----

    [Theory]
    [InlineData(ManualAccountForm.BalanceExample, FigureKind.Money, true)]
    [InlineData(ManualAccountForm.LimitExample, FigureKind.Money, false)]
    [InlineData(ManualAccountForm.PaymentExample, FigureKind.Money, false)]
    [InlineData(ManualAccountForm.AprExample, FigureKind.Percent, false)]
    public void EachExampleIsAReadableFigureWrittenBackExactlyAsItIsShown(string example, FigureKind kind, bool allowNegative)
    {
        var parsed = FigureParser.Parse(example, kind, allowNegative);

        Assert.Equal(FigureStatus.Valid, parsed.Status);
        Assert.Equal(example, FigureParser.FormatForBox(parsed.Value, kind));
    }

    [Fact]
    public void TheExamplesAreObviouslyInventedOnes_NotAnyRealAccount()
    {
        // The Add form ships in a public repository: its placeholder hints must read as made up. The bank is
        // the word "Example" itself, and the figures are round and unrelated to anything the owner holds.
        Assert.Contains("Example", ManualAccountForm.BankExample, StringComparison.Ordinal);
        Assert.Equal("Car loan", ManualAccountForm.NameExample);
        Assert.Equal("$12,345.67", ManualAccountForm.BalanceExample);
        Assert.Equal("$5,000.00", ManualAccountForm.LimitExample);
        Assert.Equal("6.25%", ManualAccountForm.AprExample);
        Assert.Equal("$250.00", ManualAccountForm.PaymentExample);
    }
}
