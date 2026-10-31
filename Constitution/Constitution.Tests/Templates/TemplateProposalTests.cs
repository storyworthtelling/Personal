using System.Reflection;
using System.Text.Json;
using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.Workouts;
using static Constitution.Tests.MealAnswers.MealAnswerData;
using static Constitution.Tests.Templates.TemplateData;

namespace Constitution.Tests.Templates;

/// <summary>Claude's proposals: what is asked, how a reply is read, and how proposals enter the library without entering the rotation.</summary>
public class TemplateProposalTests
{
    private static readonly TemplateRequest Ask = new(
        3,
        [MealTemplateSlot.WeekendBreakfast, MealTemplateSlot.BoxFruit],
        [new TemplateKnown(MealTemplateSlot.WeekendBreakfast, "Oat pancakes")],
        [new TemplateKnown(MealTemplateSlot.BoxFruit, "Cold melon")],
        [new TemplateKnown(MealTemplateSlot.BoxFruit, "Kiwi halves")],
        []);

    [Fact]
    public void AGoodReplyIsRead()
    {
        var drafts = TemplateProposalParser.Parse("[{\"slot\":\"weekend-breakfast\",\"words\":\"Fruit salad and toast\"},{\"slot\":\"box-fruit\",\"words\":\"Pear slices\"}]", Ask);

        Assert.Equal([new TemplateDraft(MealTemplateSlot.WeekendBreakfast, "Fruit salad and toast"), new TemplateDraft(MealTemplateSlot.BoxFruit, "Pear slices")], drafts);
    }

