namespace Foreword.Infrastructure.Maps;

/// <summary>
/// Where the household's day starts: an address for the drives, a point for the forecast.
/// </summary>
public sealed record HomeLocation(string Address, double Latitude, double Longitude)
{
    public static HomeLocation None { get; } = new(string.Empty, 0, 0);

    public bool HasAddress => !string.IsNullOrWhiteSpace(Address);

    /// <summary>"Not set" is both being zero, not either — zero is a real coordinate, in the Atlantic.</summary>
    public bool HasPoint => Latitude != 0 || Longitude != 0;
}

/// <summary>
/// Fills in the house from Google rather than asking for it (Settings used to ask twice).
///
/// <para>The address comes from the signed-in account's own home address, the one Google Maps labels
/// Home; the point comes from geocoding whatever address there is. Only what is missing is looked up
/// — an address the owner typed is theirs and is never replaced by the account's, and a point already
/// set is never re-geocoded, since Maps Platform bills per request.</para>
/// </summary>
public class HomeLocator(GoogleGeocodingClient geocoder)
{
    /// <param name="accountHomeAddress">
    /// The account's home address, or null. A function rather than a value because it may put a
    /// consent screen in front of the owner, so it is asked only when the address is actually missing.
    /// </param>
    public async Task<HomeLocation> Fill(HomeLocation current, Func<Task<string?>> accountHomeAddress)
    {
        var home = current;

        if (!home.HasAddress)
        {
            string? found;
            try
            {
                found = await accountHomeAddress();
            }
            catch (Exception)
            {
                // A dismissed consent screen or a dropped network costs the lookup, not the page: the
                // address field simply stays showing.
                return current;
            }

            if (string.IsNullOrWhiteSpace(found))
            {
                return current;
            }

            home = home with { Address = found.Trim() };
        }

        if (!home.HasPoint)
        {
            try
            {
                if (await geocoder.Locate(home.Address) is { } point)
                {
                    home = home with { Latitude = point.Latitude, Longitude = point.Longitude };
                }
            }
            catch (Exception)
            {
                // As above: the point field shows and asks.
            }
        }

        return home;
    }
}
