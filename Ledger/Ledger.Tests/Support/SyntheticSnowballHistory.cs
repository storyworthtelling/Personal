using System.Globalization;
using System.Text.Json;

namespace Index.Tests.Support;

/// <summary>
/// The invented Debt Snowball history every import test runs against: 70 weekly Fridays, ten fictional
/// loans, 700 rows. Read here with a plain <see cref="JsonDocument"/> and NOT with the code under test,
/// so an expectation computed from it is independent of the codec it checks.
///
/// <para>Its shape mirrors the owner's real file: a mortgage-like loan that is in the history but never
/// in the extra-payment ordering, one loan that starts part-way through (zero before it appears), one
/// that reaches zero and stays there, and one week on the mortgage that is an interpolation kept as
/// recorded. Every name comes from the fictional list; every figure was generated from a seed.</para>
/// </summary>
public static class SyntheticSnowballHistory
{
    public const string ResourceName = "Index.Tests.Snowball.synthetic-snowball-history.json";

    public const string MortgageName = "Maple Ridge Mortgage";
    public const string PaidOffName = "Northfield Student Loans 1";
    public const string StartsMidHistoryName = "Harbor Auto Finance";

    /// <summary>The mortgage's interpolated week — the midpoint of its neighbours, kept as recorded.</summary>
    public static readonly DateOnly InterpolatedWeek = new(2024, 1, 19);

    /// <summary>The first week on which <see cref="StartsMidHistoryName"/> carries a balance.</summary>
    public static readonly DateOnly MidHistoryLoanStarts = new(2023, 12, 1);

    /// <summary>The first week from which <see cref="PaidOffName"/> is at zero for good.</summary>
    public static readonly DateOnly PaidOffFrom = new(2024, 2, 23);

    private static readonly Lazy<string> text = new(ReadText);
    private static readonly Lazy<Parsed> parsed = new(() => Parse(text.Value));

    public static string Json => text.Value;

    public static IReadOnlyList<DateOnly> Weeks => parsed.Value.Weeks;

    public static IReadOnlyList<string> LoanNames => parsed.Value.Loans;

    public static IReadOnlyList<(string Loan, DateOnly Week, decimal Balance)> Rows => parsed.Value.Rows;

    public static DateOnly LastWeek => Weeks[^1];

    public static decimal BalanceOf(string loan, DateOnly week) =>
        Rows.Single(r => r.Loan == loan && r.Week == week).Balance;

    private sealed record Parsed(
        List<DateOnly> Weeks, List<string> Loans, List<(string Loan, DateOnly Week, decimal Balance)> Rows);

    private static string ReadText()
    {
        using var stream = typeof(SyntheticSnowballHistory).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The synthetic Snowball history fixture is not embedded in the test assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Parsed Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var weeks = root.GetProperty("weeks").EnumerateArray()
            .Select(w => DateOnly.ParseExact(w.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();
        var loans = root.GetProperty("loans").EnumerateArray().Select(l => l.GetString()!).ToList();
        var rows = root.GetProperty("rows").EnumerateArray()
            .Select(r => (
                r.GetProperty("loan").GetString()!,
                DateOnly.ParseExact(r.GetProperty("week").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.GetProperty("balance").GetDecimal()))
            .ToList();
        return new Parsed(weeks, loans, rows);
    }
}
