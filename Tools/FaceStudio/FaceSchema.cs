using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace FaceStudio;

/// <summary>
/// Checks the face against Google's published Watch Face Format XSD for its version (committed in
/// <see cref="Folder"/>). The XSD is written for XSD 1.1, which lets an <c>xs:all</c> repeat its
/// elements; .NET validates XSD 1.0 only, so each <c>xs:all</c> is read here as a repeating
/// <c>xs:choice</c>, and a pattern's needless escapes of '_' and ':' are dropped. That keeps every element name, nesting and attribute type the format allows, and
/// loses only "at most once" and "required" child counts. Google's own validator, built from its
/// source, is the full check (P14-PROGRESS.md, A3).
/// </summary>
public static class FaceSchema
{
    public const string Folder = "Tools/FaceStudio/Specification/wff4";

    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";

    public static IReadOnlyList<string> Problems(XDocument face, string repoRoot)
    {
        var schemas = new XmlSchemaSet { XmlResolver = new RelaxingResolver() };
        var entry = Path.Combine(repoRoot, Folder, "watchface.xsd");
        using (var reader = XmlReader.Create(Relaxed(entry), new XmlReaderSettings(), new Uri(entry).AbsoluteUri))
        {
            schemas.Add(null, reader);
        }
        schemas.Compile();

        var problems = new List<string>();
        face.Validate(schemas, (sender, found) =>
            problems.Add($"{found.Severity} at line {found.Exception?.LineNumber}: {found.Message}"));
        return problems;
    }

    /// <summary>The schema file with each XSD 1.1 <c>xs:all</c> read as a repeating <c>xs:choice</c>.</summary>
    private static Stream Relaxed(string path)
    {
        var schema = XDocument.Load(path);
        foreach (var all in schema.Descendants(Xs + "all").ToList())
        {
            var choice = new XElement(Xs + "choice", new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"));
            foreach (var child in all.Elements())
            {
                child.Attribute("minOccurs")?.Remove();
                child.Attribute("maxOccurs")?.Remove();
                choice.Add(child);
            }
            all.ReplaceWith(choice);
        }

        // Two patterns escape '_' and ':', which XSD 1.1's regex reads as the characters themselves and
        // .NET's refuses; they are written plainly, which means the same.
        foreach (var pattern in schema.Descendants(Xs + "pattern"))
        {
            var value = pattern.Attribute("value")!;
            value.Value = value.Value.Replace(@"\_", "_", StringComparison.Ordinal).Replace(@"\:", ":", StringComparison.Ordinal);
        }

        var stream = new MemoryStream();
        schema.Save(stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>Resolves each <c>xs:include</c> to its committed file, relaxed the same way.</summary>
    private sealed class RelaxingResolver : XmlUrlResolver
    {
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            if (!absoluteUri.IsFile)
            {
                throw new XmlException($"The face's schema reaches outside its folder: {absoluteUri}.");
            }
            return Relaxed(absoluteUri.LocalPath);
        }
    }
}
