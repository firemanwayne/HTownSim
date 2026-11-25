namespace HoustonIncidents.Harvester.Services;

/// <summary>
/// <see cref="HttpClient"/>-based implementation of <see cref="IIncidentPageClient"/>.
/// </summary>
/// <param name="httpClient">The configured HTTP client (timeout and User-Agent set at registration).</param>
/// <param name="options">The harvester options supplying the page URL.</param>
internal sealed class IncidentPageClient(HttpClient httpClient, IOptions<IncidentOptions> options) : IIncidentPageClient
{
    /// <inheritdoc />
    public Task<Result<string>> GetPageAsync(CancellationToken cancellationToken)
    {
        return GetPageAsync(options.Value.Url, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<string>> GetPageAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var html = await httpClient
                .GetStringAsync(url, cancellationToken)
                .ConfigureAwait(false);

            return Result<string>.Ok(html);
        }
        catch (HttpRequestException ex)
        {
            return Result<string>.Fail($"HTTP request failed: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<string>.Fail("HTTP request timed out.");
        }
    }
}
