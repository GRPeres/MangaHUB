using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using MangaHub.Infrastructure.Data;
using MangaHub.Infrastructure.Caching;
using MangaHub.Infrastructure.Local;
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
        services.AddHttpClient<IOpenLibraryClient, OpenLibraryClient>(client => client.BaseAddress = new Uri("https://openlibrary.org"));
        services.AddHttpClient<IMyAnimeListClient, MyAnimeListClient>(client => client.BaseAddress = new Uri("https://api.myanimelist.net/v2/"));
        services.AddHttpClient<MangaDexSource>(client =>
        {
            client.BaseAddress = new Uri("https://api.mangadex.org");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MangaHub/0.1 self-hosted reader");
        });
        services.AddHttpClient("mangadex-sync", client =>
        {
            client.BaseAddress = new Uri("https://api.mangadex.org");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MangaHub/0.1 self-hosted catalog sync");
        });
        services.AddHttpClient("mangadex-pages", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MangaHub/0.1 self-hosted reader");
        });
        services.AddScoped<IMangaSource, LocalMangaSource>();
        services.AddScoped<IMangaSource>(serviceProvider => serviceProvider.GetRequiredService<MangaDexSource>());
        services.AddScoped<MangaSourceRegistry>();

        return services;
    }
}
