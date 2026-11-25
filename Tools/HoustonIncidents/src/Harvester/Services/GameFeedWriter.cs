using System.Globalization;
using System.IO;
using System.Text.Json;

namespace HoustonIncidents.Harvester.Services;

/// <summary>
/// Writes feed files into the game's live-incident inbox (<see cref="IncidentOptions.GameInboxDirectory"/>), which HTownSim
/// watches. Each file is written under a <c>.tmp</c> name and then renamed to <c>live-*.json</c>.
/// </summary>
/// <param name="options">Supplies the inbox folder.</param>
internal sealed class GameFeedWriter(IOptions<IncidentOptions> options) : IGameFeedWriter
{
    /// <summary>JSON settings: indented for people reading the folder.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <inheritdoc />
    public async Task<Result<string>> WriteAsync(LiveFeed feed, CancellationToken cancellationToken)
    {
        var folder = Environment.ExpandEnvironmentVariables(options.Value.GameInboxDirectory);

        var name = "live-" + feed.Generated.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);

        var temporary = Path.Combine(folder, name + ".tmp");

        var final = Path.Combine(folder, name + ".json");

        try
        {
            Directory.CreateDirectory(folder);

            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer
                    .SerializeAsync(stream, feed, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporary, final, true);

            return Result<string>.Ok(final);
        }
        catch (IOException ex)
        {
            return Result<string>.Fail($"Could not write {final}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<string>.Fail($"Could not write {final}: {ex.Message}");
        }
    }
}
