namespace NytRemarkable.Infrastructure.Remarkable;

public interface IRemarkableAuthProvider
{
    Task<string> GetUserToken();
}
