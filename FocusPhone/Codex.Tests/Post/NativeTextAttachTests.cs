using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Every text Post sends is composed in one dialog (<c>ComposeText</c>), for one person or a
/// group, and every reply happens inside Post — the owner's ruling. The dialog must carry a
/// picture or video as well as words, and when the bridge to Google Messages is down the words
/// still go as a plain text through the phone rather than handing the owner to another app.
///
/// <para>Read from source because the compose dialog lives in an Android head the test project
/// cannot reference — the same convention as <c>AreaHueTests</c>.</para>
/// </summary>
public class NativeTextAttachTests
{
    private static readonly string MailboxSource = ReadSource("MailboxArea.cs");

    [Fact]
    public void TheComposeOffersAttachForOnePersonOrAGroup()
    {
        var body = MethodBody("ComposeText");

        Assert.Contains("SetNeutralButton(\"Attach\", (_, _) => PickMedia(recipients, conversationId))", body);
    }

    [Fact]
    public void WithoutTheBridgeAOneToOneTextGoesThroughSmsSender()
    {
        var body = MethodBody("SendWithoutBridge");

        // The fourth argument is the send's key action (LOG-LOOP-PLAN.md §4.1), which reports the radio's result.
        Assert.Contains("SmsSender.Send(Host, recipients[0], text, sending)", body);
    }

    [Fact]
    public void BothReplyEntriesUseTheOneCompose()
    {
        Assert.Contains("private void SendText(string number) => ComposeText([number], openBridgeConversationId);", MailboxSource);
        Assert.Contains(
            "private void ShowGroupCompose(IReadOnlyList<string> recipients) => ComposeText(recipients, openBridgeConversationId);",
            MailboxSource);
    }

    // Replying never leaves Post: nothing in the Mailbox or the Rolodex hands a text to whichever
    // app answers "smsto:" (Google Messages, now that it holds the texting role). The Rolodex
    // aims its Text action at Post's own router by class.
    [Theory]
    [InlineData("MailboxArea.cs")]
    [InlineData("RolodexArea.cs")]
    public void NoTextIsHandedToAnotherApp(string file)
    {
        var source = ReadSource(file);

        Assert.DoesNotMatch(new Regex(@"new Intent\(Intent\.ActionSendto,"), source);
        Assert.DoesNotContain("TextSendPath.DeepLink", source);
    }

    [Fact]
    public void TheRolodexStartsATextInPost()
    {
        Assert.Contains("new Intent(Host, typeof(SmsSendToActivity))", ReadSource("RolodexArea.cs"));
    }

    private static string MethodBody(string methodName)
    {
        var header = new Regex($@"\n    (?:private|public|internal)\s[^\n(]*\b{methodName}\(");
        var match = header.Match(MailboxSource);
        Assert.True(match.Success, $"MailboxArea.cs has no method named {methodName}.");

        var open = MailboxSource.IndexOf('{', match.Index);
        var depth = 0;
        for (var i = open; i < MailboxSource.Length; i++)
        {
            if (MailboxSource[i] == '{')
            {
                depth++;
            }
            else if (MailboxSource[i] == '}' && --depth == 0)
            {
                return MailboxSource[open..(i + 1)];
            }
        }
        throw new InvalidOperationException($"Could not find the end of {methodName}.");
    }

    private static string ReadSource(string file)
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null
            && !File.Exists(Path.Combine(directory, "FocusPhone", "Post.Android", file)))
        {
            directory = Path.GetDirectoryName(directory);
        }
        if (directory is null)
        {
            throw new InvalidOperationException($"Could not locate FocusPhone/Post.Android/{file} from the test output directory.");
        }
        return File.ReadAllText(Path.Combine(directory, "FocusPhone", "Post.Android", file));
    }
}
