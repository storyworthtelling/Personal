namespace DailyLoader.Domain.Interfaces;

public interface INytCrosswordClient
{
    Task<byte[]> DownloadDailyPuzzle(DateOnly date);
    Task<byte[]> DownloadBonusPuzzle(DateOnly date);
}
