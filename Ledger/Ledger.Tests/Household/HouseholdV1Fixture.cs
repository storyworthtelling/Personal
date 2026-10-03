namespace Index.Tests.Household;

/// <summary>
/// R-G28's load-bearing fixture: the household file EXACTLY as the original (version-less) serialiser wrote
/// it — captured by running the pre-3.6 <c>HouseholdSnapshot</c> through <c>VaultEnvelope.SerializerOptions</c>
/// BEFORE the codec was touched, then pasted here verbatim. It is deliberately NOT built from the current
/// types: a fixture produced by the new code would pass whatever the new code did. Note what it does not
/// have — no "Version", no history, no growth. That absence IS the contract: absent means version 1.
/// The owner's phone may have published this shape months ago and her phone must still read it.
/// </summary>
internal static class HouseholdV1Fixture
{
    public const string Json = """
{
  "PublishedAtUtc": "2026-09-25T14:30:00+00:00",
  "PublishedByAccountEmail": "owner@example.com",
  "Institutions": [
    {
      "Name": "Redwood Bank",
      "IsStale": false,
      "AsOfUtc": "2026-09-25T12:30:00+00:00",
      "Accounts": [
        {
          "Name": "Everyday Checking",
          "Mask": "7301",
          "Kind": 0,
          "CurrentBalance": 3150.25,
          "CreditLimit": null,
          "NextPaymentDueDate": null,
          "NextPaymentAmount": null,
          "IsoCurrencyCode": "USD"
        },
        {
          "Name": "Bluebird Visa",
          "Mask": "7306",
          "Kind": 1,
          "CurrentBalance": 4816.25,
          "CreditLimit": 6000,
          "NextPaymentDueDate": "2026-10-09T00:00:00+00:00",
          "NextPaymentAmount": 85,
          "IsoCurrencyCode": "USD"
        }
      ]
    },
    {
      "Name": "Meridian Card",
      "IsStale": true,
      "AsOfUtc": "2026-09-23T08:00:00+00:00",
      "Accounts": [
        {
          "Name": "Meridian Plus",
          "Mask": "7305",
          "Kind": 1,
          "CurrentBalance": 5908.30,
          "CreditLimit": 6500,
          "NextPaymentDueDate": "2026-10-12T00:00:00+00:00",
          "NextPaymentAmount": 110,
          "IsoCurrencyCode": "USD"
        }
      ]
    }
  ],
  "Snowball": {
    "DebtFreeDate": "2031-09-26",
    "TotalWeeks": 236,
    "TotalOwed": 280842.11,
    "ExtraPaymentAmount": 0,
    "LoansInDisplayOrder": [
      {
        "Name": "Redwood Bank - Bluebird",
        "DisplayNumber": 1,
        "IsFocus": true,
        "IsPaidOff": false,
        "Balance": 7412.36,
        "Apr": 0.2650,
        "WeeklyPaymentNow": 465,
        "PayoffDate": "2027-07-16",
        "FocusedWeeklyPayment": 465,
        "WeeksBeforeFocused": 0,
        "AmountBeforeFocused": 7412.36,
        "WeeksToPayoff": 17
      },
      {
        "Name": "Maple Ridge Mortgage",
        "DisplayNumber": null,
        "IsFocus": false,
        "IsPaidOff": true,
        "Balance": 0,
        "Apr": 0.0425,
        "WeeklyPaymentNow": 0,
        "PayoffDate": "2026-09-25",
        "FocusedWeeklyPayment": 0,
        "WeeksBeforeFocused": 0,
        "AmountBeforeFocused": 0,
        "WeeksToPayoff": 0
      }
    ]
  },
  "Budget": {
    "YearMonth": "2026-09",
    "TrackingOnly": false,
    "Categories": [
      {
        "Name": "Groceries",
        "LimitAmount": 400,
        "ActualAmount": 185.20,
        "TrackingOnly": false,
        "LimitUnreadable": false
      }
    ]
  },
  "Savings": {
    "TotalSaved": 9550,
    "Accounts": [
      {
        "InstitutionName": "Redwood Bank",
        "AccountName": "Savings",
        "Balance": 8200,
        "GoalTargetAmount": 15000,
        "MonthlyPace": 187.75,
        "MonthsOfHistory": 14
      }
    ]
  }
}
""";
}
