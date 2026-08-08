using MangaHub.Api.Data;
using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddMangaHubInfrastructure(builder.Configuration);
var mangaHubOptions = builder.Configuration.GetSection("MangaHub").Get<MangaHubOptions>() ?? new MangaHubOptions();
var authentication = builder.Services.AddAuthentication();
authentication.AddCookie("External", options =>
{
    options.Cookie.Name = "mangahub_external";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
});
if (mangaHubOptions.GoogleAuth.IsConfigured)
{
    authentication.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = mangaHubOptions.GoogleAuth.ClientId;
        options.ClientSecret = mangaHubOptions.GoogleAuth.ClientSecret;
        options.SignInScheme = "External";
        options.CallbackPath = "/auth/google/callback";
        options.Scope.Add("email");
        options.SaveTokens = false;
    });
}
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
builder.Services.AddScoped<NotificationRepository>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<SessionCookieService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
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
builder.Services.AddScoped<NotificationService>();

var app = builder.Build();
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
// The API is internal-only in Compose; nginx is the trusted reverse proxy.
forwardedHeaders.KnownNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

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
app.UseAuthentication();
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapControllers();

app.Run();
