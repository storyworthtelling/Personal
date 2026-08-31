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
            PaperRole.InkMuted => Parse(PaperColorTokens.InkMuted),
            PaperRole.Hairline => Parse(PaperColorTokens.Hairline),
            PaperRole.Active => Parse(grayscale ? PaperColorTokens.GrayAccent : PaperColorTokens.Accent),
            PaperRole.Prior => Parse(grayscale ? PaperColorTokens.GraySlate : PaperColorTokens.Slate),
            PaperRole.New => Parse(grayscale ? PaperColorTokens.GraySage : PaperColorTokens.Sage),
            PaperRole.Problem => Parse(grayscale ? PaperColorTokens.GrayPlum : PaperColorTokens.Plum),
            PaperRole.Caution => Parse(grayscale ? PaperColorTokens.GrayOchre : PaperColorTokens.Ochre),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unhandled paper colour role."),
        };
    }

    /// <summary>
    /// Resolves a P4 <see cref="ColorRole"/> at a <see cref="ColorTier"/> for a target
    /// <see cref="UiColorProfile"/> (DESIGN.md §3/§16). The App profile emits the lively five-tier palette;
    /// the document profiles emit the muted per-role hue (colour) or the separable gray ramp (rM2), where
    /// the tiers collapse to that one muted value (with the wash carrying its alpha). This is the only way
    /// above the tokens to get a role colour; nothing else deals in hex.
    /// </summary>
    public static SKColor ResolveRole(ColorRole role, ColorTier tier, UiColorProfile profile) => profile switch
    {
        UiColorProfile.App => AppTier(role, tier),
        UiColorProfile.DocumentColor => Muted(DocHex(role), tier),
        UiColorProfile.DocumentGray => Muted(GrayHex(role), tier),
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unhandled colour profile."),
    };

    private static SKColor AppTier(ColorRole role, ColorTier tier)
    {
        var (mark, text, deep, fill) = AppTiers(role);
        return tier switch
        {
            ColorTier.Wash => WithWashAlpha(mark),
            ColorTier.Fill => Parse(fill),
            ColorTier.Mark => Parse(mark),
            ColorTier.Text => Parse(text),
            ColorTier.Deep => Parse(deep),
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unhandled colour tier."),
        };
    }

    // Documents are muted: the tiers collapse to the one muted hue, with the wash carrying its alpha.
    private static SKColor Muted(string hex, ColorTier tier) =>
        tier == ColorTier.Wash ? WithWashAlpha(hex) : Parse(hex);

    private static (string mark, string text, string deep, string fill) AppTiers(ColorRole role) => role switch
    {
        ColorRole.Action => (PaperColorTokens.ActionMark, PaperColorTokens.ActionText, PaperColorTokens.ActionDeep, PaperColorTokens.ActionFill),
        ColorRole.Positive => (PaperColorTokens.PositiveMark, PaperColorTokens.PositiveText, PaperColorTokens.PositiveDeep, PaperColorTokens.PositiveFill),
        ColorRole.Resolved => (PaperColorTokens.ResolvedMark, PaperColorTokens.ResolvedText, PaperColorTokens.ResolvedDeep, PaperColorTokens.ResolvedFill),
        ColorRole.Reference => (PaperColorTokens.ReferenceMark, PaperColorTokens.ReferenceText, PaperColorTokens.ReferenceDeep, PaperColorTokens.ReferenceFill),
        ColorRole.Caution => (PaperColorTokens.CautionMark, PaperColorTokens.CautionText, PaperColorTokens.CautionDeep, PaperColorTokens.CautionFill),
        ColorRole.Problem => (PaperColorTokens.ProblemMark, PaperColorTokens.ProblemText, PaperColorTokens.ProblemDeep, PaperColorTokens.ProblemFill),
        ColorRole.InProgress => (PaperColorTokens.ProgressMark, PaperColorTokens.ProgressText, PaperColorTokens.ProgressDeep, PaperColorTokens.ProgressFill),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unhandled colour role."),
    };

    private static string DocHex(ColorRole role) => role switch
    {
        ColorRole.Action => PaperColorTokens.ActionDoc,
        ColorRole.Positive => PaperColorTokens.PositiveDoc,
        ColorRole.Resolved => PaperColorTokens.ResolvedDoc,
        ColorRole.Reference => PaperColorTokens.ReferenceDoc,
        ColorRole.Caution => PaperColorTokens.CautionDoc,
        ColorRole.Problem => PaperColorTokens.ProblemDoc,
        ColorRole.InProgress => PaperColorTokens.ProgressDoc,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unhandled colour role."),
    };

    private static string GrayHex(ColorRole role) => role switch
    {
        ColorRole.Action => PaperColorTokens.ActionGray,
        ColorRole.Positive => PaperColorTokens.PositiveGray,
        ColorRole.Resolved => PaperColorTokens.ResolvedGray,
        ColorRole.Reference => PaperColorTokens.ReferenceGray,
        ColorRole.Caution => PaperColorTokens.CautionGray,
        ColorRole.Problem => PaperColorTokens.ProblemGray,
        ColorRole.InProgress => PaperColorTokens.ProgressGray,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unhandled colour role."),
    };

    private static SKColor WithWashAlpha(string hex) => Parse(hex).WithAlpha((byte)PaperColorTokens.WashAlpha);

    private static SKColor Parse(string hex) => SKColor.Parse(hex);
}
