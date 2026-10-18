using System.Text.RegularExpressions;

namespace Constitution.Tests.Documents;

/// <summary>
/// C-2.1.1: no document file names a type size, a colour or a page size of its own. The same shape as
/// Folio's PointLadderTests guard, pointed at <c>Constitution.Infrastructure/Documents</c> (where the
/// painting happens) and <c>Constitution.Domain/Documents</c> (where the geometry lives, which may
/// hold lengths but never a type size or a colour). Sizes arrive from <c>PaperTypeScale</c>, colours
/// from <c>PaperPalette</c>/<c>AppIdentityTokens</c>, the page from <c>ConstitutionPage</c>.
/// </summary>
public class DocumentLiteralTests
{
    private static readonly Regex Comments = new(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static string Hex() => "#" + "[0-9A-Fa-f]{6}";

    /// <summary>What is wrong with one source text, as short kinds; the file and line are the caller's.</summary>
    internal static IReadOnlyList<string> Offences(string source)
    {
        var code = Comments.Replace(source, string.Empty);
        var found = new List<string>();

        // Both spellings: new SKFont(face, 12) and the target-typed new(face, 12) the painters use.
        if (Regex.IsMatch(code, @"new\s+SKFont\s*\([^)]*,\s*\d+(\.\d+)?f?\s*\)|EmbeddedTypefaces\.\w+\s*,\s*\d", RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            found.Add("font built at a literal size");
        }

        if (Regex.IsMatch(code, @"\b(?:TextSize|Size)\s*=\s*\d", RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            found.Add("size assigned a number");
        }

        if (Regex.IsMatch(code, @"\bSKColors\.", RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            found.Add("named SKColors colour");
        }

        if (Regex.IsMatch(code, @"new\s+SKColor\s*\(\s*(?:0x|\d)", RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            found.Add("colour built from numbers");
        }

        if (Regex.IsMatch(code, Hex(), RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            found.Add("hex colour literal");
        }

        if (Regex.IsMatch(code, @"\b54(\.0)?f?\b", RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            found.Add("the dose size typed out");
        }

        foreach (Match call in Regex.Matches(code, @"\.BeginPage\s*\(([^)]*)\)", RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            if (!call.Groups[1].Value.Contains("ConstitutionPage.", StringComparison.Ordinal))
            {
                found.Add("page begun at a size other than ConstitutionPage");
            }
        }

        return found;
    }

    private static IReadOnlyList<string> DocumentFiles() =>
        ConstitutionSource.Files(".cs")
            .Where(file =>
            {
                var relative = ConstitutionSource.Relative(file);
                return relative.StartsWith("Constitution.Infrastructure/Documents/", StringComparison.Ordinal)
                    || relative.StartsWith("Constitution.Domain/Documents/", StringComparison.Ordinal);
            })
            .ToList();

    [Fact]
    public void NoDocumentNamesItsOwnSizeOrColour()
    {
        var files = DocumentFiles();
        Assert.True(files.Count >= 8, $"The guard found only {files.Count} document files; it is not reading the real folders.");

        var offenders = files
            .SelectMany(file => Offences(File.ReadAllText(file)).Select(kind => $"{ConstitutionSource.Relative(file)}: {kind}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Document files naming their own size or colour:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheGuardReadsAPaintingFolder()
    {
        var painting = DocumentFiles()
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Infrastructure/", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .Where(text => text.Contains("PaperTypeScale.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(painting);
    }

    [Fact]
    public void TheDetectorFiresOnEachPlantedOffence()
    {
        // Assembled at run time so this file holds no literal of its own that the guard would flag.
        var font = "var f = new SKFont(face, " + "54f);";
        var targetTyped = "SKFont Dose { get; } = new(EmbeddedTypefaces.SerifBlack, " + "50f);";
        var size = "paint.TextSize = " + "12;";
        var named = "var c = SKColors" + ".Red;";
        var built = "var c = new SKColor(" + "0x12, 0x34, 0x56);";
        var hex = "var h = \"" + "#" + "A1B2C3\";";
        var dose = "const float Dose = " + "54f;";
        var page = "sink.BeginPage(" + "400f, 500f);";

        Assert.Contains("font built at a literal size", Offences(font));
        Assert.Contains("font built at a literal size", Offences(targetTyped));
        Assert.Contains("size assigned a number", Offences(size));
        Assert.Contains("named SKColors colour", Offences(named));
        Assert.Contains("colour built from numbers", Offences(built));
        Assert.Contains("hex colour literal", Offences(hex));
        Assert.Contains("the dose size typed out", Offences(dose));
        Assert.Contains("page begun at a size other than ConstitutionPage", Offences(page));
    }

    [Fact]
    public void TheDetectorLeavesTheRegisteredWaysAlone()
    {
        Assert.Empty(Offences("var f = new SKFont(face, PaperTypeScale.Dose);"));
        Assert.Empty(Offences("sink.BeginPage(ConstitutionPage.Width, ConstitutionPage.Height);"));
        Assert.Empty(Offences("var c = PaperPalette.Resolve(PaperRole.InkMuted, profile); // 54 and #FFFFFF in a comment"));
    }
}