    [Fact]
    public void ProseAndFencesAroundTheListAreIgnored()
    {
        var reply = "Here you go:\n```json\n[{\"slot\":\"box-fruit\",\"words\":\"Pear slices\"}]\n```\nEnjoy.";

        Assert.Equal([new TemplateDraft(MealTemplateSlot.BoxFruit, "Pear slices")], TemplateProposalParser.Parse(reply, Ask));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no list at all")]
    [InlineData("[{\"slot\":")]
    [InlineData("{\"slot\":\"box-fruit\",\"words\":\"Pear\"}")]
    [InlineData("[1,2,3]")]
    [InlineData("[null,\"x\",[]]")]
    public void AnUnusableReplyProposesNothing(string? reply)
    {
        Assert.Empty(TemplateProposalParser.Parse(reply, Ask));
    }

    [Fact]
    public void AnOverlongReplyProposesNothing()
    {
        var reply = "[{\"slot\":\"box-fruit\",\"words\":\"Pear slices\"}]" + new string(' ', TemplateProposalParser.MaxReplyLength);

        Assert.Empty(TemplateProposalParser.Parse(reply, Ask));
    }

    [Fact]
    public void BadItemsAreDroppedAndGoodOnesKept()
    {
        var reply = JsonSerializer.Serialize(new object[]
        {
            new { slot = "box-fruit", words = "Pear slices" },
            new { slot = "weekday-lunch", words = "Not asked for" },
            new { slot = "nonsense", words = "Unknown slot" },
            new { slot = "box-fruit", words = 7 },
            new { slot = "box-fruit", words = "   " },
            new { slot = "box-fruit", words = new string('x', MealTemplate.MaxWordsLength + 1) },
            new { slot = "box-fruit", words = "An extraordinarily elaborate arrangement of seasonal fruit" },
            new { words = "No slot" },
        });

        Assert.Equal([new TemplateDraft(MealTemplateSlot.BoxFruit, "Pear slices")], TemplateProposalParser.Parse(reply, Ask));
    }

    [Fact]
    public void TheCountBoundsTheDrafts()
    {
        var reply = JsonSerializer.Serialize(Enumerable.Range(1, 10).Select(number => new { slot = "box-fruit", words = $"Fruit idea {(char)('a' + number)}" }));

        Assert.Equal(3, TemplateProposalParser.Parse(reply, Ask).Count);
    }

    [Fact]
    public void WhatIsAlreadyKnownIsNotProposedAgain()
    {
        var reply = "[{\"slot\":\"weekend-breakfast\",\"words\":\"  oat   PANCAKES \"},{\"slot\":\"box-fruit\",\"words\":\"cold melon\"},{\"slot\":\"box-fruit\",\"words\":\"Kiwi Halves\"},{\"slot\":\"box-fruit\",\"words\":\"Pear slices\"},{\"slot\":\"box-fruit\",\"words\":\"pear slices\"}]";

        Assert.Equal([new TemplateDraft(MealTemplateSlot.BoxFruit, "Pear slices")], TemplateProposalParser.Parse(reply, Ask));
    }

    [Fact]
    public void ASaidApprovedIsNotBelieved()
    {
        var reply = "[{\"slot\":\"box-fruit\",\"words\":\"Pear slices\",\"status\":\"approved\",\"approval\":\"explicit\"}]";
        var drafts = TemplateProposalParser.Parse(reply, Ask);

        var merged = TemplateProposals.Merge(TemplateLibrary.Empty, drafts);

        var added = Assert.Single(merged.Added);
        Assert.Equal(TemplateStatus.Proposed, added.Status);
        Assert.Equal(TemplateApproval.None, added.Approval);
        Assert.Empty(merged.Library.Approved);
    }

    [Fact]
    public async Task NoAuthorIsUnavailableAndProposesNothing()
    {
        var result = await new NoTemplateAuthor().ProposeAsync(Ask, CancellationToken.None);

        Assert.Equal(AuthorUnavailable.NotConfigured, Assert.IsType<TemplateAuthored.Unavailable>(result).Reason);
    }

    private sealed class FakeAuthor(string reply) : ITemplateAuthor
    {
        public TemplateRequest? Seen { get; private set; }

        public Task<TemplateAuthored> ProposeAsync(TemplateRequest request, CancellationToken cancellation)
        {
            Seen = request;
            return Task.FromResult<TemplateAuthored>(new TemplateAuthored.Drafts(TemplateProposalParser.Parse(reply, request)));
        }
    }

    [Fact]
    public async Task AFakeAuthorsReplyBecomesProposals()
    {
        var author = new FakeAuthor("[{\"slot\":\"box-fruit\",\"words\":\"Pear slices\"}]");
        var request = TemplateRequest.From(Full(), [], [MealTemplateSlot.BoxFruit], 2);

        var result = Assert.IsType<TemplateAuthored.Drafts>(await author.ProposeAsync(request, CancellationToken.None));
        var merged = TemplateProposals.Merge(Full(), result.Proposals);

        Assert.Equal("Pear slices", Assert.Single(merged.Library.Proposed).Words);
        Assert.Equal(Full().Approved.Count, merged.Library.Approved.Count);
    }

    // --- the request ---

    [Fact]
    public void TheRequestHoldsApprovedAndRejectedWordsAndNothingElse()
    {
        var library = Lib(
            Approved("tpl-box-fruit-apple", MealTemplateSlot.BoxFruit, "Apple slices"),
            Rejected("tpl-box-fruit-melon", MealTemplateSlot.BoxFruit, "Cold melon", "Robin says it makes Lee ill"),
            Proposed("tpl-box-fruit-pear", MealTemplateSlot.BoxFruit, "Pear slices"));

        var request = TemplateRequest.From(library, [], [MealTemplateSlot.BoxFruit], 2);
        var text = JsonSerializer.Serialize(request);

        Assert.Equal(["Apple slices"], request.Approved.Select(known => known.Words));
        Assert.Contains("Cold melon", request.Rejected.Select(known => known.Words));
        Assert.Equal(["Pear slices"], request.Pending.Select(known => known.Words));
        Assert.DoesNotContain("Robin", text);
        Assert.DoesNotContain("makes", text);
        Assert.DoesNotContain("tpl-", text);
    }

    [Fact]
    public void TheRequestCarriesTastesAsWordsWithNoPerson()
    {
        var sam = new PersonId("sam");
        var notes = new[]
        {
            new TasteNote(sam, TasteSubjectKind.Ingredient, "mushroom", TasteStance.Dislikes, TasteOrigin.Seeded, Week),
            new TasteNote(sam, TasteSubjectKind.Dish, "tpl-box-fruit-apple", TasteStance.Likes, TasteOrigin.Learned, Week),
            new TasteNote(new PersonId("lee"), TasteSubjectKind.Ingredient, "mushroom", TasteStance.Dislikes, TasteOrigin.Learned, Week),
        };

        var request = TemplateRequest.From(Full(), notes, [MealTemplateSlot.BoxFruit], 2);
        var text = JsonSerializer.Serialize(request);

        var hint = Assert.Single(request.Tastes);
        Assert.Equal(new TemplateTasteHint("ingredient", "mushroom", "dislikes"), hint);
        Assert.DoesNotContain("sam", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lee", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheRequestCountIsBounded()
    {
        Assert.Equal(TemplateRequest.MaxCount, TemplateRequest.From(Full(), [], [MealTemplateSlot.BoxFruit], 99).Count);
        Assert.Equal(1, TemplateRequest.From(Full(), [], [MealTemplateSlot.BoxFruit], 0).Count);
    }

    [Fact]
    public void TheRequestTypeCannotHoldAPersonOrAReading()
    {
        var allowed = new HashSet<Type>
        {
            typeof(int), typeof(string), typeof(MealTemplateSlot), typeof(TemplateKnown), typeof(TemplateTasteHint),
            typeof(IReadOnlyList<MealTemplateSlot>), typeof(IReadOnlyList<TemplateKnown>), typeof(IReadOnlyList<TemplateTasteHint>),
        };
        var members = new[] { typeof(TemplateRequest), typeof(TemplateKnown), typeof(TemplateTasteHint) }
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.DeclaringType == type));

        foreach (var property in members)
        {
            Assert.Contains(property.PropertyType, allowed);
        }
    }

    [Fact]
    public void TheRequestsNamesNameNoPersonOrBody()
    {
        var names = new[] { typeof(TemplateRequest), typeof(TemplateKnown), typeof(TemplateTasteHint) }
            .SelectMany(type => type.GetProperties().Select(property => property.Name));

        Assert.DoesNotContain(names, name => name.Contains("Person", StringComparison.Ordinal) || name.Contains("Body", StringComparison.Ordinal) || name.Contains("Name", StringComparison.Ordinal) || name.Contains("Reason", StringComparison.Ordinal));
    }

    // --- merging ---

    [Fact]
    public void ProposalsEnterAsProposedWithTemplateIds()
    {
        var merged = TemplateProposals.Merge(TemplateLibrary.Empty, [new TemplateDraft(MealTemplateSlot.BoxFruit, "Pear slices"), new TemplateDraft(MealTemplateSlot.SnackBar, "Oat bar")]);

        Assert.Equal(2, merged.Library.Proposed.Count);
        Assert.Empty(merged.Library.Approved);
        Assert.All(merged.Added, template =>
        {
            Assert.StartsWith(MealTemplate.IdPrefix, template.Id, StringComparison.Ordinal);
            Assert.True(template.Id.Length <= 60);
            template.EnsureValid();
        });
    }

    [Fact]
    public void TheSameWordsAreNeverAddedTwice()
    {
        var once = TemplateProposals.Merge(TemplateLibrary.Empty, [new TemplateDraft(MealTemplateSlot.BoxFruit, "Pear slices")]);
        var twice = TemplateProposals.Merge(once.Library, [new TemplateDraft(MealTemplateSlot.BoxFruit, " pear   SLICES")]);

        Assert.Empty(twice.Added);
        Assert.Equal(once.Library.Templates.Select(template => template.Id), twice.Library.Templates.Select(template => template.Id));
    }

    [Fact]
    public void ARejectedTemplateIsNeverProposedAgain()
    {
        var library = Lib(Rejected("tpl-box-fruit-melon", MealTemplateSlot.BoxFruit, "Cold melon", "too wet"));

        var merged = TemplateProposals.Merge(library, [new TemplateDraft(MealTemplateSlot.BoxFruit, "cold melon")]);

        Assert.Empty(merged.Added);
        Assert.Equal(TemplateStatus.Rejected, Assert.Single(merged.Library.Templates).Status);
    }

    [Fact]
    public void WordsThatShareAnIdStillGetOwnIds()
    {
        var merged = TemplateProposals.Merge(TemplateLibrary.Empty, [new TemplateDraft(MealTemplateSlot.BoxFruit, "Pear & apple"), new TemplateDraft(MealTemplateSlot.BoxFruit, "Pear, apple")]);

        Assert.Equal(2, merged.Added.Select(template => template.Id).Distinct().Count());
    }

    [Fact]
    public void AnOutstandingCapBoundsTheQueue()
    {
        var drafts = Enumerable.Range(0, 20).Select(number => new TemplateDraft(MealTemplateSlot.BoxFruit, $"Fruit idea {(char)('a' + number)}")).ToList();

        var merged = TemplateProposals.Merge(TemplateLibrary.Empty, drafts);
        var more = TemplateProposals.Merge(merged.Library, [new TemplateDraft(MealTemplateSlot.SnackBar, "Another bar")]);

        Assert.Equal(TemplateProposals.MaxOutstanding, merged.Library.Proposed.Count);
        Assert.Empty(more.Added);
    }

    [Fact]
    public void LongWordsStillMakeAValidShortId()
    {
        var merged = TemplateProposals.Merge(TemplateLibrary.Empty, [new TemplateDraft(MealTemplateSlot.BoxSandwich, "Cheese, cucumber and honey on soft rye")]);

        Assert.True(Assert.Single(merged.Added).Id.Length <= 60);
    }

    [Fact]
    public void MergingLeavesApprovedTemplatesAlone()
    {
        var merged = TemplateProposals.Merge(Full(), [new TemplateDraft(MealTemplateSlot.BoxFruit, "Pear slices")]);

        Assert.Equal(Full().Approved.Select(template => template.Id), merged.Library.Approved.Select(template => template.Id));
    }
}
