using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MangaHub.Web;
using MudBlazor.Services;
using MangaHub.Web.API;
using MangaHub.Web.Services;
using MangaHub.Web.API.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var configuredApiUrl = builder.Configuration["ApiUrl"];
var apiUrl = string.IsNullOrWhiteSpace(configuredApiUrl)
    || string.Equals(configuredApiUrl, "same-origin", StringComparison.OrdinalIgnoreCase)
        ? builder.HostEnvironment.BaseAddress
        : configuredApiUrl;

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiUrl) });
builder.Services.AddScoped<SessionTokenStore>();
builder.Services.AddScoped<ApiHttpClient>();
builder.Services.AddScoped<AuthApiService>();
builder.Services.AddScoped<AuthSessionService>();
builder.Services.AddScoped<AdminApiService>();
builder.Services.AddScoped<CatalogApiService>();
builder.Services.AddScoped<OpenLibraryApiService>();
builder.Services.AddScoped<MetadataApiService>();
builder.Services.AddScoped<MangaApiService>();
builder.Services.AddScoped<ShelfApiService>();
builder.Services.AddScoped<LibraryApiService>();
builder.Services.AddScoped<SeriesApiService>();
builder.Services.AddScoped<ReadApiService>();
builder.Services.AddScoped<ProgressApiService>();
builder.Services.AddMudServices();

await builder.Build().RunAsync();
