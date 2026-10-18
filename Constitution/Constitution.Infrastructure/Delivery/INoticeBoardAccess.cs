using Constitution.Domain.Notices;

namespace Constitution.Infrastructure.Delivery;

/// <summary>Runs work on the process's one notice board, one caller at a time (the head's board implements the same shape).</summary>
public interface INoticeBoardAccess
{
    /// <summary>Runs <paramref name="use"/> on the board.</summary>
    T With<T>(Func<NoticeBoard, T> use);
}
