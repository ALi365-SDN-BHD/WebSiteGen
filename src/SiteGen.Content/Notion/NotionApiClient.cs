using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SiteGen.Shared;

namespace SiteGen.Content.Notion;

public sealed class NotionApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly NotionProviderOptions _options;

    public NotionApiClient(NotionProviderOptions options)
    {
        _options = options;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        _http.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<JsonDocument> PostAsync(string url, string json, CancellationToken cancellationToken)
    {
        await MaybeDelayAsync(cancellationToken);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    public async Task<JsonDocument> GetAsync(string url, CancellationToken cancellationToken)
    {
        await MaybeDelayAsync(cancellationToken);
        using var response = await _http.GetAsync(url, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ContentException($"Notion request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (Exception ex)
        {
            throw new ContentException("Notion returned invalid json.", ex);
        }
    }

    private Task MaybeDelayAsync(CancellationToken cancellationToken)
    {
        if (_options.RequestDelayMs <= 0)
        {
            return Task.CompletedTask;
        }

        return Task.Delay(_options.RequestDelayMs, cancellationToken);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}

