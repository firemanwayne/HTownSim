namespace IncidentService.Api.Services;

/// <summary>
/// Keeps the ledger in one JSON file, written under a temporary name and then renamed so a crash cannot leave half a ledger.
/// </summary>
/// <param name="options">Supplies the file path.</param>
/// <param name="logger">Logs read and write problems.</param>
internal sealed partial class JsonFileIncidentStore(IOptions<ServiceOptions> options, ILogger<JsonFileIncidentStore> logger) : IIncidentStore
{
    /// <summary>JSON settings: indented for people reading the file.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <inheritdoc />
    public async Task<LedgerFile> LoadAsync(CancellationToken cancellationToken)
    {
        var path = Path();

        try
        {
            if (!File.Exists(path))
            {
                return new LedgerFile();
            }

            await using var stream = File.OpenRead(path);

            var file = await JsonSerializer
                .DeserializeAsync<LedgerFile>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return file ?? new LedgerFile();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LogLoadFailedWarning(logger, path, ex.Message);

            return new LedgerFile();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(LedgerFile file, CancellationToken cancellationToken)
    {
        var path = Path();

        var temporary = path + ".tmp";

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer
                    .SerializeAsync(stream, file, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailedWarning(logger, path, ex.Message);
        }
    }

    /// <summary>The ledger file path with environment variables expanded.</summary>
    /// <returns>The path.</returns>
    private string Path() => Environment.ExpandEnvironmentVariables(options.Value.LedgerPath);

    /// <summary>Logs a ledger that could not be read.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The file.</param>
    /// <param name="error">Why.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read ledger {Path}, starting empty: {Error}")]
    private static partial void LogLoadFailedWarning(ILogger logger, string path, string error);

    /// <summary>Logs a ledger that could not be written.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The file.</param>
    /// <param name="error">Why.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save ledger {Path}: {Error}")]
    private static partial void LogSaveFailedWarning(ILogger logger, string path, string error);
}
