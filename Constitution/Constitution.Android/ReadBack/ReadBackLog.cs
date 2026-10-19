using Android.Content;
using Constitution.Body;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.ReadBack;

/// <summary>
/// The read-back's lines go into the Body trail (<c>body.log</c>), which the Diagnostics export already carries through its
/// value-free filter: one more phrase there, not a trail of its own. The phrases are written by
/// <c>ReadBackTrail</c> and hold counts and words only.
/// </summary>
public sealed class ReadBackLog(Context context) : IReadBackLog
{
    private readonly BodyLog trail = new(context);

    public void Outcome(string phrase) => trail.Outcome(phrase);
}
