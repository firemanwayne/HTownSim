var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIncidentLedgerServices(builder.Configuration);

var app = builder.Build();

app.MapControllers();

await app
    .RunAsync()
    .ConfigureAwait(false);
