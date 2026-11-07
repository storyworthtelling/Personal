using System.Text.Json;

namespace Storyworthtelling.Voice.Secrets;

/// <summary>The schema number a vault's plaintext carries, read before anything is deserialised.</summary>
public static class VaultSchema
{
    /// <summary>
    /// The top-level <c>Schema</c> number, compared without case like the rest of the vault; 1 when there is
    /// none (every file written before there was one), and 0 when the plaintext is not a JSON object, so a
    /// damaged payload is the deserialiser's to report.
    /// </summary>
    public static int Of(string plaintext)
    {
        try
        {
            using var document = JsonDocument.Parse(plaintext);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return 0;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "Schema", StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.TryGetInt32(out var schema) ? schema : 0;
                }
            }

            return 1;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
