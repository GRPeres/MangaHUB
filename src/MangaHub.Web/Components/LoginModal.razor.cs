using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using MangaHub.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Components;

public partial class LoginModal : ComponentBase
{
    [Inject] private AuthState Auth { get; set; } = default!;

    [Parameter] public bool Open { get; set; }
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }
    [Parameter] public string Message { get; set; } = "Please log in to continue.";
    [Parameter] public EventCallback<UserResponse> OnAuthenticated { get; set; }

    private string username = "";
    private string password = "";
    private string feedback = "";
    private Severity feedbackSeverity = Severity.Info;
    private bool isBusy;

    private async Task Login()
    {
        await Authenticate(() => Auth.LoginAsync(username, password), "Login failed.");
    }

    private async Task Register()
    {
        await Authenticate(() => Auth.RegisterAsync(username, password), "Registration failed.");
    }

    private async Task Authenticate(Func<Task<UserResponse?>> action, string failureMessage)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            feedbackSeverity = Severity.Warning;
            feedback = "Enter your username and password first.";
            return;
        }

        isBusy = true;
        feedback = "";
        try
        {
            var user = await action();
            if (user is null)
            {
                feedbackSeverity = Severity.Error;
                feedback = failureMessage;
                return;
            }

            feedbackSeverity = Severity.Success;
            feedback = $"Signed in as {user.Username}.";
            await OnAuthenticated.InvokeAsync(user);
            await Close();
        }
        finally
        {
            isBusy = false;
        }
    }

    private async Task Close()
    {
        feedback = "";
        await OpenChanged.InvokeAsync(false);
    }
}
