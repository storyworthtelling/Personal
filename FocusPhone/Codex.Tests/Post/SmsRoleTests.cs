using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Google Messages is meant to be the texting app: holding the role is what keeps RCS on, and
/// Post reads and sends through it. So the launch check now asks the opposite question it used
/// to — not "does Post have the role" but "does Messages".
/// </summary>
public class SmsRoleTests
{
    // The intended state says nothing: a working phone announcing that it works is noise.
    [Fact]
    public void MessagesHoldingTheRoleIsSilent()
    {
        Assert.False(SmsRole.Decide(TextingAppHolder.GoogleMessages, messagesInstalled: true).Speaks);
    }

    // The upgrade case: Post took the role under the old design and still has it, so RCS is off.
    [Fact]
    public void PostStillHoldingTheRoleAsksForItToGoBack()
    {
        var notice = SmsRole.Decide(TextingAppHolder.Post, messagesInstalled: true);

        Assert.True(notice.Speaks);
        Assert.True(notice.OffersMessages);
        Assert.Contains("RCS", notice.Detail);
    }

    [Theory]
    [InlineData(TextingAppHolder.Other)]
    [InlineData(TextingAppHolder.Nobody)]
    public void AnyoneElseHoldingTheRoleOffersMessages(TextingAppHolder holder)
    {
        var notice = SmsRole.Decide(holder, messagesInstalled: true);

        Assert.True(notice.Speaks);
        Assert.True(notice.OffersMessages);
    }

    // There is nothing to open, so no button is offered: one that opens nothing is worse
    // than saying plainly what has to happen first.
    [Fact]
    public void WithoutMessagesInstalledNothingIsOffered()
    {
        var notice = SmsRole.Decide(TextingAppHolder.Nobody, messagesInstalled: false);

        Assert.True(notice.Speaks);
        Assert.False(notice.OffersMessages);
        Assert.Contains("not installed", notice.Headline);
    }

    [Theory]
    [InlineData("com.google.android.apps.messaging", "com.storyworthtelling.focusphone.communication", TextingAppHolder.GoogleMessages)]
    [InlineData("com.storyworthtelling.focusphone.communication", "com.storyworthtelling.focusphone.communication", TextingAppHolder.Post)]
    [InlineData("com.example.texts", "com.storyworthtelling.focusphone.communication", TextingAppHolder.Other)]
    [InlineData(null, "com.storyworthtelling.focusphone.communication", TextingAppHolder.Nobody)]
    [InlineData("", "com.storyworthtelling.focusphone.communication", TextingAppHolder.Nobody)]
    public void TheHolderIsReadFromTheDefaultPackage(string? defaultPackage, string self, TextingAppHolder expected)
    {
        Assert.Equal(expected, SmsRole.HolderOf(defaultPackage, self));
    }
}
