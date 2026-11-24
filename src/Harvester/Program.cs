var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddIncidentServices(builder.Configuration);

using var host = builder.Build();

await host.RunAsync().ConfigureAwait(false);
