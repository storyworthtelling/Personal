using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.Domain.Policy;

namespace Codex.Infrastructure.Policy;

/// <summary>
/// JSON encode/decode for the policy audit log, so the Android store is a thin adapter and the
/// (de)serialization is unit-tested without a device. The event kind is written as its name (so an
/// exported/inspected log is readable and stable if the enum is reordered). An absent or garbled
/// payload decodes to <see cref="PolicyAuditLog.Empty"/>.
/// </summary>
public static class PolicyAuditLogCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Encode(PolicyAuditLog log) => JsonSerializer.Serialize(log.Events, Options);

    public static PolicyAuditLog Decode(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return PolicyAuditLog.Empty;
        }
        try
        {
            var events = JsonSerializer.Deserialize<List<PolicyEvent>>(json, Options);
            return events is null ? PolicyAuditLog.Empty : new PolicyAuditLog(events);
        }
        catch (JsonException)
        {
            return PolicyAuditLog.Empty;
        }
    }
}
