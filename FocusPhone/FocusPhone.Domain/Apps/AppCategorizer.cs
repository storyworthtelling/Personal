namespace FocusPhone.Domain.Apps;

public record CategorizerData(
    IReadOnlyCollection<string> BlockedPackages,
    IReadOnlyCollection<string> AllowedPackages);

/// <summary>
/// Decides the fate of every installed package. Fail-closed by design:
/// anything not positively cleared is quarantined, and a block outlives
/// uninstall/reinstall because it is keyed on the package name.
/// </summary>
public class AppCategorizer
{
    private readonly HashSet<string> blockedPackages;
    private readonly HashSet<string> allowedPackages;

    public AppCategorizer()
    {
        blockedPackages = [];
        allowedPackages = [];
    }

    public AppCategorizer(CategorizerData persisted)
    {
        blockedPackages = [.. persisted.BlockedPackages];
        allowedPackages = [.. persisted.AllowedPackages];
    }

    public AppVerdict Categorize(AppInfo app, ContentRating rating)
    {
        if (blockedPackages.Contains(app.PackageName))
        {
            return AppVerdict.Blocked;
        }
        if (allowedPackages.Contains(app.PackageName))
        {
            return AppVerdict.Allowed;
        }
        if (app.HandlesHttpViewIntents || app.IsCategoryGame)
        {
            return AppVerdict.Blocked;
        }
        if (rating is ContentRating.Mature or ContentRating.Unknown)
        {
            return AppVerdict.Quarantined;
        }
        return AppVerdict.Allowed;
    }

    public void Block(string packageName)
    {
        allowedPackages.Remove(packageName);
        blockedPackages.Add(packageName);
    }

    public void Allow(string packageName)
    {
        allowedPackages.Add(packageName);
    }

    public CategorizerData Export() => new([.. blockedPackages], [.. allowedPackages]);
}
