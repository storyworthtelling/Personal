using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Storyworthtelling.Updates;

namespace Watch.Domain.Updates;

/// <summary>
/// What the phone says about the APK it is about to send on <c>update/apk</c> (plan §2.5): its package, versionCode and
/// form, and its length and SHA-256 so the watch can tell an APK that arrived whole from one that did not. The phone's
/// word is only the first reading: the installer installs nothing until the APK's own readings agree with it
/// (<see cref="WatchUpdatePlan.Verify"/>).
/// </summary>
public sealed record UpdateApkHeader(string Package, long VersionCode, ApkForm Form, long Length, string Sha256)
{
    /// <summary>The version this watch writes and reads. A header of a newer one is refused, not guessed at.</summary>
    public const int Version = 1;

    public InstallCandidate Candidate => new(Package, VersionCode, Form);
}

/// <summary>A header the watch could read, and whether the bytes behind it were exactly the APK it described.</summary>
public sealed record ReceivedApk(UpdateApkHeader Header, bool Intact);

/// <summary>
/// The channel's layout: four bytes of the header's length (big-endian), the header as UTF-8 JSON, then exactly the
/// APK's bytes. One place writes it and reads it, so Flyleaf (W-3.1.3) and the installer cannot drift apart.
/// </summary>
public static class UpdateApkFrame
{
    /// <summary>A header is a few hundred bytes; one claiming more than this is not a header.</summary>
    public const int MaxHeaderBytes = 16 * 1024;

    // Wire words, frozen: the field names of the header.
    public const string VersionField = "v";
    public const string PackageField = "package";
    public const string VersionCodeField = "versionCode";
    public const string FormField = "form";
    public const string LengthField = "length";
    public const string Sha256Field = "sha256";

    private const int LengthPrefix = 4;
    private const int Sha256HexLength = 64;

    /// <summary>The header for an APK, reading it once for its length and hash. The stream must be seekable; it is
    /// left where it started.</summary>
    public static UpdateApkHeader Describe(string package, long versionCode, ApkForm form, Stream apk)
    {
        var start = apk.Position;
        var hash = SHA256.HashData(apk);
        var length = apk.Position - start;
        apk.Position = start;
        return new UpdateApkHeader(package, versionCode, form, length, Convert.ToHexString(hash).ToLowerInvariant());
    }

    /// <summary><see cref="Describe"/> without blocking a thread on the read (finding 3).</summary>
    public static async Task<UpdateApkHeader> DescribeAsync(string package, long versionCode, ApkForm form, Stream apk, CancellationToken cancellation)
    {
        var start = apk.Position;
        var hash = await SHA256.HashDataAsync(apk, cancellation);
        var length = apk.Position - start;
        apk.Position = start;
        return new UpdateApkHeader(package, versionCode, form, length, Convert.ToHexString(hash).ToLowerInvariant());
    }

