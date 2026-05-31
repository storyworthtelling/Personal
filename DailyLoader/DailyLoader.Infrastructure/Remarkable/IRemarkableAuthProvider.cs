namespace DailyLoader.Infrastructure.Remarkable;

public interface IRemarkableAuthProvider
{
    Task<string> GetUserToken();
}
