using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HoustonIncidents.Harvester.Options;
using Microsoft.Extensions.Options;

namespace HoustonIncidents.Harvester.UnitTests.Services;

/// <summary>
/// Tests for <see cref="ServiceFeedWriter"/>.
/// </summary>
public sealed class ServiceFeedWriterTests
{
    [Fact]
    public async Task WriteAsync_ServiceAccepts_PostsFeedJsonToFeedPath()
    {
        var handler = new StubHandler(HttpStatusCode.OK);

        var writer = Make(handler);

        var feed = new LiveFeed { Incidents = [new LiveIncident { IncidentId = "COH-1", Units = ["A008"] }], Closed = ["COH-0"] };

        var result = await writer.WriteAsync(feed, CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal("http://localhost:5870/api/feed", handler.Requests.Single().RequestUri!.ToString());

        Assert.Contains("\"incidentId\":\"COH-1\"", handler.Bodies.Single());

        Assert.Contains("\"closed\":[\"COH-0\"]", handler.Bodies.Single());
    }

    [Fact]
    public async Task WriteAsync_ServiceFailsOnceThenAccepts_Retries()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var writer = Make(handler);

        var result = await writer.WriteAsync(new LiveFeed(), CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task WriteAsync_ServiceKeepsFailing_ReturnsFailureAfterAllTries()
    {
        var handler = new StubHandler(HttpStatusCode.BadGateway);

        var writer = Make(handler);

        var result = await writer.WriteAsync(new LiveFeed(), CancellationToken.None);

        Assert.False(result.IsSuccess);

        Assert.Equal(3, handler.Requests.Count);

        Assert.Contains("502", result.Error);
    }

    private static ServiceFeedWriter Make(StubHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5870/") };

        var options = Microsoft.Extensions.Options.Options.Create(new IncidentOptions { ServiceRetries = 3, ServiceRetryDelaySeconds = 0 });

        return new ServiceFeedWriter(client, options);
    }

    private sealed class StubHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int _next;

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            var status = statuses[Math.Min(_next++, statuses.Length - 1)];

            return new HttpResponseMessage(status);
        }
    }
}
