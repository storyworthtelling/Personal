using Constitution.Domain.Recipes.Screens;

namespace Constitution.Domain.Meals;

/// <summary>
/// Uses one photograph and lets it go, whatever the use came to. The capture folder, the one file in it and the rule that a file
/// outside it is neither read nor deleted are the recipe scan's (<see cref="CameraCaptureCache"/>); this is the same read-then-delete
/// order for a use that returns something other than lines, so a meal photo is held exactly as long as a recipe page is.
/// </summary>
public static class AtePhoto
{
    /// <summary>
    /// Runs <paramref name="use"/> on the photograph and deletes it in a <c>finally</c>: after a good use, after one that threw and
    /// after one that failed late. The failure still reaches the caller with its stack.
    /// </summary>
    public static async Task<T> UseAndForgetAsync<T>(
        string cacheRoot,
        string photoPath,
        Func<string, Task<T>> use,
        Func<string, bool> delete,
        Action<bool>? forgotten = null)
    {
        ArgumentNullException.ThrowIfNull(use);
        ArgumentNullException.ThrowIfNull(delete);

        if (!CameraCaptureCache.IsCaptureFile(cacheRoot, photoPath))
        {
            throw new ArgumentException("The photograph is not in the capture folder, so it was neither read nor deleted.", nameof(photoPath));
        }

        try
        {
            return await use(photoPath).ConfigureAwait(false);
        }
        finally
        {
            // The delete runs first and always; a null-conditional call would skip evaluating its argument when nobody is listening.
            var gone = delete(photoPath);
            forgotten?.Invoke(gone);
        }
    }
}
