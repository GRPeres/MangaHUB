using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MangaHub.Api.Services;

public sealed class ChatGptProposalProvider(IConfiguration configuration, IHttpClientFactory clients)
{
    private static readonly SemaphoreSlim CredentialLock = new(1);
    private string? Path => configuration["MangaHub:IssueProposals:ChatGptCredentialFile"];
    public bool Enabled => !string.IsNullOrWhiteSpace(Path);
    public bool IsConnected => Enabled && File.Exists(Path);

    private sealed record Credentials(string client_id, string access_token, string refresh_token, DateTimeOffset expires_at);

    public async Task<string> DraftAsync(string model, string instructions, string input, CancellationToken token)
    {
        var credentials = await GetCredentialsAsync(token);
        using var client = clients.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new("Bearer", credentials.access_token);
        request.Headers.UserAgent.ParseAdd("MangaHub/1.0");
        request.Content = JsonContent.Create(new
        {
            model, store = false, stream = true, instructions,
            input = new[] { new { role = "user", content = input } }
        });
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
        var result = new StringBuilder();
        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (!line.StartsWith("data: {", StringComparison.Ordinal)) continue;
            using var evt = JsonDocument.Parse(line[6..]);
            var node = evt.RootElement;
            switch (node.GetProperty("type").GetString())
            {
                case "response.output_text.delta":
                    result.Append(node.GetProperty("delta").GetString());
                    if (result.Length > 24000) throw new InvalidOperationException("ChatGPT proposal exceeded the local response limit.");
                    break;
                case "response.completed":
                    if (string.IsNullOrWhiteSpace(result.ToString())) throw new InvalidOperationException("ChatGPT returned no proposal.");
                    return result.ToString();
                case "response.failed":
                case "response.incomplete":
                case "error":
                    throw new InvalidOperationException("ChatGPT could not complete the proposal.");
            }
        }
        throw new InvalidOperationException("ChatGPT proposal stream ended before completion.");
    }

    private async Task<Credentials> GetCredentialsAsync(CancellationToken token)
    {
        await CredentialLock.WaitAsync(token);
        try
        {
            var path = Path ?? throw new InvalidOperationException("ChatGPT is not configured.");
            if (!File.Exists(path)) throw new InvalidOperationException("Connect ChatGPT using the MangaHub companion.");
            var saved = JsonSerializer.Deserialize<Credentials>(await File.ReadAllTextAsync(path, token))
                ?? throw new InvalidOperationException("ChatGPT credentials are missing.");
            if (saved.expires_at > DateTimeOffset.UtcNow.AddMinutes(2)) return saved;
            using var client = clients.CreateClient();
            using var response = await client.PostAsync("https://auth.openai.com/api/accounts/oauth/token", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token", ["client_id"] = saved.client_id,
                    ["refresh_token"] = saved.refresh_token, ["resource"] = "https://api.openai.com/v1"
                }), token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("ChatGPT renewal failed. Reconnect using the MangaHub companion.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var root = json.RootElement;
            if (root.TryGetProperty("scope", out var scope) && !(scope.GetString() ?? "").Split(' ').Contains("chatgpt.tokens.use.direct"))
                throw new InvalidOperationException("ChatGPT plan permission is missing.");
            var renewed = saved with
            {
                access_token = root.GetProperty("access_token").GetString()!,
                refresh_token = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString()! : saved.refresh_token,
                expires_at = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32())
            };
            var temporary = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                await using (var stream = new FileStream(temporary, options))
                    await JsonSerializer.SerializeAsync(stream, renewed, cancellationToken: CancellationToken.None);
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return renewed;
        }
        finally { CredentialLock.Release(); }
    }
}
