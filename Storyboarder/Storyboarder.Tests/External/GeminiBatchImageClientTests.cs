using System.Net;
using Microsoft.Extensions.Options;
using Storyboarder.Domain.External;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.External;
using Storyboarder.Tests.Support;
using Xunit;

namespace Storyboarder.Tests.External;

// Parse-level coverage of the batch client against the ASSUMED Batch REST shapes (see the warnings in
// GeminiBatchImageClient — the envelope still needs live verification). These pin the internal correlation
// and image/error extraction so a real-shape adjustment is a localized change with a test to update.
public class GeminiBatchImageClientTests
{
    private static (GeminiBatchImageClient Client, FakeHttpMessageHandler Handler) Create()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options.Create(new GeminiOptions { ApiKey = "test-key", Model = "gemini-2.5-flash-image" });
        return (new GeminiBatchImageClient(new HttpClient(handler), options), handler);
    }

    [Fact]
    public async Task Submit_ReturnsTheJobName_AndSendsEachPanelKeyedByItsId()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"name\":\"batches/job-123\"}");

        var panelId = Guid.NewGuid();
        var jobId = await client.SubmitAsync(
            [new BatchPanelRequest(panelId, "a hero at a gate", "noir", [], 1.5)], CancellationToken.None);

        Assert.Equal("batches/job-123", jobId);
        var sent = Assert.Single(handler.RequestBodies);
        Assert.Contains(panelId.ToString(), sent);          // keyed by panel id
        Assert.Contains("a hero at a gate", sent);          // the prompt is in the request body
        Assert.Contains("batchGenerateContent", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Poll_ReportsRunning_WhileTheOperationIsNotDone()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"done\":false,\"metadata\":{\"state\":\"BATCH_STATE_RUNNING\"}}");

        var status = await client.PollAsync("batches/job-123", CancellationToken.None);

        Assert.Equal(BatchJobState.Running, status.State);
    }

    [Fact]
    public async Task Poll_MapsFinishedResults_ToImagesAndFailuresByPanelKey()
    {
        var (client, handler) = Create();
        var rendered = Guid.NewGuid();
        var blocked = Guid.NewGuid();
        var imageBase64 = Convert.ToBase64String([1, 2, 3, 4]);

        var operation =
            "{\"done\":true,\"response\":{\"inlinedResponses\":{\"inlinedResponses\":[" +
            "{\"metadata\":{\"key\":\"" + rendered + "\"},\"response\":{\"candidates\":[{\"content\":{\"parts\":[" +
            "{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"" + imageBase64 + "\"}}]}}]}}," +
            "{\"metadata\":{\"key\":\"" + blocked + "\"},\"error\":{\"message\":\"blocked on safety\"}}" +
            "]}}}";
        handler.EnqueueOk(operation);

        var status = await client.PollAsync("batches/job-123", CancellationToken.None);

        Assert.Equal(BatchJobState.Succeeded, status.State);
        Assert.Equal(2, status.Results.Count);

        var ok = status.Results.Single(r => r.PanelId == rendered);
        Assert.True(ok.Succeeded);
        Assert.Equal([1, 2, 3, 4], ok.Image);

        var fail = status.Results.Single(r => r.PanelId == blocked);
        Assert.False(fail.Succeeded);
        Assert.Contains("blocked on safety", fail.Error);
    }

    [Fact]
    public async Task Poll_ReportsFailed_OnAnOperationLevelError()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"done\":true,\"error\":{\"code\":13,\"message\":\"internal\"}}");

        var status = await client.PollAsync("batches/job-123", CancellationToken.None);

        Assert.Equal(BatchJobState.Failed, status.State);
    }

    [Fact]
    public async Task Submit_Throws_OnAnHttpError()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"bad\"}}");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.SubmitAsync([new BatchPanelRequest(Guid.NewGuid(), "x", "y", [], null)], CancellationToken.None));
    }
}
