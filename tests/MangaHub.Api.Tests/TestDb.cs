using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Tests;

internal static class TestDb
{
    public static MangaHubDbContext Create()
    {
        var options = new DbContextOptionsBuilder<MangaHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableSensitiveDataLogging()
            .Options;

        return new MangaHubDbContext(options);
    }
}
