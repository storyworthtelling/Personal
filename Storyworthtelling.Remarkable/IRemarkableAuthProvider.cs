namespace Storyworthtelling.Remarkable;

public interface IRemarkableAuthProvider
{
    Task<string> GetUserToken();
}
