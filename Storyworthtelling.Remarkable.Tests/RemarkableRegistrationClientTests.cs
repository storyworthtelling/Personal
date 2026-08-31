using System.Net;
using System.Text.Json;
using Storyworthtelling.Remarkable.Tests.Support;

namespace Storyworthtelling.Remarkable.Tests;

public class RemarkableRegistrationClientTests
{
    private const string DeviceUrl = "https://webapp-prod.cloud.remarkable.engineering/token/json/2/device/new";

    [Fact]
    public async Task Register_PostsTheCode_AndReturnsTheDeviceToken()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("\"the-device-token\"")   // the API wraps the token in quotes
        });
        var client = new RemarkableRegistrationClient(new HttpClient(handler));

        var token = await client.Register("  abcdefgh  ");

        Assert.Equal("the-device-token", token);   // quotes + surrounding whitespace trimmed
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(DeviceUrl, request.RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("abcdefgh", body.RootElement.GetProperty("code").GetString());   // the code is trimmed
        Assert.Equal("mobile-android", body.RootElement.GetProperty("deviceDesc").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("deviceID").GetString()));
    }

    [Fact]
    public async Task Register_Throws_WhenTheCodeIsRejected()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest));
        var client = new RemarkableRegistrationClient(new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.Register("bad-code"));
    }
}
