using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using MangaHub.Infrastructure.Data;
using MangaHub.Infrastructure.Caching;
using MangaHub.Infrastructure.Local;
using MangaHub.Infrastructure.RemoteJobs;
using MangaHub.Infrastructure.Security;
using MangaHub.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MangaHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMangaHubInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MangaHubOptions>(configuration.GetSection("MangaHub"));
        services.AddSingleton<RemoteJobPriorityContext>();
        services.AddSingleton<RemoteRequestScheduler>();
        services.AddSingleton<IRemoteRequestScheduler>(provider => provider.GetRequiredService<RemoteRequestScheduler>());
        services.AddHostedService(provider => provider.GetRequiredService<RemoteRequestScheduler>());

        var connectionString = configuration.GetConnectionString("MangaHub")
            ?? configuration["DATABASE_URL"]
            ?? "Host=localhost;Database=mangahub;Username=mangahub;Password=mangahub";

        services.AddDbContext<MangaHubDbContext>(options => options.UseNpgsql(connectionString));
        services.AddMemoryCache();
        services.AddScoped<ILibraryScanner, LocalLibraryScanner>();
        services.AddSingleton<IArchiveReader, CbzArchiveReader>();
        services.AddSingleton<IMangaDexChapterCache, MangaDexChapterCache>();
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddSingleton<ISessionTokenService, JwtSessionTokenService>();
        services.AddHttpClient<IOpenLibraryClient, OpenLibraryClient>(client =>
            {
                client.BaseAddress = new Uri("https://openlibrary.org");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MangaHub/0.1 self-hosted catalog");
            })
            .AddHttpMessageHandler(provider => new RemoteRequestSchedulingHandler(
                RemoteProvider.OpenLibrary,
                provider.GetRequiredService<IRemoteRequestScheduler>(),
                provider.GetRequiredService<RemoteJobPriorityContext>()));
        services.AddHttpClient<IMyAnimeListClient, MyAnimeListClient>(client => client.BaseAddress = new Uri("https://api.myanimelist.net/v2/"))
            .AddHttpMessageHandler(provider => new RemoteRequestSchedulingHandler(
                RemoteProvider.MyAnimeList,
                provider.GetRequiredService<IRemoteRequestScheduler>(),
                provider.GetRequiredService<RemoteJobPriorityContext>()));
        services.AddHttpClient<IMangaUpdatesClient, MangaUpdatesClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.mangaupdates.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MangaHub/0.1 self-hosted catalog sync");
        }).AddHttpMessageHandler(provider => new RemoteRequestSchedulingHandler(
            RemoteProvider.MangaUpdates,
            provider.GetRequiredService<IRemoteRequestScheduler>(),
            provider.GetRequiredService<RemoteJobPriorityContext>()));
        services.AddHttpClient<MangaDexSource>(client =>
        {
            client.BaseAddress = new Uri("https://api.mangadex.org");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MangaHub/0.1 self-hosted reader");
        }).AddHttpMessageHandler(provider => new RemoteRequestSchedulingHandler(
            RemoteProvider.MangaDexApi,
            provider.GetRequiredService<IRemoteRequestScheduler>(),
            provider.GetRequiredService<RemoteJobPriorityContext>()));
        services.AddScoped<IMangaDexCatalogLookup>(serviceProvider => serviceProvider.GetRequiredService<MangaDexSource>());
        services.AddHttpClient("mangadex-sync", client =>
        {
            client.BaseAddress = new Uri("https://api.mangadex.org");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MangaHub/0.1 self-hosted catalog sync");
        }).AddHttpMessageHandler(provider => new RemoteRequestSchedulingHandler(
            RemoteProvider.MangaDexApi,
            provider.GetRequiredService<IRemoteRequestScheduler>(),
            provider.GetRequiredService<RemoteJobPriorityContext>()));
        services.AddHttpClient("mangadex-pages", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MangaHub/0.1 self-hosted reader");
        }).AddHttpMessageHandler(provider => new RemoteRequestSchedulingHandler(
            RemoteProvider.MangaDexPages,
            provider.GetRequiredService<IRemoteRequestScheduler>(),
            provider.GetRequiredService<RemoteJobPriorityContext>()));
        services.AddScoped<IMangaSource, LocalMangaSource>();
        services.AddScoped<IMangaSource>(serviceProvider => serviceProvider.GetRequiredService<MangaDexSource>());
        services.AddScoped<MangaSourceRegistry>();

        return services;
    }
}
