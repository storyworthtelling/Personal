using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Storyworthtelling.Updates;
using Watch.Domain;
using Watch.Domain.Updates;

namespace Watch.Tests.Updates;

/// <summary>
/// The <c>update/apk</c> contract (plan §2.5): Flyleaf opens a channel on <c>/update/apk/&lt;transfer&gt;</c> and writes a
/// versioned header, then the APK. The watch reads the header, copies exactly the APK's bytes and checks them against
/// the header's length and SHA-256, so a transfer that was cut short or changed on the way is never installed.
/// </summary>
public class UpdateApkTests
{
    private static readonly byte[] Apk = Enumerable.Range(0, 5000).Select(index => (byte)(index * 7)).ToArray();

    private static UpdateApkHeader Describe(byte[] apk, ApkForm form = ApkForm.Watch) =>
        UpdateApkFrame.Describe(WatchIdentity.DashPackage, 1_100_000_300, form, new MemoryStream(apk));

    private static byte[] Framed(UpdateApkHeader header, byte[] apk)
    {
        using var output = new MemoryStream();
        UpdateApkFrame.Pack(header, new MemoryStream(apk), output);
        return output.ToArray();
    }

    private static byte[] FramedJson(string json, byte[]? apk = null)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var framed = new byte[4 + body.Length + (apk?.Length ?? 0)];
        BinaryPrimitives.WriteInt32BigEndian(framed, body.Length);
        body.CopyTo(framed, 4);
        apk?.CopyTo(framed, 4 + body.Length);
        return framed;
    }

    private static (ContractRead<ReceivedApk> Read, byte[] Copied) Unpack(byte[] framed)
    {
        using var copy = new MemoryStream();
        var read = UpdateApkFrame.Unpack(new MemoryStream(framed), copy);
        return (read, copy.ToArray());
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public void TheHeaderDescribesTheApk()
    {
        var header = Describe(Apk);

        Assert.Equal(WatchIdentity.DashPackage, header.Package);
        Assert.Equal(1_100_000_300, header.VersionCode);
        Assert.Equal(ApkForm.Watch, header.Form);
        Assert.Equal(Apk.Length, header.Length);
        Assert.Equal(Hex(Apk), header.Sha256);
        Assert.Equal(new InstallCandidate(WatchIdentity.DashPackage, 1_100_000_300, ApkForm.Watch), header.Candidate);
    }

    [Fact]
    public void TheApkFollowsTheHeader()
    {
        var header = Describe(Apk);

        var (read, copied) = Unpack(Framed(header, Apk));

        Assert.True(read.Accepted);
        Assert.Equal(UpdateApkHeader.Version, read.Version);
        Assert.Equal(header, read.Message!.Header);
        Assert.True(read.Message.Intact);
        Assert.Equal(Apk, copied);
    }

    [Fact]
    public void EveryHeaderCarriesAnIntegerV()
    {
        var header = UpdateApkFrame.Header(Describe(Apk));
        var json = Encoding.UTF8.GetString(header, 4, header.Length - 4);

        Assert.Equal(1, UpdateApkHeader.Version);
        Assert.Equal(header.Length - 4, BinaryPrimitives.ReadInt32BigEndian(header));
        Assert.Contains("\"v\":1,", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewerVersionIsRefused()
    {
        var json = $"{{\"v\":2,\"package\":\"{WatchIdentity.DashPackage}\",\"versionCode\":1,\"form\":\"watch\",\"length\":{Apk.Length},\"sha256\":\"{Hex(Apk)}\"}}";

        var (read, copied) = Unpack(FramedJson(json, Apk));

        Assert.False(read.Accepted);
        Assert.Equal(ContractRefusal.Newer, read.Refusal);
        Assert.Equal(2, read.Version);
        Assert.Null(read.Message);
        Assert.Empty(copied);
    }

    [Theory]
    [InlineData("{\"package\":\"com.storyworthtelling.focusphone.communication\",\"versionCode\":1,\"form\":\"watch\",\"length\":1,\"sha256\":\"%\"}")]
    [InlineData("{\"v\":\"1\",\"package\":\"com.storyworthtelling.focusphone.communication\",\"versionCode\":1,\"form\":\"watch\",\"length\":1,\"sha256\":\"%\"}")]
    [InlineData("{\"v\":1.5,\"package\":\"com.storyworthtelling.focusphone.communication\",\"versionCode\":1,\"form\":\"watch\",\"length\":1,\"sha256\":\"%\"}")]
    [InlineData("{\"v\":0,\"package\":\"com.storyworthtelling.focusphone.communication\",\"versionCode\":1,\"form\":\"watch\",\"length\":1,\"sha256\":\"%\"}")]
    [InlineData("{\"v\":1,\"versionCode\":1,\"form\":\"watch\",\"length\":1,\"sha256\":\"%\"}")]
    [InlineData("{\"v\":1,\"package\":\"\",\"versionCode\":1,\"form\":\"watch\",\"length\":1,\"sha256\":\"%\"}")]
    [InlineData("{\"v\":1,\"package\":\"com.storyworthtelling.focusphone.communication\",\"versionCode\":\"1\",\"form\":\"watch\",\"length\":1,\"sha256\":\"%\"}")]
    [InlineData("{\"v\":1,\"package\":\"com.storyworthtelling.focusphone.communication\",\"versionCode\":1,\"form\":\"watch\",\"length\":0,\"sha256\":\"%\"}")]
    [InlineData("{\"v\":1,\"package\":\"com.storyworthtelling.focusphone.communication\",\"versionCode\":1,\"form\":\"watch\",\"sha256\":\"%\"}")]
    [InlineData("{\"v\":1,\"package\":\"com.storyworthtelling.focusphone.communication\",\"versionCode\":1,\"form\":\"watch\",\"length\":1}")]
    [InlineData("{\"v\":1,\"package\":\"com.storyworthtelling.focusphone.communication\",\"versionCode\":1,\"form\":\"watch\",\"length\":1,\"sha256\":\"ABCDEF\"}")]
    [InlineData("[1]")]
    [InlineData("not json")]
    public void AMalformedHeaderIsUnreadable(string json)
    {
        var (read, copied) = Unpack(FramedJson(json.Replace("%", Hex(Apk), StringComparison.Ordinal), Apk));

        Assert.Equal(ContractRefusal.Unreadable, read.Refusal);
        Assert.Null(read.Message);
        Assert.Empty(copied);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0, 0 })]
    [InlineData(new byte[] { 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0, 0x01, 0, 0, (byte)'{' })]
    [InlineData(new byte[] { 0, 0, 0, 9, (byte)'{' })]
    public void ABrokenFrameIsUnreadable(byte[] framed)
    {
        Assert.Equal(ContractRefusal.Unreadable, Unpack(framed).Read.Refusal);
    }

    [Theory]
    [InlineData("\"tablet\"")]
    [InlineData("\"Watch\"")]
    [InlineData("11")]
    [InlineData("null")]
    public void AnyOtherFormIsUnknown(string form)
    {
        var json = $"{{\"v\":1,\"package\":\"{WatchIdentity.DashPackage}\",\"versionCode\":1,\"form\":{form},\"length\":{Apk.Length},\"sha256\":\"{Hex(Apk)}\"}}";

        var (read, _) = Unpack(FramedJson(json, Apk));

        Assert.True(read.Accepted);
        Assert.Equal(ApkForm.Unknown, read.Message!.Header.Form);
    }

    [Fact]
    public void NoFormIsUnknownHere()
    {
        // Unlike a sidecar, where no form meant every APK published before P14: this contract is new, so a header that
        // says nothing about its form is installed nowhere.
        var json = $"{{\"v\":1,\"package\":\"{WatchIdentity.DashPackage}\",\"versionCode\":1,\"length\":{Apk.Length},\"sha256\":\"{Hex(Apk)}\"}}";

        Assert.Equal(ApkForm.Unknown, Unpack(FramedJson(json, Apk)).Read.Message!.Header.Form);
    }

    [Fact]
    public void AShortApkIsNotIntact()
    {
        var framed = Framed(Describe(Apk), Apk);

        var (read, _) = Unpack(framed[..^10]);

        Assert.True(read.Accepted);
        Assert.False(read.Message!.Intact);
    }

    [Fact]
    public void TrailingBytesAreNotIntact()
    {
        var framed = Framed(Describe(Apk), Apk).Concat(new byte[] { 1, 2, 3 }).ToArray();

        Assert.False(Unpack(framed).Read.Message!.Intact);
    }

    [Fact]
    public void AChangedByteIsNotIntact()
    {
        var framed = Framed(Describe(Apk), Apk);
        framed[^100] ^= 0x01;

        Assert.False(Unpack(framed).Read.Message!.Intact);
    }

    [Fact]
    public void TheCopyNeverRunsPastTheLength()
    {
        var framed = Framed(Describe(Apk), Apk).Concat(new byte[4096]).ToArray();

        var (_, copied) = Unpack(framed);

        Assert.True(copied.Length <= Apk.Length);
    }

    [Fact]
    public void TheFieldNamesAreFrozen()
    {
        Assert.Equal("v", UpdateApkFrame.VersionField);
        Assert.Equal("package", UpdateApkFrame.PackageField);
        Assert.Equal("versionCode", UpdateApkFrame.VersionCodeField);
        Assert.Equal("form", UpdateApkFrame.FormField);
        Assert.Equal("length", UpdateApkFrame.LengthField);
        Assert.Equal("sha256", UpdateApkFrame.Sha256Field);
        Assert.Equal(16 * 1024, UpdateApkFrame.MaxHeaderBytes);
    }

    [Fact]
    public void TheFormIsTheSidecarsWord()
    {
        // One word for a form across the release, the sidecar and this contract.
        Assert.Equal(ApkForms.Field, UpdateApkFrame.FormField);
        var json = Encoding.UTF8.GetString(UpdateApkFrame.Header(Describe(Apk))[4..]);
        Assert.Contains($"\"form\":\"{ApkForms.WatchWord}\"", json, StringComparison.Ordinal);
    }

    // ---- the channel's path names the transfer ----

    [Theory]
    [InlineData("a")]
    [InlineData("1100000300-0f3c")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz01")]
    public void APathCarriesItsTransfer(string transfer)
    {
        var path = UpdatePaths.ApkPathFor(transfer);

        Assert.Equal(WatchIdentity.UpdateApkPath + "/" + transfer, path);
        Assert.Equal(transfer, UpdatePaths.TransferOf(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/update/apk")]
    [InlineData("/update/apk/")]
    [InlineData("/update/apkx/1")]
    [InlineData("/update/installed")]
    [InlineData("/update/apk/../x")]
    [InlineData("/update/apk/a/b")]
    [InlineData("/update/apk/Upper")]
    [InlineData("/update/apk/a b")]
    [InlineData("/update/apk/abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz012")]
    public void AnyOtherPathHasNoTransfer(string? path)
    {
        Assert.Null(UpdatePaths.TransferOf(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("..")]
    public void ABadTransferIsRefused(string transfer)
    {
        Assert.Throws<ArgumentException>(() => UpdatePaths.ApkPathFor(transfer));
    }
}
