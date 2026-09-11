using Foreword.Domain.Models;
using Foreword.Infrastructure.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foreword.Tests.Pdf;

/// <summary>
/// Writes a realistic brief to disk when FOREWORD_BRIEF names a path, so the sheets can be looked at
/// rather than only asserted about. Skipped otherwise — a tool, not a check.
/// </summary>
public class SampleBriefTests
{
    [Fact]
    public void WriteSample()
    {
        var path = Environment.GetEnvironmentVariable("FOREWORD_BRIEF");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        static ArticleContent Story(string section, string title, string byline, bool abstractOnly,
            params string[] paragraphs) => new()
        {
            Title = title,
            Byline = byline,
            Section = section,
            AbstractOnly = abstractOnly,
            Body = [.. paragraphs.Select(p => new TextBlock { Text = p })],
        };

        var articles = new[]
        {
            Story("World", "The river towns are rebuilding, but not where they were",
                "By Amara Whitfield \u00b7 The New York Times", false,
                "Three years after the flood, the question in Marietta is no longer whether to rebuild. It is how far back from the water a town is willing to move, and who pays for the ground it leaves behind.",
                "The county bought out ninety-one properties along the east bank, most of them bungalows put up in the nineteen-fifties when the levee was new and the river seemed settled. What replaced them is not housing. It is a meadow, mown twice a year, that the county now calls a park and the former residents still call their street.",
                "Engineers describe the trade in plain terms. Every foot of setback buys a margin against a flood that arrives more often than the old maps promised, and every buyout removes a household from the ledger of people who must be warned, evacuated and, eventually, compensated again. The arithmetic is not in dispute. The difficulty is that it is being done to places rather than by them.",
                "State officials say the programme will expand. A bill moving through the legislature would let counties buy in advance of a disaster rather than after one, which planners have wanted for years and which residents\u2019 groups have already begun to organise against.",
                "In Marietta the split runs along an obvious line. Households that took the buyout have largely moved uphill, into subdivisions annexed in the last decade. Those who stayed are older, own outright, and describe the offer as a price rather than a choice."),
            Story("Business", "Chip makers are quietly rewriting their capacity promises",
                "By Daniel Okonjo \u00b7 The New York Times", true,
                "Three of the largest foundries have revised the timetables they gave investors last spring, pushing the bulk of new output into 2028 without formally withdrawing the earlier guidance."),
            Story("Opinion", "The case against the setback programme is weaker than it sounds",
                "By Priya Raghavan \u00b7 The New York Times", false,
                "Every objection to buying land before a flood rests on the same assumption: that the town as it stands is the town worth keeping.",
                "It is an assumption worth examining, because the alternative on offer is not preservation. It is the same street, three feet of water at a time, for as long as anyone can afford to stay."),
        };

        using var puzzle = new SkiaPageLayout();
        puzzle.DrawCentered("The crossword would be here",
            SkiaPageLayout.Serif(Storyworthtelling.Paper.PaperTypeScale.Title), 30);
        puzzle.NewPage();
        puzzle.DrawCentered("(page two)",
            SkiaPageLayout.Serif(Storyworthtelling.Paper.PaperTypeScale.Title), 30);

        var brief = new PdfComposer(NullLogger<PdfComposer>.Instance).Compose(
            articles,
            puzzle.ToPdf(),
            new DateOnly(2026, 9, 6),
            "A quiet Sunday, and the first one this month you could call cool. The lawn moved to today because Saturday would not stop raining \u2014 do it before noon and the rest of the day is yours. Eight stories below, one of which you will disagree with.");

        File.WriteAllBytes(path!, brief);
    }
}
