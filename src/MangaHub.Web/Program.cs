using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MangaHub.Web;
using MangaHub.Web.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var configuredApiUrl = builder.Configuration["ApiUrl"];
var apiUrl = string.IsNullOrWhiteSpace(configuredApiUrl)
    || string.Equals(configuredApiUrl, "same-origin", StringComparison.OrdinalIgnoreCase)
        ? builder.HostEnvironment.BaseAddress
        : configuredApiUrl;

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiUrl) });
builder.Services.AddScoped<MangaHubApiClient>();
builder.Services.AddScoped<AuthState>();
builder.Services.AddMudServices();

await builder.Build().RunAsync();
