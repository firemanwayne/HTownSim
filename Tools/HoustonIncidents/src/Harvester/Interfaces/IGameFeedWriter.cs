namespace HoustonIncidents.Harvester.Interfaces;

/// <summary>
/// Hands incidents to the game through its live-feed inbox folder.
/// </summary>
internal interface IGameFeedWriter
{
    /// <summary>
    /// Writes one feed file (written under a temporary name, then renamed, so the game never reads half a file).
    /// </summary>
    /// <param name="feed">The incidents and closures to send.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The path written, or a failure.</returns>
    Task<Result<string>> WriteAsync(LiveFeed feed, CancellationToken cancellationToken);
}
