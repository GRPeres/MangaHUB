using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using MangaHub.Infrastructure.Data;
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
        services.AddScoped<ILibraryScanner, LocalLibraryScanner>();
        services.AddSingleton<IArchiveReader, CbzArchiveReader>();
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddSingleton<ISessionTokenService, JwtSessionTokenService>();
        services.AddHttpClient<IOpenLibraryClient, OpenLibraryClient>(client => client.BaseAddress = new Uri("https://openlibrary.org"));
        services.AddHttpClient<MangaDexSource>(client => client.BaseAddress = new Uri("https://api.mangadex.org"));
        services.AddScoped<IMangaSource, LocalMangaSource>();
        services.AddScoped<IMangaSource, MangaDexSource>();
        services.AddScoped<MangaSourceRegistry>();

        return services;
    }
}
