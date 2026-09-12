using System.Text.RegularExpressions;
using Xunit;

namespace Foreword.Tests.Guard;

/// <summary>
/// No person is anybody's default (Release I).
///
/// <para>Foreword has only ever run on one phone, so one person became the answer to questions
/// nobody had noticed were questions: where a hand-written note goes when nobody is named on it,
/// which column an event falls into when none of the three sources can tell, what a
/// <c>CalendarEvent</c> is in before anyone sets it. Each of those was
/// <c>PersonColumn.Matt</c>, written as an initialiser or a <c>??</c>, and on a second install every
/// one of them is wrong in the same silent way — the event lands on the wrong person's day, nothing
/// errors, and the only sign is somebody reading their sheet and not recognising it.</para>
///
/// <para><b>Naming a person is not the offence.</b> The household roster names all four, the
/// composer gives each column its own hue, and the vision prompt lists the four a note can be
/// attributed to — all of those are about the <i>columns</i>, which exist on both installs. What is
/// forbidden is a person standing in as the answer when the real answer is "whoever this phone
/// belongs to", which is <see cref="Domain.Install.OwnerColumn"/>.</para>
/// </summary>
public class NobodyIsTheDefaultTests
{
    /// <summary>
    /// Files that legitimately enumerate the household: the roster itself, the per-column styling,
    /// and the keyword seeds. None of them is answering "whose phone is this".
    /// </summary>
    private static readonly string[] Roster =
    [
        "PersonColumn.cs",
        "PersonIdentity.cs",
        "ColumnRouting.cs",
        "DayPdfComposer.cs",
        "OwnerColumn.cs",
    ];

    [Fact]
    public void NoPersonStandsInForWhoeverThisPhoneBelongsTo()
    {
        var offenders = new List<string>();

        foreach (var project in new[] { "Foreword.Domain", "Foreword.Infrastructure", "Foreword.Android" })
        {
            foreach (var file in Directory.EnumerateFiles(
                Path.Combine(Root(), project), "*.cs", SearchOption.AllDirectories))
            {
                if (Skip(file) || Roster.Contains(Path.GetFileName(file)))
                {
                    continue;
                }

                var source = Stripped(File.ReadAllText(file));

                // A fallback: "?? PersonColumn.Someone".
                foreach (Match match in Regex.Matches(source, @"\?\?\s*PersonColumn\.(?<who>\w+)"))
                {
                    offenders.Add($"{Path.GetFileName(file)}: ?? PersonColumn.{match.Groups["who"].Value}");
                }

                // An initialiser: "= PersonColumn.Someone" or "= [PersonColumn.Someone]".
                foreach (Match match in Regex.Matches(
                    source, @"=\s*\[?\s*PersonColumn\.(?<who>\w+)\s*\]?\s*;"))
                {
                    offenders.Add($"{Path.GetFileName(file)}: = PersonColumn.{match.Groups["who"].Value}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These make one person the answer to a question whose answer is whoever this phone "
            + "belongs to. On a second install each is silently wrong:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// And the thing that replaced them still works. The guard above would pass just as happily if
    /// the resolution were deleted outright, which would take the only per-install answer with it.
    /// </summary>
    [Fact]
    public void TheInstallStillHasAnOwner()
    {
        var resolved = Domain.Install.OwnerColumn.Resolve(
            chosen: null,
            accountEmail: "ashley@example.com",
            new Dictionary<Domain.Models.PersonColumn, IReadOnlyList<string>>
            {
                [Domain.Models.PersonColumn.Ashley] = ["ashley@example.com"],
            });

        Assert.Equal(Domain.Models.PersonColumn.Ashley, resolved);
    }

    private static bool Skip(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>
    /// Comments and strings out, so a doc comment explaining why a person is <i>not</i> the default
    /// is not itself reported — which would make the guard argue with its own documentation.
    /// </summary>
    private static string Stripped(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        var withoutLines = Regex.Replace(withoutBlocks, @"//[^\n]*", " ");

        return Regex.Replace(withoutLines, @"@?""(?:[^""\\\n]|\\.)*""", " \"\" ");
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Foreword.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
