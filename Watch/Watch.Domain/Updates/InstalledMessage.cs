using System.Text.Json;

namespace Watch.Domain.Updates;

/// <summary>A message on <c>update/installed</c> (plan §2.5).</summary>
public abstract record InstalledMessage;

/// <summary>The phone asking what the watch has.</summary>
public sealed record InstalledAsk : InstalledMessage;

/// <summary>One suite app on the watch and its versionCode.</summary>
public sealed record WatchApp(string Package, long VersionCode);

/// <summary>
/// The watch's answer, and what it says after each install: the suite apps it has, and the last outcome of each app's
/// install, so the phone can say how the watch's last run went (W-3.1.3) and knows not to send again a prompt that is
/// waiting on the wrist. Package names, numbers and outcome words only.
/// </summary>
public sealed record InstalledReport(IReadOnlyList<WatchApp> Apps, IReadOnlyList<WatchInstallResult> Results) : InstalledMessage;

/// <summary>The <c>update/installed</c> message codec: UTF-8 JSON with an integer <c>v</c> and a <c>kind</c>.</summary>
public static class InstalledCodec
{
    /// <summary>The version this watch writes and reads.</summary>
    public const int Version = 1;

    // Wire words, frozen.
    public const string VersionField = "v";
    public const string KindField = "kind";
    public const string AskKind = "ask";
    public const string ReportKind = "report";
    public const string AppsField = "apps";
    public const string ResultsField = "results";
    public const string PackageField = "package";
    public const string VersionCodeField = "versionCode";
    public const string OutcomeField = "outcome";
    public const string StatusField = "status";

    public static byte[] Encode(InstalledMessage message)
    {
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionField, Version);
            switch (message)
            {
                case InstalledAsk:
                    writer.WriteString(KindField, AskKind);
                    break;
                case InstalledReport report:
                    writer.WriteString(KindField, ReportKind);
                    writer.WritePropertyName(AppsField);
                    writer.WriteStartArray();
                    foreach (var app in report.Apps)
                    {
                        writer.WriteStartObject();
                        writer.WriteString(PackageField, app.Package);
                        writer.WriteNumber(VersionCodeField, app.VersionCode);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WritePropertyName(ResultsField);
                    WriteResults(writer, report.Results);
                    break;
                default:
                    throw new ArgumentException($"{message.GetType().Name} is not an update/installed message.", nameof(message));
            }
            writer.WriteEndObject();
        }
        return json.ToArray();
    }

    /// <summary>The message, or why it was refused. A newer <c>v</c> is refused before anything else is read.</summary>
    public static ContractRead<InstalledMessage> Decode(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return ContractRead<InstalledMessage>.Refused(ContractRefusal.Unreadable, 0);
        }
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ContractRead<InstalledMessage>.Refused(ContractRefusal.Unreadable, 0);
            }

            var version = ContractVersion.Of(root, VersionField);
            if (version <= 0)
            {
                return ContractRead<InstalledMessage>.Refused(ContractRefusal.Unreadable, 0);
            }
            if (version > Version)
            {
                return ContractRead<InstalledMessage>.Refused(ContractRefusal.Newer, version);
            }

            var kind = root.TryGetProperty(KindField, out var said) && said.ValueKind == JsonValueKind.String ? said.GetString() : null;
            InstalledMessage? message = kind switch
            {
                AskKind => new InstalledAsk(),
                ReportKind => ReadReport(root),
                _ => null,
            };
            return message is null
                ? ContractRead<InstalledMessage>.Refused(ContractRefusal.Unreadable, version)
                : ContractRead<InstalledMessage>.Read(message, version);
        }
        catch (JsonException)
        {
            // Not JSON: refused as unreadable, and the line says so.
            return ContractRead<InstalledMessage>.Refused(ContractRefusal.Unreadable, 0);
        }
    }

    internal static void WriteResults(Utf8JsonWriter writer, IReadOnlyList<WatchInstallResult> results)
    {
        writer.WriteStartArray();
        foreach (var result in results)
        {
            writer.WriteStartObject();
            writer.WriteString(PackageField, result.Package);
            writer.WriteNumber(VersionCodeField, result.VersionCode);
            writer.WriteString(OutcomeField, WatchInstallOutcomes.Word(result.Outcome));
            if (result.Status is { } status)
            {
                writer.WriteString(StatusField, WatchInstallOutcomes.Word(status));
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    /// <summary>The results in an array, or null when any entry will not read: one unread word refuses them all.</summary>
    internal static IReadOnlyList<WatchInstallResult>? ReadResults(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var results = new List<WatchInstallResult>();
        foreach (var entry in array.EnumerateArray())
        {
            if (!TryApp(entry, out var app)
                || !entry.TryGetProperty(OutcomeField, out var word) || word.ValueKind != JsonValueKind.String
                || WatchInstallOutcomes.FromWord(word.GetString()) is not { } outcome)
            {
                return null;
            }

            InstallerStatus? status = null;
            if (entry.TryGetProperty(StatusField, out var said))
            {
                if (said.ValueKind != JsonValueKind.String || WatchInstallOutcomes.StatusFromWord(said.GetString()) is not { } known)
                {
                    return null;
                }
                status = known;
            }
            results.Add(new WatchInstallResult(app.Package, app.VersionCode, outcome, status));
        }
        return results;
    }

    private static InstalledReport? ReadReport(JsonElement root)
    {
        if (!root.TryGetProperty(AppsField, out var apps) || apps.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty(ResultsField, out var results))
        {
            return null;
        }

        var read = new List<WatchApp>();
        foreach (var entry in apps.EnumerateArray())
        {
            if (!TryApp(entry, out var app))
            {
                return null;
            }
            read.Add(app);
        }

        return ReadResults(results) is { } outcomes ? new InstalledReport(read, outcomes) : null;
    }

    private static bool TryApp(JsonElement entry, out WatchApp app)
    {
        app = new WatchApp(string.Empty, 0);
        if (entry.ValueKind != JsonValueKind.Object
            || !entry.TryGetProperty(PackageField, out var package) || package.ValueKind != JsonValueKind.String
            || package.GetString() is not { Length: > 0 } name
            || !entry.TryGetProperty(VersionCodeField, out var code) || code.ValueKind != JsonValueKind.Number
            || !code.TryGetInt64(out var versionCode))
        {
            return false;
        }
        app = new WatchApp(name, versionCode);
        return true;
    }
}
