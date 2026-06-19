using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Storyboarder.Api;
using Storyboarder.Domain.External;
using Storyboarder.Infrastructure.Persistence;
using Xunit;

namespace Storyboarder.Tests.Api;

public class ProjectsApiTests : IClassFixture<ProjectsApiTests.ApiFactory>
{
    private readonly ApiFactory factory;

    public ProjectsApiTests(ApiFactory factory) => this.factory = factory;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Create_FromPickedDoc_ThenGet_ReturnsStatus()
    {
        factory.Docs
            .Setup(d => d.FetchText("doc-123", "token-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Chapter one. The hero set out.");
        var client = factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/projects", new
        {
            fileId = "doc-123",
            fileName = "My Novel",
            accessToken = "token-abc",
            artStyle = (string?)null
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement;
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("My Novel", created.GetProperty("title").GetString());

        var status = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{id}", Json);
        Assert.Equal("My Novel", status.GetProperty("title").GetString());
        Assert.False(string.IsNullOrEmpty(status.GetProperty("stage").GetString()));
    }

    [Fact]
    public async Task Create_MissingFields_ReturnsBadRequest()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/projects", new
        {
            fileId = "",
            fileName = "x",
            accessToken = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownProject_Returns404()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/projects/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task VolumePdf_NotReady_Returns404()
    {
        factory.Docs
            .Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/projects", new
        {
            fileId = "f", fileName = "n", accessToken = "t"
        });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var response = await client.GetAsync($"/api/projects/{id}/volumes/1/pdf");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Hosts the real app with in-memory SQLite, a faked Google Docs client, and the background worker
    // removed (so tests don't make real Claude/Gemini calls).
    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public Mock<IGoogleDocsClient> Docs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            connection.Open();
            builder.ConfigureServices(services =>
            {
                RemoveAll(services, typeof(IDbContextFactory<StoryboarderDbContext>));
                RemoveAll(services, typeof(DbContextOptions<StoryboarderDbContext>));
                RemoveAll(services, typeof(DbContextOptions));
                services.AddDbContextFactory<StoryboarderDbContext>(options => options.UseSqlite(connection));

                RemoveAll(services, typeof(IGoogleDocsClient));
                services.AddSingleton(Docs.Object);

                RemoveByImplementation(services, typeof(GraphicNovelWorker));
            });
        }

        private static void RemoveAll(IServiceCollection services, Type serviceType)
        {
            for (int i = services.Count - 1; i >= 0; i--)
                if (services[i].ServiceType == serviceType)
                    services.RemoveAt(i);
        }

        private static void RemoveByImplementation(IServiceCollection services, Type implementationType)
        {
            for (int i = services.Count - 1; i >= 0; i--)
                if (services[i].ImplementationType == implementationType)
                    services.RemoveAt(i);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                connection.Dispose();
        }
    }
}
