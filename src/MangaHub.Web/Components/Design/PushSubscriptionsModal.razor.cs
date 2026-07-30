using MangaHub.Web.API.DTOs;
using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Design;

public partial class PushSubscriptionsModal
{
    [Parameter] public bool Open { get; set; }
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }
    [Parameter] public IReadOnlyList<WebPushSubscriptionResponse> Subscriptions { get; set; } = [];
    [Parameter] public EventCallback OnRefresh { get; set; }
    [Parameter] public EventCallback<WebPushSubscriptionResponse> OnUnsubscribe { get; set; }

    private Task Close() => OpenChanged.InvokeAsync(false);
}
