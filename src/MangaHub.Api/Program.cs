using MangaHub.Api.Data;
using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddMangaHubInfrastructure(builder.Configuration);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(builder.Configuration["FrontendOrigin"] ?? "http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 8;
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<CatalogRepository>();
builder.Services.AddScoped<ShelfRepository>();
builder.Services.AddScoped<SeriesRepository>();
builder.Services.AddScoped<ProgressRepository>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<SessionCookieService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<OpenLibraryService>();
builder.Services.AddScoped<MangaDexCatalogMatchService>();
builder.Services.AddScoped<MangaUpdatesCatalogMatchService>();
builder.Services.AddScoped<MetadataService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<ShelfService>();
builder.Services.AddScoped<LibraryService>();
builder.Services.AddScoped<SeriesService>();
builder.Services.AddScoped<ReaderService>();
builder.Services.AddSingleton<ReaderPreparationService>();
builder.Services.AddScoped<ProgressService>();
builder.Services.AddScoped<CatalogCacheService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseRateLimiter();
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapControllers();

app.Run();
