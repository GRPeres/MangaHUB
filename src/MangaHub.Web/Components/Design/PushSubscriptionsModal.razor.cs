using MangaHub.Web.API.DTOs;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Components.Design;

public partial class PushSubscriptionsModal
{
    [Parameter] public bool Open { get; set; }
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }
    [Parameter] public IReadOnlyList<WebPushSubscriptionResponse> Subscriptions { get; set; } = [];
    [Parameter] public EventCallback OnRefresh { get; set; }
    [Parameter] public EventCallback<WebPushSubscriptionResponse> OnUnsubscribe { get; set; }

    private Task Close() => OpenChanged.InvokeAsync(false);

    private static string IconFor(string label)
    {
        if (label.Contains("win", StringComparison.OrdinalIgnoreCase) || label.Contains("mac", StringComparison.OrdinalIgnoreCase) || label.Contains("linux", StringComparison.OrdinalIgnoreCase))
        {
            return Icons.Material.Filled.DesktopWindows;
        }

        return label.Contains("android", StringComparison.OrdinalIgnoreCase)
            ? Icons.Material.Filled.PhoneAndroid
            : Icons.Material.Filled.PhoneIphone;
    }
}
