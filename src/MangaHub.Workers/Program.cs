using MangaHub.Infrastructure;
using MangaHub.Workers;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMangaHubWorkerInfrastructure(builder.Configuration);
builder.Services.AddHttpClient<InternalMaintenanceApiClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<MangaHubOptions>>().Value;
    client.BaseAddress = new Uri(options.InternalApiUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddHostedService<RemoteMaintenanceScheduleWorker>();
builder.Services.AddHostedService<MaintenanceWatchdogWorker>();
builder.Services.AddHostedService<UsageAnalyticsWorker>();
builder.Services.AddHostedService<NotificationCleanupWorker>();
builder.Services.AddHostedService<MaintenanceJobWorker>();

var host = builder.Build();
host.Run();
