namespace IconStudio;

// Renders the Paper & Ink adaptive launcher-icon family for all six apps
// (DESIGN.md §8). Usage:
//   dotnet run --project Tools/IconStudio -- --repo-root .            # write resources
//   dotnet run --project Tools/IconStudio -- --preview out/previews   # 1024px composites
public static class Program
{
    public static int Main(string[] args)
    {
        string? repoRoot = null;
        string? previewDir = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--repo-root")
            {
                repoRoot = Path.GetFullPath(args[i + 1]);
            }
            if (args[i] == "--preview")
            {
                previewDir = Path.GetFullPath(args[i + 1]);
            }
        }
        if (repoRoot is null && previewDir is null)
        {
            Console.Error.WriteLine("Usage: IconStudio --repo-root <path> | --preview <dir>");
            return 1;
        }

        foreach (var spec in IconSpecs.All)
        {
            if (repoRoot is not null)
            {
                var files = AdaptiveIconRenderer.Write(repoRoot, spec);
                Console.WriteLine($"{spec.App}: wrote {files.Count} files under {spec.ResourceRoot}");
            }
            if (previewDir is not null)
            {
                Console.WriteLine($"{spec.App}: preview {AdaptiveIconRenderer.WritePreview(previewDir, spec)}");
            }
        }
        if (repoRoot is not null)
        {
            var branded = BrandAssets.Write(repoRoot);
            Console.WriteLine($"branding + grain: wrote {branded.Count} files");
        }
        return 0;
    }
}
