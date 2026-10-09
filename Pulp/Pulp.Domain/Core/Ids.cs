namespace Pulp.Domain.Core;

/// <summary>
/// The handful of identities every rule in Pulp names. Each is a value compared without case, so an
/// id read back from the household file or a provider matches the one the app wrote, whatever its
/// casing. They live in one place so the Domain's folders never grow two kinds of "title".
/// </summary>
public readonly record struct TitleId
{
    public TitleId(string value) => Value = CoreText.Require(value, nameof(TitleId));

    public string Value { get; }

    public bool Equals(TitleId other) => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? string.Empty);

    public override string ToString() => Value;
}

/// <summary>A streaming service, by the id Pulp's own catalogue gives it.</summary>
public readonly record struct ServiceId
{
    public ServiceId(string value) => Value = CoreText.Require(value, nameof(ServiceId));

    public string Value { get; }

    public bool Equals(ServiceId other) => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? string.Empty);

    public override string ToString() => Value;
}

/// <summary>A person in the household — an adult or a child viewer.</summary>
public readonly record struct ViewerId
{
    public ViewerId(string value) => Value = CoreText.Require(value, nameof(ViewerId));

    public string Value { get; }

    public bool Equals(ViewerId other) => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? string.Empty);

    public override string ToString() => Value;
}

/// <summary>What a title is. Sport is not a title; it has its own folder.</summary>
public enum TitleKind
{
    Film,
    Show,
}

internal static class CoreText
{
    public static string Require(string value, string what) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(what + " needs a value; an empty id would match nothing it was meant to.", nameof(value))
            : value;
}
