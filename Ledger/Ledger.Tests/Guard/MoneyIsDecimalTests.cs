using System.Reflection;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Guard G3 (INDEX-P11-PLAN.md §4): no <c>double</c>/<c>float</c> money member sneaks back onto
/// any Domain type. P11 is the first phase that does arithmetic with <c>Account</c>'s balances —
/// a snowball total across nine loans, or a budget that must balance to exactly $0.00 — and
/// 0.1 + 0.2 is not 0.3 in binary floating point (INDEX-P11-TECHNICAL-FINDING.md, Correction 2).
/// <c>Account</c>'s six money fields moved to <c>decimal</c> for that reason; this guard is what
/// stops a <c>double</c> from silently coming back on any current or future Domain type.
///
/// <para>One member is named as a deliberate exception, rather than exempted by namespace, so a
/// new stray <c>double</c> anywhere else in Domain still fails loudly:</para>
/// <list type="bullet">
/// <item><description><c>PlaidTransaction.Amount</c> — a Plaid wire DTO already displaying doubles
/// today in the recent-transactions panel. This task is scoped to <c>Account</c>'s existing six
/// fields only (INDEX-P11-PLAN.md, Release A); the <c>Transaction</c> entity itself (Release E)
/// is <c>decimal</c> from the day it is introduced, per the finding's §3 schema.</description></item>
/// </list>
///
/// <para>Release B (Guard G12) deleted <c>DesignTokens.Type</c> entirely — the in-app type ladder
/// moved to the shared <c>Storyworthtelling.Maui</c> kit's <c>Paper.xaml</c>, which this guard's
/// assembly scan never reaches (the kit targets a different TFM), so the seven exceptions that used
/// to name its rungs are gone rather than left to point at a type that no longer exists.</para>
///
/// <para><b>Release F adds a whole-TYPE exemption, not a member one:</b> <c>Index.Domain.Worksheet</c>'s
/// <c>WorksheetRegions</c>/<c>WorksheetBox</c> are PAGE GEOMETRY in points — the same unit
/// <c>VectorPdfSink</c>/<c>SkiaSharp</c> already measure every printed page in throughout the repo
/// (Folio's <c>PageGeometry</c>, Foreword's <c>JournalRegions</c>) — never a money figure. The guard's
/// own name and doc comment are about MONEY, not every float in Domain; a per-member exception list
/// would need roughly twenty entries for one geometry type and would grow by one every time a box is
/// added, which defeats the point of a guard that is supposed to catch a NEW mistake rather than be
/// re-approved for an old, correct pattern each time it recurs.</para>
/// </summary>
public class MoneyIsDecimalTests
{
    private static readonly (string Type, string Member)[] Exceptions =
    [
        ("Index.Domain.External.PlaidTransaction", "Amount"),
    ];

    // Whole types exempted because every one of their float/double members is page geometry (points),
    // never money — see the type's own remarks above.
    private static readonly string[] GeometryTypeExceptions =
    [
        "Index.Domain.Worksheet.WorksheetRegions",
        "Index.Domain.Worksheet.WorksheetBox",
        // Release H: the follow-up page's own geometry, the same reasoning as WorksheetRegions above —
        // points, never money.
        "Index.Domain.Worksheet.FollowUpRegions",
        // Remediation 2.1: the result types of those two geometry classes' layout functions. Their
        // floats are Y positions in points (where a block starts on a sheet), never money.
        "Index.Domain.Worksheet.FollowUpLayout",
        "Index.Domain.Worksheet.WorksheetPageLayout",
        // Phase 3.1: the result types of SmallMultipleLayout — a panel's box and a touch's position
        // within it, in the chart's own device-independent units. Geometry, never money.
        "Index.Domain.Model.PanelRect",
        "Index.Domain.Model.PanelHit",
        // Phase 4: layout measures in dp (card gaps, the well's padding, a post-it's margin) — geometry,
        // never money.
        "Index.Domain.Design.PlanSpacing",
        "Index.Domain.Design.PlanInset",
    ];

    [Fact]
    public void NoDoubleOrFloatMemberAnywhereInDomain()
    {
        var assembly = typeof(Index.Domain.Model.Account).Assembly;
        var offenders = new List<string>();

        foreach (var type in assembly.GetTypes())
        {
            if (GeometryTypeExceptions.Contains(type.FullName))
                continue;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsDoubleOrFloat(property.PropertyType) && !IsException(type, property.Name))
                    offenders.Add($"{type.FullName}.{property.Name} ({property.PropertyType.Name})");
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsDoubleOrFloat(field.FieldType) && !IsException(type, field.Name))
                    offenders.Add($"{type.FullName}.{field.Name} ({field.FieldType.Name})");
            }
        }

        Assert.True(offenders.Count == 0,
            "double/float member(s) found where Index.Domain should be decimal:\n" + string.Join("\n", offenders));
    }

    private static bool IsDoubleOrFloat(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(double) || underlying == typeof(float);
    }

    private static bool IsException(Type type, string memberName) =>
        Exceptions.Any(e => e.Type == (type.FullName ?? type.Name) && e.Member == memberName);
}
