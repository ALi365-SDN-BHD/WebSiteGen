using System.Net;
using System.Net.Http.Headers;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace SiteGen.Cli.Commands;

public static class WebhookCommand
{
    public static async Task<int> RunAsync(ArgReader reader)
    {
        var host = (reader.GetOption("--host") ?? "localhost").Trim();
        var portText = (reader.GetOption("--port") ?? "8787").Trim();
        var path = NormalizePath(reader.GetOption("--path") ?? "/webhook/notion");

        if (!int.TryParse(portText, out var port) || port <= 0 || port > 65535)
        {
            Console.Error.WriteLine("Invalid --port.");
            return 2;
        }

        var token = Environment.GetEnvironmentVariable("SITEGEN_WEBHOOK_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("Missing env: SITEGEN_WEBHOOK_TOKEN");
            return 2;
        }

        var repo = reader.GetOption("--repo") ?? Environment.GetEnvironmentVariable("SITEGEN_GITHUB_REPO");
        if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/', StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Missing --repo <owner/repo> or env: SITEGEN_GITHUB_REPO");
            return 2;
        }

        var githubToken =
            Environment.GetEnvironmentVariable("SITEGEN_GITHUB_TOKEN") ??
            Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        if (string.IsNullOrWhiteSpace(githubToken))
        {
            Console.Error.WriteLine("Missing env: SITEGEN_GITHUB_TOKEN (or GITHUB_TOKEN)");
            return 2;
        }

        var eventType = reader.GetOption("--event") ?? "sitegen_notion";
        var prefix = $"http://{host}:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        Console.WriteLine($"Webhook: {prefix.TrimEnd('/')}{path}");
        Console.WriteLine("Press Ctrl+C to stop.");

        using var http = CreateGitHubHttpClient(githubToken);
        var gate = new SemaphoreSlim(1, 1);

        while (true)
        {
            var ctx = await listener.GetContextAsync();
            _ = Task.Run(() => HandleRequestAsync(ctx, path, token.Trim(), repo.Trim(), eventType.Trim(), http, gate));
        }
    }

    private static async Task HandleRequestAsync(
        HttpListenerContext context,
        string path,
        string token,
        string repo,
        string eventType,
        HttpClient http,
        SemaphoreSlim gate)
    {
        try
        {
            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 405;
                context.Response.Close();
                return;
            }

            if (!string.Equals(context.Request.Url?.AbsolutePath, path, StringComparison.Ordinal))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            var headerToken = context.Request.Headers["X-Sitegen-Token"];
            if (!string.Equals(headerToken, token, StringComparison.Ordinal))
            {
                context.Response.StatusCode = 401;
                context.Response.Close();
                return;
            }

            await gate.WaitAsync();
            try
            {
                await DispatchAsync(http, repo, eventType);
            }
            finally
            {
                gate.Release();
            }

            context.Response.StatusCode = 202;
            context.Response.Close();
        }
        catch
        {
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch
            {
            }
        }
    }

    private static async Task DispatchAsync(HttpClient http, string repo, string eventType)
    {
        var url = $"https://api.github.com/repos/{repo}/dispatches";
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("event_type", eventType);
            writer.WritePropertyName("client_payload");
            writer.WriteStartObject();
            writer.WriteString("source", "sitegen-webhook");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        using var content = new ByteArrayContent(buffer.WrittenSpan.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        using var res = await http.PostAsync(url, content);

        if (!res.IsSuccessStatusCode)
        {
            var text = await res.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"GitHub dispatch failed: {(int)res.StatusCode} {res.ReasonPhrase} {text}");
        }
    }

    private static HttpClient CreateGitHubHttpClient(string token)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("sitegen", "2"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static string NormalizePath(string path)
    {
        var trimmed = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "/webhook/notion";
        }

        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }

        return trimmed;
    }
}
