namespace HoustonIncidents.Harvester;

/// <summary>
/// Dependency-injection registration for the harvester.
/// </summary>
internal static class ServiceRegistryExtensions
{
    /// <summary>
    /// Registers the harvester options, HTTP clients (page and geocoder), services, and hosted worker.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddIncidentServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IncidentOptions>(configuration.GetSection(IncidentOptions.SectionName));

        services.AddHttpClient<IIncidentPageClient, IncidentPageClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<IncidentOptions>>().Value;

            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);

            client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
        });

        services.AddHttpClient<IIncidentGeocoder, IncidentGeocoder>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<IncidentOptions>>().Value;

            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);

            client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
        });

        services.TryAddSingleton<IIncidentTableParser, IncidentTableParser>();

        services.TryAddSingleton<IFireIncidentTableParser, FireIncidentTableParser>();

        var serviceUrl = configuration[$"{IncidentOptions.SectionName}:{nameof(IncidentOptions.ServiceUrl)}"];

        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            services.TryAddSingleton<IGameFeedWriter, GameFeedWriter>();
        }
        else
        {
            services.AddHttpClient<IGameFeedWriter, ServiceFeedWriter>(client =>
            {
                client.BaseAddress = new Uri(serviceUrl.TrimEnd('/') + "/");
            });
        }

        services.TryAddSingleton<IIncidentTracker, IncidentTracker>();

        services.AddHostedService<IncidentWorker>();

        return services;
    }
}
