namespace Watch.Domain.Updates;

/// <summary>Why a Data Layer message was not read.</summary>
public enum ContractRefusal
{
    None,

    /// <summary>Its <c>v</c> is newer than this watch reads: it may mean something else by the same words.</summary>
    Newer,

    /// <summary>No integer <c>v</c>, not JSON, or a field missing or of the wrong kind.</summary>
    Unreadable,
}

/// <summary>
/// One message read off a watch contract (plan §2.5): the message, or why not, and the <c>v</c> it carried (0 when it
/// carried none that could be read). Every contract message carries an integer <c>v</c>.
/// </summary>
public sealed record ContractRead<T>(T? Message, int Version, ContractRefusal Refusal) where T : class
{
    public bool Accepted => Refusal == ContractRefusal.None && Message is not null;

    public static ContractRead<T> Read(T message, int version) => new(message, version, ContractRefusal.None);

    public static ContractRead<T> Refused(ContractRefusal why, int version) => new(null, version, why);
}