    /// <summary>The length prefix and the header, ready to go in front of the APK.</summary>
    public static byte[] Header(UpdateApkHeader header)
    {
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionField, UpdateApkHeader.Version);
            writer.WriteString(PackageField, header.Package);
            writer.WriteNumber(VersionCodeField, header.VersionCode);
            writer.WriteString(FormField, ApkForms.Word(header.Form));
            writer.WriteNumber(LengthField, header.Length);
            writer.WriteString(Sha256Field, header.Sha256);
            writer.WriteEndObject();
        }

        var body = json.ToArray();
        var framed = new byte[LengthPrefix + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(framed, body.Length);
        body.CopyTo(framed, LengthPrefix);
        return framed;
    }

    /// <summary>Writes the header and then the APK to <paramref name="output"/>.</summary>
    public static void Pack(UpdateApkHeader header, Stream apk, Stream output)
    {
        output.Write(Header(header));
        apk.CopyTo(output);
    }

    /// <summary>
    /// Reads the header and copies exactly the APK's bytes to <paramref name="apk"/>, hashing them on the way. A header
    /// that will not read, or is of a newer version, is refused and nothing is copied. A transfer that ends early, runs
    /// past the length or hashes differently is read but not <see cref="ReceivedApk.Intact"/>.
    /// </summary>
    public static ContractRead<ReceivedApk> Unpack(Stream framed, Stream apk)
    {
        var prefix = new byte[LengthPrefix];
        if (framed.ReadAtLeast(prefix, LengthPrefix, throwOnEndOfStream: false) < LengthPrefix)
        {
            return ContractRead<ReceivedApk>.Refused(ContractRefusal.Unreadable, 0);
        }

        var size = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (size <= 0 || size > MaxHeaderBytes)
        {
            return ContractRead<ReceivedApk>.Refused(ContractRefusal.Unreadable, 0);
        }

        var body = new byte[size];
        if (framed.ReadAtLeast(body, size, throwOnEndOfStream: false) < size)
        {
            return ContractRead<ReceivedApk>.Refused(ContractRefusal.Unreadable, 0);
        }

        var (header, version, refusal) = ReadHeader(body);
        if (header is null)
        {
            return ContractRead<ReceivedApk>.Refused(refusal, version);
        }

        var intact = CopyExactly(framed, apk, header);
        return ContractRead<ReceivedApk>.Read(new ReceivedApk(header, intact), version);
    }

    private static (UpdateApkHeader? Header, int Version, ContractRefusal Refusal) ReadHeader(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, 0, ContractRefusal.Unreadable);
            }

            var version = ContractVersion.Of(root, VersionField);
            if (version <= 0)
            {
                return (null, 0, ContractRefusal.Unreadable);
            }
            if (version > UpdateApkHeader.Version)
            {
                return (null, version, ContractRefusal.Newer);
            }

            if (!root.TryGetProperty(PackageField, out var package) || package.ValueKind != JsonValueKind.String
                || package.GetString() is not { Length: > 0 } name
                || !root.TryGetProperty(VersionCodeField, out var code) || code.ValueKind != JsonValueKind.Number
                || !code.TryGetInt64(out var versionCode)
                || !root.TryGetProperty(LengthField, out var length) || length.ValueKind != JsonValueKind.Number
                || !length.TryGetInt64(out var bytes) || bytes <= 0
                || !root.TryGetProperty(Sha256Field, out var hash) || hash.ValueKind != JsonValueKind.String
                || hash.GetString() is not { } sha256 || !IsSha256(sha256))
            {
                return (null, version, ContractRefusal.Unreadable);
            }

            return (new UpdateApkHeader(name, versionCode, FormOf(root), bytes, sha256), version, ContractRefusal.None);
        }
        catch (JsonException)
        {
            // Not JSON at all: nothing in it can be trusted, so it is refused as unreadable.
            return (null, 0, ContractRefusal.Unreadable);
        }
    }

    // The form is required here, unlike a sidecar's: this contract is new, so no header was ever written without one.
    private static ApkForm FormOf(JsonElement root) =>
        root.TryGetProperty(FormField, out var form) && form.ValueKind == JsonValueKind.String
            ? ApkForms.FromWord(form.GetString())
            : ApkForm.Unknown;

    private static bool IsSha256(string hex) =>
        hex.Length == Sha256HexLength && hex.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    // Copies at most the header's length, so a stream that runs on never fills the watch; then checks nothing followed.
    private static bool CopyExactly(Stream framed, Stream apk, UpdateApkHeader header)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        var remaining = header.Length;
        while (remaining > 0)
        {
            var read = framed.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                return false;
            }
            hash.AppendData(buffer, 0, read);
            apk.Write(buffer, 0, read);
            remaining -= read;
        }

        var trailing = framed.Read(buffer, 0, 1) > 0;
        var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        return !trailing && string.Equals(digest, header.Sha256, StringComparison.Ordinal);
    }
}

/// <summary>Reading a message's <c>v</c>: a whole number of one or more, or 0 for a message that has none to read.</summary>
internal static class ContractVersion
{
    public static int Of(JsonElement root, string field) =>
        root.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var version) && version > 0
            ? version
            : 0;
}

/// <summary>
/// The channel path of one transfer: <see cref="WatchIdentity.UpdateApkPath"/>, a slash, and a name the phone chooses
/// for it. The installer receives each APK into a file named for its transfer, so two transfers never share a file, and
/// the platform's callback after a restart still finds the right one.
/// </summary>
public static class UpdatePaths
{
    public const int MaxTransferLength = 64;

    private const string Prefix = WatchIdentity.UpdateApkPath + "/";

    public static string ApkPathFor(string transfer) =>
        IsTransfer(transfer)
            ? Prefix + transfer
            : throw new ArgumentException($"A transfer is 1 to {MaxTransferLength} lowercase letters, digits or hyphens.", nameof(transfer));

    /// <summary>The transfer a channel path names, or null when it is not an <c>update/apk</c> transfer.</summary>
    public static string? TransferOf(string? path)
    {
        if (path is null || !path.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }
        var transfer = path[Prefix.Length..];
        return IsTransfer(transfer) ? transfer : null;
    }

    private static bool IsTransfer(string transfer) =>
        transfer.Length is > 0 and <= MaxTransferLength
        && transfer.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
