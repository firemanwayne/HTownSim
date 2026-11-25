namespace IncidentService.Api;

/// <summary>
/// Dependency-injection registration for the incident service.
/// </summary>
internal static class ServiceRegistryExtensions
{
    /// <summary>
    /// Registers the options, the ledger, its file store, the housekeeping worker and the controllers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddIncidentLedgerServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ServiceOptions>(configuration.GetSection(ServiceOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IIncidentStore, JsonFileIncidentStore>();

        services.TryAddSingleton<IIncidentLedger, IncidentLedger>();

        services.AddHostedService<LedgerWorker>();

        services
            .AddControllers()
            .AddApplicationPart(typeof(ServiceRegistryExtensions).Assembly)
            .ConfigureApplicationPartManager(manager =>
            {
                manager.FeatureProviders.Clear();

                manager.FeatureProviders.Add(new InternalControllerFeatureProvider());
            })
            .AddJsonOptions(json => json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        return services;
    }
}
