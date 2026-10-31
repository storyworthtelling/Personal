using Constitution.Domain.Notices;
using Constitution.Domain.Voice;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Delivery;
using Constitution.Settings;

namespace Constitution.WorkoutDelivery;

/// <summary>The delivery service's door onto the process-wide notice board.</summary>
public sealed class ConnectionNoticeBoardAccess : INoticeBoardAccess
{
    public T With<T>(Func<NoticeBoard, T> use) => ConnectionNoticeBoard.With(use);
}

/// <summary>The encouragement seat over any composer of seat lines; the head hands it the voice it already has.</summary>
public sealed class SeatLineEncouragement(Func<SeatRequest, CancellationToken, Task<SeatLine>> compose) : IWorkoutEncouragement
{
    public async Task<EncouragementLine?> LineAsync(ProgressionFacts news, DateTimeOffset now, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(news);
        if (SeatFacts.WorkoutEncouragement(news) is not { } request)
        {
            return null;
        }

        var line = await compose(request, cancellation);
        return new EncouragementLine(line.Text, line.IsVoiced);
    }
}
