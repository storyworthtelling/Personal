namespace Foreword.Infrastructure.Maps;

/// <summary>
/// The Google Maps Platform key and the household's address, read live from settings so a key
/// entered on the phone reaches the next morning's run without a rebuild.
///
/// <para>One key for four APIs: Maps Platform bills a project rather than a product, so routes,
/// weather, pollen and air quality are the same credential with different APIs enabled on it.</para>
/// </summary>
public class MapsOptions
{
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Where the family's day starts. Text, because that is what the Routes API takes and what a
    /// calendar's location field contains.
    /// </summary>
    public string HomeAddress { get; set; } = string.Empty;

    // The weather, pollen and air-quality APIs take a point rather than an address.
    public double HomeLatitude { get; set; }
    public double HomeLongitude { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>The forecast needs somewhere to be about; 0,0 is the Atlantic.</summary>
    public bool HasLocation => IsConfigured && (HomeLatitude != 0 || HomeLongitude != 0);
}
