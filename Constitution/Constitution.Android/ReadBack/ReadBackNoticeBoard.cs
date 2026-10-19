using Constitution.Domain.Notices;
using Constitution.Infrastructure.Delivery;
using Constitution.Settings;

namespace Constitution.ReadBack;

/// <summary>The process's one notice board (what the dog-ear and the Waiting page read), in the shape the services take.</summary>
public sealed class ReadBackNoticeBoard : INoticeBoardAccess
{
    public T With<T>(Func<NoticeBoard, T> use) => ConnectionNoticeBoard.With(use);
}
