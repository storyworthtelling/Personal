namespace Constitution.Domain.Body;

/// <summary>
/// One day's value of one signal, in the signal's canonical unit (see <see cref="BodySignal"/>).
/// There is deliberately no name, birthdate or person here: the install is the person (brief, Two
/// installs), and a reading that could say whose it is could be sent somewhere it should not go.
/// </summary>
public sealed record BodyReading(BodySignal Signal, DateOnly Day, double Value);
