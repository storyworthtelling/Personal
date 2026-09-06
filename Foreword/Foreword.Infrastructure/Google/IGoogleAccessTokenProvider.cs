namespace Foreword.Infrastructure.Google;

// Supplies a valid Google access token, refreshing it from the stored refresh token as needed.
public interface IGoogleAccessTokenProvider
{
    Task<string> GetAccessToken();
}
