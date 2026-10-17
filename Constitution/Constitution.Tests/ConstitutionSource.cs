namespace Constitution.Tests;

/// <summary>
/// Finds Constitution's own files for the guards that read the repository by content. Scoped to
/// the folder holding <c>Constitution.sln</c>, not the whole repository: other suite apps have
/// their own <c>MainActivity.cs</c>, and scanning them would turn that into a false failure here.
/// </summary>
public static class ConstitutionSource
{
    /// <summary>Walks up from the test bin directory to the folder containing Constitution.sln.</summary>
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Constitution.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Could not locate Constitution.sln above " + AppContext.BaseDirectory);
        }

        return directory.FullName;
    }

    /// <summary>Every file under Constitution/ with one of the given extensions, skipping build output.</summary>
    public static IReadOnlyList<string> Files(params string[] extensions)
    {
        var root = Root();
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !Path.GetRelativePath(root, file)
                .Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();
    }

    public static string Relative(string absolute) =>
        Path.GetRelativePath(Root(), absolute).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>True when the file is the named guard's own source, which has to name what it forbids.</summary>
    public static bool IsGuard(string file, string guardFileName) =>
        Path.GetFileName(file).Equals(guardFileName, StringComparison.Ordinal);
}
