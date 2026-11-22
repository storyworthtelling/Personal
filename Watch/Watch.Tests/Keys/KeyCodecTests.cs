using System.Text;
using System.Text.Json;
using Watch.Domain;
using Watch.Domain.Keys;
using Watch.Domain.Updates;
using static Watch.Tests.Keys.KeyFixtures;

namespace Watch.Tests.Keys;

/// <summary>
/// The handed-over token's contract (plan §2.4, §2.5, P14 W-4.1.2): an integer <c>v</c>, a newer one refused before
/// anything else is read, anything malformed refusing the whole message, and nothing crossing but the named fields of
/// one read-only Drive token. Mail is Post's to fetch (<c>dash/mail</c>), so no Gmail token is a word this contract has.
/// </summary>
public class KeyCodecTests
{
    private const string Path = WatchIdentity.GoogleKeysPath;

    private static ContractRead<KeyMessage> Decode(string json) => KeyCodec.Decode(Path, Encoding.UTF8.GetBytes(json));

    // A handoff as the phone writes it, with one field changed.
    private static string TokenJson(string scope = "\"drive.readonly\"", string token = "\"" + Sentinel + "\"", string life = "1800") =>
        $"{{\"v\":1,\"kind\":\"token\",\"scope\":{scope},\"token\":{token},\"life\":{life}}}";

    [Theory]
    [MemberData(nameof(KeyFixtures.Names), MemberType = typeof(KeyFixtures))]
    public void EveryMessageRoundTrips(string name)
    {
        var sent = Messages[name];
        var bytes = KeyCodec.Encode(sent);

        var read = KeyCodec.Decode(sent.Path, bytes);

        Assert.True(read.Accepted, $"{name}: {read.Refusal}");
        Assert.Equal(KeyCodec.Version, read.Version);
        Assert.Equal(sent.GetType(), read.Message!.GetType());
        Assert.Equal(bytes, KeyCodec.Encode(read.Message));
    }

