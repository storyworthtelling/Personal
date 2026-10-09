using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// A one-to-one text sent natively by Post (the role-holder path) must be able to carry a
/// picture or video, the same way a group reply can. Before this, <c>ShowNativeCompose</c>
/// offered only a message box, so the owner's only way to send a photo to one person was to
/// leave Post for Google Messages — the failure this app is measured against.
///
/// <para>The MMS plumbing already exists and is shared: a one-recipient MMS goes through the
/// same <c>MmsSender</c>/<c>MmsComposer</c> path a group reply does (the composer's single-
/// recipient picture case is covered by <see cref="MmsComposerTests"/>). So the only thing to
/// pin here is that the 1:1 compose dialog wires its Attach button into that path, with the
/// one number as its recipient list.</para>
///
/// <para>Read from source because the compose dialog lives in an Android head the test project
/// cannot reference — the same convention as <c>AreaHueTests</c>.</para>
/// </summary>
public class NativeTextAttachTests
{
    private static readonly string MailboxSource = ReadMailboxSource();

    [Fact]
    public void OneToOneNativeCompose_OffersAttachThroughTheSingleRecipientMms()
    {
        var body = MethodBody("ShowNativeCompose");

        Assert.Contains("SetNeutralButton(\"Attach\", (_, _) => PickMediaForGroup([number]))", body);
    }

    [Fact]
    public void OneToOneNativeCompose_StillSendsPlainTextThroughSmsSender()
    {
        var body = MethodBody("ShowNativeCompose");

        Assert.Contains("SmsSender.Send(Host, number, text)", body);
    }

    [Fact]
    public void GroupCompose_StillOffersAttach()
    {
        var body = MethodBody("ShowGroupCompose");

        Assert.Contains("SetNeutralButton(\"Attach\", (_, _) => PickMediaForGroup(recipients))", body);
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

    private static string ReadMailboxSource()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null
            && !File.Exists(Path.Combine(directory, "FocusPhone", "Post.Android", "MailboxArea.cs")))
        {
            directory = Path.GetDirectoryName(directory);
        }
        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate FocusPhone/Post.Android/MailboxArea.cs from the test output directory.");
        }
        return File.ReadAllText(Path.Combine(directory, "FocusPhone", "Post.Android", "MailboxArea.cs"));
    }
}
