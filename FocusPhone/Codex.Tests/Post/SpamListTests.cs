using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Spam is judged once, by the owner, and then never surfaces again: the same number, or
/// a message worded almost exactly like one they marked. The matching is deliberately
/// narrow on the wording side — a bad match hides a real message for good, so a short or
/// merely similar message must not be swept up by a near miss.
/// </summary>
public class SpamListTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static TimelineItem Text(string sender, string snippet, string id = "m") =>
        new(TimelineItemKind.Message, ContactKeyNormalizer.NormalizePhone(sender), Now, snippet,
            Incoming: true, SourceId: id);

    private static TimelineItem Call(string sender, string id = "c") =>
        new(TimelineItemKind.Call, ContactKeyNormalizer.NormalizePhone(sender), Now, null,
            Incoming: true, SourceId: id);

    private static TimelineItem Mail(string address, string subject, string body, string id = "e") =>
        new(TimelineItemKind.Email, ContactKeyNormalizer.NormalizeEmail(address), Now, body,
            Incoming: true, SourceId: id)
        {
            Subject = subject,
            Body = body,
            FromAddress = address,
        };

    [Fact]
    public void A_marked_number_stays_marked_whatever_it_says_next()
    {
        var spam = new SpamList();
        spam.Mark([Text("(555) 010-0199", "Claim your prize today")]);

        Assert.True(spam.Matches(Text("555-010-0199", "Totally different words entirely")));
    }

    [Fact]
    public void A_number_is_matched_however_it_was_spelled()
    {
        var spam = new SpamList();
        spam.Mark([Call("+1 (555) 010-0199")]);

        Assert.True(spam.Matches(Call("5550100199")));
    }

    [Fact]
    public void A_different_number_is_not_matched_by_sender()
    {
        var spam = new SpamList();
        spam.Mark([Text("555-010-0199", "Claim your prize today")]);

        Assert.False(spam.Matches(Text("555-010-0200", "Lunch on Thursday?")));
    }

    [Fact]
    public void A_near_identical_message_from_another_number_is_matched()
    {
        // Spam rotates numbers and the figures in its wording. Digit runs are treated as one
        // token, so only the words have to agree.
        var spam = new SpamList();
        spam.Mark([Text("555-010-0199", "Congrats! You won 500 dollars, claim at example.com now")]);

        Assert.True(spam.Matches(Text("555-010-0777", "Congrats! You won 900 dollars, claim at example.com now")));
    }

    [Fact]
    public void A_merely_similar_message_is_not_matched()
    {
        var spam = new SpamList();
        spam.Mark([Mail("offers@example.com", "Hello", "Your package is out for delivery today")]);

        Assert.False(spam.Matches(Text("555-010-0300", "Your package has been delivered today")));
    }

    [Fact]
    public void A_short_message_matches_only_when_it_is_exactly_the_same()
    {
        // "Call me back" would otherwise match "Call me back now" at 75% overlap, and short
        // ordinary messages are exactly the ones a fuzzy match must not swallow.
        var spam = new SpamList();
        spam.Mark([Text("555-010-0199", "Call me back")]);

        Assert.False(spam.Matches(Text("555-010-0300", "Call me back now")));
        Assert.True(spam.Matches(Text("555-010-0300", "call me back")));
    }

    [Fact]
    public void Unrelated_wording_from_a_new_number_is_not_matched()
    {
        var spam = new SpamList();
        spam.Mark([Text("555-010-0199", "Claim your prize today at the online portal")]);

        Assert.False(spam.Matches(Text("555-010-0300", "Dinner at the usual place on Friday evening")));
    }

    [Fact]
    public void Email_is_judged_by_its_subject_and_body_as_well_as_its_sender()
    {
        var spam = new SpamList();
        spam.Mark([Mail("offers@example.com", "Act now", "Limited time offer on all products")]);

        Assert.True(spam.Matches(Mail("other@example.net", "Act now", "Limited time offer on all products", "e2")));
        Assert.False(spam.Matches(Mail("other@example.net", "Minutes", "Attached are the minutes from Tuesday", "e3")));
    }

    [Fact]
    public void A_group_message_from_a_spam_sender_is_not_hidden_on_sender_alone()
    {
        // A sender match means "that number, one to one". A group the number happens to be
        // part of is a conversation with other people in it, so it is judged on its wording.
        var spam = new SpamList();
        spam.Mark([Text("555-010-0199", "Claim your prize today")]);

        var group = new TimelineItem(TimelineItemKind.Message, "group:7", Now, "Are we still on for Saturday?",
            Incoming: true, SourceId: "g")
        {
            GroupId = "7",
        };

        Assert.False(spam.Matches(group));
    }

    [Fact]
    public void Unmarking_restores_exactly_what_was_marked()
    {
        var spam = new SpamList();
        var spammer = Text("555-010-0199", "Claim your prize today");
        spam.Mark([spammer]);

        spam.Unmark([spammer]);

        Assert.False(spam.Matches(spammer));
        Assert.False(spam.Matches(Text("555-010-0199", "Claim your prize today", "m2")));
        Assert.Empty(spam.ToStored());
    }

    [Fact]
    public void Filter_drops_what_matches_and_keeps_the_rest_in_order()
    {
        var spam = new SpamList();
        spam.Mark([Text("555-010-0199", "Claim your prize today")]);
        var keepA = Text("555-010-0300", "Lunch on Thursday?", "a");
        var drop = Text("555-010-0199", "Anything at all", "b");
        var keepB = Mail("friend@example.org", "Hi", "Are you free this weekend?", "c");

        var kept = spam.Filter([keepA, drop, keepB]).ToList();

        Assert.Equal(new[] { keepA, keepB }, kept);
    }

    [Fact]
    public void The_stored_form_round_trips()
    {
        var spam = new SpamList();
        spam.Mark([
            Text("555-010-0199", "Congrats! You won 500 dollars, claim at example.com now"),
            Call("555-010-0199"),
        ]);

        var reloaded = SpamList.From(spam.ToStored());

        Assert.True(reloaded.Matches(Call("5550100199")));
        Assert.True(reloaded.Matches(Text("555-010-0777", "Congrats! You won 900 dollars, claim at example.com now")));
        Assert.False(reloaded.Matches(Text("555-010-0300", "Lunch on Thursday?")));
    }

    [Fact]
    public void Fingerprint_treats_digit_runs_as_one_token_and_ignores_case_and_punctuation()
    {
        Assert.Equal(
            SpamList.Fingerprint("Your code is 123456."),
            SpamList.Fingerprint("your CODE, is 98!"));
    }
}

