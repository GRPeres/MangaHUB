using MangaHub.Infrastructure;
using MangaHub.Core.Services;
using MangaHub.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMangaHubInfrastructure(builder.Configuration);
builder.Services.AddScoped<MangaUpdatesCatalogMatchService>();
builder.Services.AddHostedService<LibraryScanWorker>();
builder.Services.AddHostedService<RemoteSyncWorker>();

var host = builder.Build();
host.Run();
