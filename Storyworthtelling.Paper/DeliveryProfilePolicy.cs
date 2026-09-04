namespace Storyworthtelling.Paper;

/// <summary>
/// The one place that encodes which <see cref="DeviceColorProfile"/> a delivery destination gets: the
/// owner's colour Paper Pro renders in colour, the grayscale rM2 renders in the designed gray ramp, and the
/// Drive PDF / in-app view renders at screen RGB. Callers resolve the profile here <em>before</em> rendering
/// the bytes, then hand the finished PDF to <see cref="IPaperDelivery"/>.
/// </summary>
public static class DeliveryProfilePolicy
{
    /// <summary>The profile for a reMarkable delivery, keyed on whether the paired tablet is a colour device.</summary>
    public static DeviceColorProfile ForRemarkable(bool colorDevice) =>
        colorDevice ? DeviceColorProfile.PaperPro : DeviceColorProfile.Rm2;

    /// <summary>The profile for the Drive archive and the in-app view: full screen RGB.</summary>
    public static DeviceColorProfile ForDrive() => DeviceColorProfile.Screen;
}
