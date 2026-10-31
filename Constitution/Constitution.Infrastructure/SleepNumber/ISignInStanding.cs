namespace Constitution.Infrastructure.SleepNumber;

/// <summary>
/// Whether the service has already refused the login this install holds. The household login is shared, so a
/// refusal is remembered until the stored login is replaced and is not tried again meanwhile (S1): asking
/// again can only fail again, and a run of failed sign-ins is how a shared account gets locked. The Android
/// head answers from the credential-health report and the moment the login was last saved.
/// </summary>
public interface ISignInStanding
{
    /// <summary>True when the login held now was refused and has not been replaced since.</summary>
    bool LoginRefused { get; }
}
