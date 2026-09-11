using System.Text.RegularExpressions;
using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Guard;

/// <summary>
/// No screen offers an email address field for somebody who has no email address.
///
/// <para>The girls' days reach the page on calendars shared between the parents, so their columns
/// are filled by the calendar an event came from — never by an invitation, because there is no
/// account to invite. An address field offered for one of them is a field that <b>cannot work</b>:
/// whatever is typed into it never matches a guest, nothing errors, and every event goes on landing
/// exactly where it was landing before. That is the shape of a settings screen that does nothing,
/// and it is invisible from the code — the rows are built in a loop, and the loop that is wrong
/// looks exactly like the loop that is right.</para>
///
/// <para>So the loop is pinned: address rows are built over
/// <see cref="PersonIdentity.WithMailboxes"/>, never over <c>ColumnSet.People</c>.</para>
/// </summary>
public class AddressFieldsTests
{
    [Fact]
    public void AddressRowsAreBuiltOverThePeopleWhoHaveAddresses()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(Root(), "Foreword.Android"), "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);

            // Every loop that builds an address row, whichever collection it walks. The block is
            // read by counting braces rather than matched by a pattern: a brace-balancing regex
            // written here ran past the loop's own closing brace and reported the wrong loop, which
            // is a guard that fails for a reason that is not the bug.
            foreach (Match match in Regex.Matches(source, @"foreach\s*\(\s*var\s+\w+\s+in\s+(?<over>[\w.]+)\s*\)\s*\{"))
            {
                var body = Block(source, match.Index + match.Length - 1);
                if (!body.Contains("Address", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var over = match.Groups["over"].Value;
                if (over is not ("PersonIdentity.WithMailboxes" or "settings.Mapping.PersonAddresses"))
                {
                    offenders.Add($"{Path.GetFileName(file)}: foreach over {over}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These build an address field for everybody, including the two who have no email "
            + "address at all — a field that can never match anything and never says so:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// And the routing itself still reads addresses for whoever has one. The guard above would pass
    /// happily if address routing were deleted outright, which would take the one signal that is a
    /// fact rather than a guess with it.
    /// </summary>
    [Fact]
    public void AnInvitedParentIsStillRoutedByTheirAddress()
    {
        var routing = new ColumnRouting(
            Column: null,
            Addresses: new Dictionary<PersonColumn, IReadOnlyList<string>>
            {
                [PersonColumn.Ashley] = ["ashley@example.com"],
            },
            Names: new Dictionary<PersonColumn, IReadOnlyList<string>>(),
            Default: PersonColumn.Matt);

        var invited = new CalendarEvent
        {
            Title = "Parent evening",
            Guests = ["ashley@example.com"],
            Start = DateTime.Today.AddHours(19),
            End = DateTime.Today.AddHours(20),
        };

        Assert.Equal([PersonColumn.Ashley], ColumnSet.Resolve(invited, routing));
    }

    /// <summary>The braced block starting at <paramref name="open"/>, counted rather than matched.</summary>
    private static string Block(string source, int open)
    {
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }

        return source[open..];
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
