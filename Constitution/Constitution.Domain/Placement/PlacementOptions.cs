namespace Constitution.Domain.Placement;

/// <summary>What placement is told that is not on a calendar.</summary>
public sealed record PlacementOptions
{
    /// <summary>The shower buffer when nobody has said otherwise: fifteen minutes. The agent's number, not the owner's.</summary>
    public static readonly TimeSpan DefaultShowerBuffer = TimeSpan.FromMinutes(15);

    private readonly TimeSpan showerBuffer = DefaultShowerBuffer;

    /// <summary>
    /// Room kept free after a sweaty session so there is time to shower before the next thing. It is not an
    /// event and does not show on the calendar; it is time nothing else may be placed on, and it has to lie
    /// inside the session's gap like the session itself. Zero means no buffer.
    /// </summary>
    public TimeSpan ShowerBuffer
    {
        get => showerBuffer;
        init
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A shower buffer cannot be negative.");
            }

            showerBuffer = value;
        }
    }
}
