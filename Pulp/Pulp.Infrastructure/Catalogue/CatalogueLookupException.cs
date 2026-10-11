using Pulp.Domain.Catalogue;

namespace Pulp.Infrastructure.Catalogue;

/// <summary>Why a lookup failed, in the terms the dog-ear and Diagnostics need (D260, D261).</summary>
public enum LookupFailure
{
    /// <summary>No key is set for the source; nothing was sent.</summary>
    MissingKey,

    /// <summary>The source refused the key — the one failure that reveals the key's settings field.</summary>
    Rejected,

    /// <summary>A rate limit or an exhausted allowance: not the key's fault, try later.</summary>
    Throttled,

    /// <summary>Any other failed response — the source is down or refused the request itself.</summary>
    Unavailable,

    /// <summary>A success whose body is not the shape the source documents.</summary>
    Malformed,
}

/// <summary>
/// A lookup that failed, naming the source, the kind of failure and the HTTP status. The message
/// never carries a key: the request URL is not quoted, because two of the sources take the key in
/// the query string.
/// </summary>
public sealed class CatalogueLookupException : Exception
{
    public CatalogueLookupException(LookupSource source, LookupFailure failure, int? statusCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        Source = source;
        Failure = failure;
        StatusCode = statusCode;
    }

    public new LookupSource Source { get; }

    public LookupFailure Failure { get; }

    public int? StatusCode { get; }
}
