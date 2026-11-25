using System.Net.Http.Json;

namespace HoustonIncidents.Harvester.Services;

/// <summary>
/// Hands each poll to the incident service (Tools/IncidentService), which owns the incident lifecycle, instead of dropping a file
/// for the game. A failed post is retried a few times, since the service may still be starting.
/// </summary>
/// <param name="client">The HTTP client, with its base address set from <see cref="IncidentOptions.ServiceUrl"/>.</param>
/// <param name="options">Supplies the service address and retry settings.</param>
internal sealed class ServiceFeedWriter(HttpClient client, IOptions<IncidentOptions> options) : IGameFeedWriter
{
    /// <summary>The service endpoint that takes a poll.</summary>
    private const string FeedPath = "api/feed";

    /// <inheritdoc />
    public async Task<Result<string>> WriteAsync(LiveFeed feed, CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, options.Value.ServiceRetries);

        var error = string.Empty;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var response = await client
                    .PostAsJsonAsync(FeedPath, feed, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return Result<string>.Ok(new Uri(client.BaseAddress!, FeedPath).ToString());
                }

                error = $"The service answered {(int)response.StatusCode}";
            }
            catch (HttpRequestException ex)
            {
                error = ex.Message;
            }

            if (attempt < attempts)
            {
                await Task
                    .Delay(TimeSpan.FromSeconds(options.Value.ServiceRetryDelaySeconds), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return Result<string>.Fail($"Could not reach the incident service at {client.BaseAddress}: {error}");
    }
}