/// <summary>Whether a sender is already in the owner's address book.</summary>
public class RolodexMembershipTests
{
    [Fact]
    public void A_sender_in_the_book_by_phone_is_known_whatever_the_spelling()
    {
        var book = new[] { "(555) 010-0199", "friend@example.org" };

        Assert.True(RolodexMembership.IsKnown(book, [ContactKeyNormalizer.NormalizePhone("+1 555 010 0199")]));
    }

    [Fact]
    public void A_sender_in_the_book_by_email_is_known_regardless_of_case()
    {
        var book = new[] { "Friend@Example.org" };

        Assert.True(RolodexMembership.IsKnown(book, [ContactKeyNormalizer.NormalizeEmail("friend@example.org")]));
    }

    [Fact]
    public void A_sender_not_in_the_book_is_unknown()
    {
        var book = new[] { "(555) 010-0199" };

        Assert.False(RolodexMembership.IsKnown(book, [ContactKeyNormalizer.NormalizePhone("555-010-0300")]));
    }

    [Fact]
    public void A_correspondence_is_known_if_any_of_its_keys_is_in_the_book()
    {
        // A merged thread can carry more than one key for one person; one known key means
        // the person is known, and offering to add them again would be wrong.
        var book = new[] { "(555) 010-0199" };

        Assert.True(RolodexMembership.IsKnown(book,
            [ContactKeyNormalizer.NormalizePhone("555-010-0300"), ContactKeyNormalizer.NormalizePhone("555-010-0199")]));
    }
}
