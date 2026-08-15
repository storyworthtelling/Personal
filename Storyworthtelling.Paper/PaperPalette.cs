using SkiaSharp;

namespace Storyworthtelling.Paper;

/// <summary>
/// Resolves a semantic <see cref="PaperRole"/> to a concrete <see cref="SKColor"/> for a target
/// <see cref="DeviceColorProfile"/> — the device-adaptive colour model approved in the P3 mockup.
/// The colour profiles (Paper Pro, Screen) emit the calm hues; the grayscale profile (rM2) emits the
/// designed gray ramp so meaning survives desaturation. Grounds vary per device; ink and hairlines are
/// shared. Colour desaturation on the Paper Pro itself is a property of the Kaleido panel, not of the
/// bytes we emit, so the two colour profiles share the same hues and differ only in ground.
/// </summary>
public static class PaperPalette
{
    public static SKColor Resolve(PaperRole role, DeviceColorProfile profile)
    {
        var grayscale = profile == DeviceColorProfile.Rm2;
        return role switch
        {
            PaperRole.Ground => Parse(profile switch
            {
                DeviceColorProfile.PaperPro => PaperColorTokens.PaperProGround,
                DeviceColorProfile.Rm2 => PaperColorTokens.Rm2Ground,
                _ => PaperColorTokens.ScreenGround,
            }),
            PaperRole.Ink => Parse(PaperColorTokens.Ink),
            PaperRole.InkSoft => Parse(PaperColorTokens.InkSoft),
            PaperRole.Hairline => Parse(PaperColorTokens.Hairline),
            PaperRole.Active => Parse(grayscale ? PaperColorTokens.GrayAccent : PaperColorTokens.Accent),
            PaperRole.Prior => Parse(grayscale ? PaperColorTokens.GraySlate : PaperColorTokens.Slate),
            PaperRole.New => Parse(grayscale ? PaperColorTokens.GraySage : PaperColorTokens.Sage),
            PaperRole.Problem => Parse(grayscale ? PaperColorTokens.GrayPlum : PaperColorTokens.Plum),
            PaperRole.Caution => Parse(grayscale ? PaperColorTokens.GrayOchre : PaperColorTokens.Ochre),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unhandled paper colour role."),
        };
    }

    private static SKColor Parse(string hex) => SKColor.Parse(hex);
}
