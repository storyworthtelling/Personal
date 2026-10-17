namespace Constitution.Domain.Diagnostics;

/// <summary>How the last attempt at one part of the app ended.</summary>
public sealed record ComponentOutcome
{
    public ComponentOutcome(DiagnosticsComponent component, DiagnosticsOutcome outcome)
    {
        Component = Defined(component, nameof(component));
        Outcome = Defined(outcome, nameof(outcome));
    }

    public DiagnosticsComponent Component { get; }

    public DiagnosticsOutcome Outcome { get; }

    internal static T Defined<T>(T value, string parameter) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw DiagnosticsPhrases.Undefined(parameter);
}

/// <summary>A delivered document: its file name, where it went, and how that ended.</summary>
public sealed record DocumentDelivery
{
    public DocumentDelivery(DocumentFile file, DeliveryTarget target, DiagnosticsOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(file);

        File = file;
        Target = ComponentOutcome.Defined(target, nameof(target));
        Outcome = ComponentOutcome.Defined(outcome, nameof(outcome));
    }

    public DocumentFile File { get; }

    public DeliveryTarget Target { get; }

    public DiagnosticsOutcome Outcome { get; }
}

/// <summary>One connection and what its login is worth right now.</summary>
public sealed record CredentialStatus
{
    public CredentialStatus(ConnectionId connection, CredentialState state)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Connection = connection;
        State = ComponentOutcome.Defined(state, nameof(state));
    }

    public ConnectionId Connection { get; }

    public CredentialState State { get; }
}
