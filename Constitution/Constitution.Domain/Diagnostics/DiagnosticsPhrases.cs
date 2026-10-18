namespace Constitution.Domain.Diagnostics;

/// <summary>
/// Every sentence the Diagnostics export says about a closed value, typed out here. A phrase is a
/// constant: it is chosen by an enum, so nothing a user or a service wrote can end up in it, and none
/// carries a digit or a health word (<c>DiagnosticsPrivacyTests.EveryClosedValueRendersCleanly</c>
/// scans every one). An undefined enum value throws rather than printing its number, since a number
/// in an export is the thing the export promises not to hold.
/// </summary>
public static class DiagnosticsPhrases
{
    public static string Of(DiagnosticsComponent component) => component switch
    {
        DiagnosticsComponent.MorningDelivery => "Morning delivery",
        DiagnosticsComponent.ReadBack => "Read-back",
        DiagnosticsComponent.BodySync => "Body sync",
        DiagnosticsComponent.HouseholdSync => "Household sync",
        DiagnosticsComponent.CalendarWrite => "Calendar write",
        _ => throw Undefined(nameof(component)),
    };

    public static string Of(DiagnosticsOutcome outcome) => outcome switch
    {
        DiagnosticsOutcome.NeverRan => "Has not run yet",
        DiagnosticsOutcome.Succeeded => "Done",
        DiagnosticsOutcome.Refused => "Refused by the service",
        DiagnosticsOutcome.Unreachable => "Could not be reached",
        DiagnosticsOutcome.FoundNothingRead => "Document found, nothing read",
        DiagnosticsOutcome.Partial => "Done in part",
        _ => throw Undefined(nameof(outcome)),
    };

    public static string Of(DeliveryTarget target) => target switch
    {
        DeliveryTarget.ReMarkable => "reMarkable",
        DeliveryTarget.Drive => "Drive",
        _ => throw Undefined(nameof(target)),
    };

    public static string Of(CredentialState state) => state switch
    {
        CredentialState.Working => "Working",
        CredentialState.Missing => "Missing",
        CredentialState.Rejected => "Rejected",
        _ => throw Undefined(nameof(state)),
    };

    public static string Of(InstallRole role) => role switch
    {
        InstallRole.Owner => "owner",
        InstallRole.Partner => "partner",
        InstallRole.Unknown => "not known yet",
        _ => throw Undefined(nameof(role)),
    };

    public static string Of(TrailKind kind) => kind switch
    {
        TrailKind.Body => "Body trail",
        TrailKind.Vault => "Vault trail",
        _ => throw Undefined(nameof(kind)),
    };

    internal static ArgumentOutOfRangeException Undefined(string parameter) =>
        new(parameter, "That value is not one the Diagnostics export knows.");
}
