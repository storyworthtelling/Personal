using System.Reflection;
using System.Text.RegularExpressions;
using Watch.Domain;
using Watch.Domain.Keys;
using Watch.Domain.Updates;
using static Watch.Tests.Keys.KeyFixtures;

namespace Watch.Tests.Keys;

/// <summary>
/// The token the phone hands the watch (plan §2.4, A61, P14 W-4.1.2): never logged, never written as text, held in memory
/// for no longer than the phone itself would use it, and asked for only when it is needed.
/// </summary>
public class KeyHandoffTests
{
    private const string Path = WatchIdentity.GoogleKeysPath;

    private static readonly Regex Shape = new(
        @"^(CONTRACT keys/google: (received v\d+|refused v\d+, newer than (Phone|Watch) reads|refused, unreadable|sent v\d+|not sent, [A-Za-z][A-Za-z0-9.]*)|KEYS keys/google: [A-Za-z]+)$");

    // ---- never logged ----

    [Fact]
    public void TokenNeverLogged()
    {
        var token = Token;
        var handoff = Handoff(TimeSpan.FromMinutes(30));
        var hold = new TokenHold();
        hold.Take(handoff, T0);
        var read = KeyCodec.Decode(Path, KeyCodec.Encode(handoff));
        Assert.True(read.Accepted);

        var said = new List<string>
        {
            token.ToString(), $"{token}", handoff.ToString(), $"{handoff}", read.ToString(), $"{read.Message}",
            hold.ToString(), $"{hold}", $"{hold.Current(T0)}", string.Join(", ", new object[] { token, handoff }),
        };
        var lines = new List<string>();
        foreach (var end in Enum.GetValues<KeyEnd>())
        {
            lines.Add(KeyLogLines.Receipt(Path, read, end));
            lines.Add(KeyLogLines.Receipt(Path, ContractRead<KeyMessage>.Refused(ContractRefusal.Newer, 2), end));
            lines.Add(KeyLogLines.Receipt(Path, ContractRead<KeyMessage>.Refused(ContractRefusal.Unreadable, 0), end));
        }
        lines.Add(KeyLogLines.Sent(Path));
        lines.Add(KeyLogLines.NotSent(Path, new InvalidOperationException(Sentinel)));
        lines.Add(KeyLogLines.NotSent(Path, new HttpRequestException(token.BearerHeader())));
        lines.AddRange(Enum.GetValues<KeyOutcome>().Select(outcome => KeyLogLines.Outcome(Path, outcome)));

        Assert.All(said.Concat(lines), line =>
        {
            Assert.DoesNotContain("wrist-token", line, StringComparison.Ordinal);
            Assert.DoesNotContain("0001", line, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer", line, StringComparison.Ordinal);
        });
        Assert.All(lines, line => Assert.Matches(Shape, line));
    }

    [Fact]
    public void ATokenSaysItIsAToken()
    {
        Assert.Equal("[access token]", Token.ToString());
    }

    [Fact]
    public void TheTokenLeavesAsAHeader()
    {
        Assert.Equal("Bearer " + Sentinel, Token.BearerHeader());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invented-but-short")]
    [InlineData("invented token with spaces")]
    [InlineData("invented-token-with-a-\"-quote")]
    [InlineData("invented-token-with-a-\nnew-line")]
    [InlineData("invented-token-with-é-and-more")]
    [InlineData("invented-token-with-a-;-semicolon")]
    public void ATokenIsATokensShape(string? value)
    {
        Assert.Null(AccessToken.From(value));
    }

    [Fact]
    public void ALongTokenIsATokenToItsLimit()
    {
        Assert.NotNull(AccessToken.From("invented" + new string('x', AccessToken.LongestLength - "invented".Length)));
        Assert.Null(AccessToken.From("invented" + new string('x', AccessToken.LongestLength - "invented".Length + 1)));
        Assert.NotNull(AccessToken.From(Sentinel));
    }

    [Fact]
    public void ATokenIsItsValue()
    {
        Assert.Equal(Token, Token);
        Assert.NotEqual(Token, OtherToken);
    }

    // ---- held in memory, for its life ----

    [Fact]
    public void AHandedTokenIsHeldForItsLife()
    {
        var hold = new TokenHold();

        Assert.Equal(KeyOutcome.Held, hold.Take(Handoff(TimeSpan.FromMinutes(30)), T0));

        Assert.Equal(Token, hold.Current(T0));
        Assert.Equal(Token, hold.Current(T0 + TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(1)));
        Assert.Null(hold.Current(T0 + TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void PastItsTimeTheTokenIsLetGo()
    {
        var hold = new TokenHold();
        hold.Take(Handoff(TimeSpan.FromMinutes(10)), T0);

        Assert.Null(hold.Current(T0 + TimeSpan.FromMinutes(11)));

        // Gone from memory, not only hidden: a clock that steps back does not bring it back.
        Assert.Null(hold.Current(T0));
    }

    [Fact]
    public void NothingIsHeldAtFirst()
    {
        Assert.Null(new TokenHold().Current(T0));
    }

    [Fact]
    public void ANewerTokenReplacesTheOld()
    {
        var hold = new TokenHold();
        hold.Take(Handoff(TimeSpan.FromMinutes(10)), T0);

        hold.Take(new TokenHandoff(TokenScope.DriveReadOnly, OtherToken, TimeSpan.FromMinutes(40)), T0 + TimeSpan.FromMinutes(9));

        Assert.Equal(OtherToken, hold.Current(T0 + TimeSpan.FromMinutes(20)));
    }

    [Fact]
    public void ARejectedTokenIsForgotten()
    {
        var hold = new TokenHold();
        hold.Take(Handoff(TimeSpan.FromMinutes(30)), T0);

        Assert.True(hold.Forget(Token));

        Assert.Null(hold.Current(T0));
    }

    [Fact]
    public void ATokenHandedSinceIsNotForgotten()
    {
        // Google refused the token a call used, but a fresh one arrived meanwhile: the fresh one stays.
        var hold = new TokenHold();
        hold.Take(new TokenHandoff(TokenScope.DriveReadOnly, OtherToken, TimeSpan.FromMinutes(30)), T0);

        Assert.False(hold.Forget(Token));

        Assert.Equal(OtherToken, hold.Current(T0));
    }

    [Fact]
    public void ThePhoneSayingNoneLetsTheTokenGo()
    {
        var hold = new TokenHold();
        hold.Take(Handoff(TimeSpan.FromMinutes(30)), T0);

        Assert.Equal(KeyOutcome.SignIn, hold.Refused(new TokenNone(TokenScope.DriveReadOnly, TokenRefusal.SignIn)));
        Assert.Null(hold.Current(T0));
        Assert.Equal(KeyOutcome.Failed, hold.Refused(new TokenNone(TokenScope.DriveReadOnly, TokenRefusal.Failed)));
    }

    // ---- asked for when needed, not pushed ----

    [Fact]
    public void AskWhenThereIsNone()
    {
        Assert.True(new TokenHold().AskDue(T0));
    }

    [Fact]
    public void NoAskWhileTheTokenIsGood()
    {
        var hold = new TokenHold();
        hold.Take(Handoff(TimeSpan.FromMinutes(30)), T0);

        Assert.False(hold.AskDue(T0 + TimeSpan.FromMinutes(24)));
    }

    [Fact]
    public void AskWhenTheTokenIsNearlyDone()
    {
        var hold = new TokenHold();
        hold.Take(Handoff(TimeSpan.FromMinutes(30)), T0);

        Assert.True(hold.AskDue(T0 + TimeSpan.FromMinutes(25)));
        Assert.True(hold.AskDue(T0 + TimeSpan.FromMinutes(31)));
    }

    [Fact]
    public void AnAskIsNotRepeatedWithinAMinute()
    {
        var hold = new TokenHold();
        hold.Asked(T0);

        Assert.False(hold.AskDue(T0 + TimeSpan.FromSeconds(59)));
        Assert.True(hold.AskDue(T0 + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void AShortAnswerDoesNotStartAStorm()
    {
        // A phone near the end of its own token hands what is left; the next ask still waits its minute.
        var hold = new TokenHold();
        hold.Asked(T0);
        hold.Take(Handoff(TimeSpan.FromSeconds(30)), T0 + TimeSpan.FromSeconds(5));

        Assert.False(hold.AskDue(T0 + TimeSpan.FromSeconds(10)));
        Assert.True(hold.AskDue(T0 + TimeSpan.FromMinutes(1)));
    }

    // ---- nothing but memory ----

    [Fact]
    public void TheHoldKeepsNothingButMemory()
    {
        // No member hands the token out as text, bytes or a stream, so there is nothing to write down: the one way out is
        // an AccessToken, whose text is a redaction and whose value leaves only as a header.
        var type = typeof(TokenHold);
        var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var outward = members.OfType<MethodInfo>().Where(method => !method.IsSpecialName && method.Name != nameof(ToString))
            .Select(method => method.ReturnType)
            .Concat(members.OfType<PropertyInfo>().Select(property => property.PropertyType))
            .ToList();

        Assert.True(type.IsSealed);
        Assert.False(type.IsSerializable);
        Assert.Empty(members.OfType<PropertyInfo>());
        Assert.All(outward, returned => Assert.Contains(returned, new[] { typeof(AccessToken), typeof(bool), typeof(KeyOutcome), typeof(void) }));
    }

    [Fact]
    public void TheTokensValueIsNotPublic()
    {
        var type = typeof(AccessToken);

        Assert.True(type.IsSealed);
        Assert.False(type.IsSerializable);
        Assert.Empty(type.GetProperties(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(["BearerHeader", "Equals", "GetHashCode", "ToString"],
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(method => method.Name).Order().ToArray());
    }

    // ---- the lines ----

    [Fact]
    public void AReceiptSaysTheContractAndVersion()
    {
        var read = ContractRead<KeyMessage>.Read(new TokenAsk(TokenScope.DriveReadOnly), 1);

        Assert.Equal("CONTRACT keys/google: received v1", KeyLogLines.Receipt(Path, read, KeyEnd.Phone));
        Assert.Equal("CONTRACT keys/google: refused v2, newer than Watch reads",
            KeyLogLines.Receipt(Path, ContractRead<KeyMessage>.Refused(ContractRefusal.Newer, 2), KeyEnd.Watch));
        Assert.Equal("CONTRACT keys/google: refused, unreadable",
            KeyLogLines.Receipt(Path, ContractRead<KeyMessage>.Refused(ContractRefusal.Unreadable, 0), KeyEnd.Watch));
    }

    [Fact]
    public void AnOutcomeIsAWord()
    {
        Assert.Equal("KEYS keys/google: Held", KeyLogLines.Outcome(Path, KeyOutcome.Held));
        Assert.Equal("CONTRACT keys/google: sent v1", KeyLogLines.Sent(Path));
        Assert.Equal("CONTRACT keys/google: not sent, TimeoutException", KeyLogLines.NotSent(Path, new TimeoutException()));
    }

    [Fact]
    public void AFailureIsItsTypeOnly()
    {
        // A failure's message can hold whatever the call was given, the token included.
        var failure = new InvalidOperationException("The call with " + Sentinel + " was refused");

        Assert.Equal("CONTRACT keys/google: not sent, InvalidOperationException", KeyLogLines.NotSent(Path, failure));
    }

    [Theory]
    [InlineData(WatchIdentity.DashPeoplePath)]
    [InlineData(WatchIdentity.UpdateInstalledPath)]
    [InlineData(Sentinel)]
    [InlineData("")]
    public void OnlyTheKeysPathIsAContract(string path)
    {
        Assert.Throws<ArgumentException>(() => KeyLogLines.Contract(path));
        Assert.Throws<ArgumentException>(() => KeyLogLines.Outcome(path, KeyOutcome.Held));
    }

    [Fact]
    public void TheContractIsThePathWithoutItsSlash()
    {
        Assert.Equal("keys/google", KeyLogLines.Contract(Path));
    }
}