    [Theory]
    [MemberData(nameof(KeyFixtures.Names), MemberType = typeof(KeyFixtures))]
    public void EveryMessageCarriesAnIntegerV(string name)
    {
        using var json = JsonDocument.Parse(KeyCodec.Encode(Messages[name]));

        Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("v").ValueKind);
        Assert.Equal(KeyCodec.Version, json.RootElement.GetProperty("v").GetInt32());
    }

    [Fact]
    public void AHandoffKeepsItsTokenAndLife()
    {
        var read = Assert.IsType<TokenHandoff>(KeyCodec.Decode(Path, KeyCodec.Encode(Handoff(TimeSpan.FromMinutes(30)))).Message);

        Assert.Equal(TokenScope.DriveReadOnly, read.Scope);
        Assert.Equal("Bearer " + Sentinel, read.Token.BearerHeader());
        Assert.Equal(TimeSpan.FromMinutes(30), read.Life);
    }

    [Fact]
    public void ARefusalKeepsWhy()
    {
        var read = Assert.IsType<TokenNone>(Decode("{\"v\":1,\"kind\":\"none\",\"scope\":\"drive.readonly\",\"why\":\"sign-in\"}").Message);

        Assert.Equal(TokenRefusal.SignIn, read.Why);
    }

    [Fact]
    public void ALifeIsWholeSeconds()
    {
        using var json = JsonDocument.Parse(KeyCodec.Encode(Handoff(TimeSpan.FromSeconds(90.7))));

        Assert.Equal(90, json.RootElement.GetProperty("life").GetInt64());
    }

    [Fact]
    public void ANewerVersionIsRefused()
    {
        var read = Decode("{\"v\":2,\"kind\":\"ask\",\"scope\":\"drive.readonly\"}");

        Assert.Equal(ContractRefusal.Newer, read.Refusal);
        Assert.Equal(2, read.Version);
        Assert.Null(read.Message);
    }

    [Fact]
    public void ANewerVersionIsRefusedBeforeItsKindIsRead()
    {
        var read = Decode("{\"v\":3,\"kind\":\"vault\",\"what\":\"anything at all\"}");

        Assert.Equal(ContractRefusal.Newer, read.Refusal);
        Assert.Equal(3, read.Version);
    }

    public static TheoryData<string> Malformed() => new()
    {
        "not json",
        "[]",
        "\"token\"",
        "{}",
        "{\"kind\":\"ask\",\"scope\":\"drive.readonly\"}",
        "{\"v\":0,\"kind\":\"ask\",\"scope\":\"drive.readonly\"}",
        "{\"v\":-1,\"kind\":\"ask\",\"scope\":\"drive.readonly\"}",
        "{\"v\":\"1\",\"kind\":\"ask\",\"scope\":\"drive.readonly\"}",
        "{\"v\":1.5,\"kind\":\"ask\",\"scope\":\"drive.readonly\"}",
        "{\"v\":1,\"scope\":\"drive.readonly\"}",
        "{\"v\":1,\"kind\":7,\"scope\":\"drive.readonly\"}",
        "{\"v\":1,\"kind\":\"vault\",\"scope\":\"drive.readonly\"}",
        "{\"v\":1,\"kind\":\"ask\"}",
        "{\"v\":1,\"kind\":\"ask\",\"scope\":\"drive\"}",
        "{\"v\":1,\"kind\":\"ask\",\"scope\":\"gmail.readonly\"}",
        "{\"v\":1,\"kind\":\"ask\",\"scope\":\"gmail.modify\"}",
        "{\"v\":1,\"kind\":\"ask\",\"scope\":\"https://www.googleapis.com/auth/drive.readonly\"}",
        "{\"v\":1,\"kind\":\"ask\",\"scope\":\"DRIVE.READONLY\"}",
        "{\"v\":1,\"kind\":\"ask\",\"scope\":null}",
        "{\"v\":1,\"kind\":\"ask\",\"scope\":\"drive.readonly\",\"scope\":\"drive.readonly\"}",
        "{\"v\":1,\"kind\":\"ask\",\"scope\":\"drive.readonly\",\"token\":\"" + Sentinel + "\"}",
        TokenJson(scope: "\"drive\""),
        TokenJson(scope: "\"gmail.readonly\""),
        TokenJson(token: "null"),
        TokenJson(token: "17"),
        TokenJson(token: "\"\""),
        TokenJson(token: "\"invented-but-short\""),
        TokenJson(token: "\"invented token with spaces in it\""),
        TokenJson(token: "\"invented-token-with-a-quote-\\\"-inside\""),
        TokenJson(token: "\"invented-token-with-a-new\\nline-inside\""),
        TokenJson(token: "\"invented-token-with-é-in-it-and-more\""),
        TokenJson(token: "\"" + new string('a', AccessToken.LongestLength + 1) + "\""),
        TokenJson(life: "0"),
        TokenJson(life: "-60"),
        TokenJson(life: "2701"),
        TokenJson(life: "36000"),
        TokenJson(life: "60.5"),
        TokenJson(life: "\"1800\""),
        TokenJson(life: "null"),
        "{\"v\":1,\"kind\":\"token\",\"scope\":\"drive.readonly\",\"life\":1800}",
        "{\"v\":1,\"kind\":\"token\",\"scope\":\"drive.readonly\",\"token\":\"" + Sentinel + "\"}",
        "{\"v\":1,\"kind\":\"none\",\"scope\":\"drive.readonly\"}",
        "{\"v\":1,\"kind\":\"none\",\"scope\":\"drive.readonly\",\"why\":\"forgot\"}",
        "{\"v\":1,\"kind\":\"none\",\"scope\":\"drive.readonly\",\"why\":\"sign-in\",\"token\":\"" + Sentinel + "\"}",
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedMessageIsUnreadable(string json)
    {
        var read = Decode(json);

        Assert.Equal(ContractRefusal.Unreadable, read.Refusal);
        Assert.Null(read.Message);
    }

    [Fact]
    public void NothingIsUnreadable()
    {
        Assert.Equal(ContractRefusal.Unreadable, KeyCodec.Decode(Path, null).Refusal);
        Assert.Equal(ContractRefusal.Unreadable, KeyCodec.Decode(Path, []).Refusal);
    }

    [Fact]
    public void AnUnknownFieldRefusesTheMessage()
    {
        // Nothing crosses on this path but the named fields of its kind: not a second secret beside the token, by any name.
        var read = Decode(TokenJson().TrimEnd('}') + ",\"vault\":\"invented-vault-words\"}");

        Assert.Equal(ContractRefusal.Unreadable, read.Refusal);
    }

    [Fact]
    public void OnlyAReadOnlyDriveTokenCrosses()
    {
        Assert.Equal([TokenScope.DriveReadOnly], Enum.GetValues<TokenScope>());
        Assert.Equal("drive.readonly", KeyCodec.Word(TokenScope.DriveReadOnly));
    }

    [Theory]
    [InlineData(WatchIdentity.DashPeoplePath)]
    [InlineData(WatchIdentity.DashMailPath)]
    [InlineData(WatchIdentity.UpdateInstalledPath)]
    [InlineData("/keys/google/")]
    [InlineData("/keys/gmail")]
    [InlineData("keys/google")]
    [InlineData("")]
    [InlineData(null)]
    public void OnlyTheKeysPathIsRead(string? path)
    {
        var read = KeyCodec.Decode(path, KeyCodec.Encode(Messages["token"]));

        Assert.Equal(ContractRefusal.Unreadable, read.Refusal);
        Assert.Null(read.Message);
    }

    [Fact]
    public void TooLargeIsUnreadable()
    {
        var padded = "{\"v\":1,\"kind\":\"ask\",\"scope\":\"drive.readonly\"" + new string(' ', KeyLimits.MaxBytes) + "}";

        Assert.Equal(ContractRefusal.Unreadable, Decode(padded).Refusal);
    }

    [Fact]
    public void TheFullestMessageFitsTheBudget()
    {
        var longest = AccessToken.From("invented" + new string('x', AccessToken.LongestLength - "invented".Length))!;

        var bytes = KeyCodec.Encode(new TokenHandoff(TokenScope.DriveReadOnly, longest, KeyLimits.LongestHold));

        Assert.True(bytes.Length <= KeyLimits.MaxBytes, $"{bytes.Length} bytes");
        Assert.True(KeyCodec.Decode(Path, bytes).Accepted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.5)]
    [InlineData(2701)]
    public void ALifeOutsideTheHoldIsNotSent(double seconds)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => KeyCodec.Encode(Handoff(TimeSpan.FromSeconds(seconds))));

        Assert.DoesNotContain("0001", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("wrist-token", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWordsAreFrozen()
    {
        Assert.Equal(1, KeyCodec.Version);
        Assert.Equal(
            ["v", "kind", "ask", "token", "none", "scope", "token", "life", "why"],
            [KeyCodec.VersionField, KeyCodec.KindField, KeyCodec.AskKind, KeyCodec.TokenKind, KeyCodec.NoneKind,
                KeyCodec.ScopeField, KeyCodec.TokenField, KeyCodec.LifeField, KeyCodec.WhyField]);
        Assert.Equal("sign-in", KeyCodec.Word(TokenRefusal.SignIn));
        Assert.Equal("failed", KeyCodec.Word(TokenRefusal.Failed));
        Assert.Equal([TokenRefusal.SignIn, TokenRefusal.Failed], Enum.GetValues<TokenRefusal>());
    }

    [Fact]
    public void TheLimitsAreTheDecidedOnes()
    {
        Assert.Equal(4096, KeyLimits.MaxBytes);
        Assert.Equal(TimeSpan.FromMinutes(45), KeyLimits.LongestHold);
        Assert.Equal(TimeSpan.FromMinutes(5), KeyLimits.RenewBefore);
        Assert.Equal(TimeSpan.FromMinutes(1), KeyLimits.AskSpacing);
        Assert.Equal(20, AccessToken.ShortestLength);
        Assert.Equal(2048, AccessToken.LongestLength);
    }

    [Fact]
    public void EveryMessageTravelsOnTheKeysPath()
    {
        Assert.All(Messages.Values, message => Assert.Equal(WatchIdentity.GoogleKeysPath, message.Path));
    }
}
