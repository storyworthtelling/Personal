using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

// D124: paid-off accounts disappear from Accounts entirely.
public class AccountStatusTests
{
    private static Account Make(AccountKind kind, decimal? balance) =>
        new() { Kind = kind, CurrentBalance = balance };

    [Fact]
    public void CreditCardAtZero_IsPaidOff()
    {
        Assert.True(AccountStatus.IsPaidOff(Make(AccountKind.Credit, 0m)));
    }

    [Fact]
    public void LoanAtZero_IsPaidOff()
    {
        Assert.True(AccountStatus.IsPaidOff(Make(AccountKind.Loan, 0m)));
    }

    [Fact]
    public void CreditCardWithABalance_IsNotPaidOff()
    {
        Assert.False(AccountStatus.IsPaidOff(Make(AccountKind.Credit, 12.50m)));
    }

    [Fact]
    public void DepositoryAccountAtZero_IsNotPaidOff()
    {
        // A $0 checking account is still an account you hold, not something you finished paying off.
        Assert.False(AccountStatus.IsPaidOff(Make(AccountKind.Depository, 0m)));
    }

    [Fact]
    public void InvestmentAccountAtZero_IsNotPaidOff()
    {
        Assert.False(AccountStatus.IsPaidOff(Make(AccountKind.Investment, 0m)));
    }

    [Fact]
    public void CreditCardWithNoBalanceYet_IsNotPaidOff()
    {
        // Null means "not refreshed yet", not "settled" — treating it as paid off would hide a card
        // whose balance simply hasn't come back yet.
        Assert.False(AccountStatus.IsPaidOff(Make(AccountKind.Credit, null)));
    }
}
