using System.Text;
using System.Text.Json;
using SiteGen.Content;
using SiteGen.Content.Notion;
using SiteGen.Routing;

namespace SiteGen.Engine.Plugins.BuiltIn;

public sealed record NotionFetchedPage(
    string PageId,
    string Title,
    string Slug,
    string NotionUrl,
    IReadOnlyDictionary<string, ContentField> Fields);

public interface INotionPageFetcher
{
    Task<NotionFetchedPage?> FetchAsync(NotionApiClient client, string pageId, CancellationToken cancellationToken);
}

public sealed class PagesIndexPlugin : ISiteGenPlugin, IDerivePagesPlugin
{
    private readonly INotionPageFetcher _notionFetcher;

    public string Name => "pages-index";
    public string Version => "1.0.0";

    public PagesIndexPlugin()
        : this(new DefaultNotionPageFetcher())
    {
    }

    public PagesIndexPlugin(INotionPageFetcher notionFetcher)
    {
        _notionFetcher = notionFetcher;
    }

    public IReadOnlyList<(ContentItem Item, RouteInfo Route, DateTimeOffset LastModified)> DerivePages(BuildContext context)
    {
        var index = GetOrCreateIndex(context);
        AddRoutedToIndex(context, index);
        ResolveNotionRelationsIfConfigured(context, index);
        if (index.Count > 0)
        {
            context.Data["pages_by_id"] = index;
        }
        return Array.Empty<(ContentItem Item, RouteInfo Route, DateTimeOffset LastModified)>();
    }

    private static Dictionary<string, object> GetOrCreateIndex(BuildContext context)
    {
        if (context.Data.TryGetValue("pages_by_id", out var existing) &&
            existing is Dictionary<string, object> dict)
        {
            return dict;
        }

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddRoutedToIndex(BuildContext context, Dictionary<string, object> index)
    {
        foreach (var (item, route) in context.Routed)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                continue;
            }

            index[item.Id] = BuildPageObject(item, route);
        }
    }

    private static Dictionary<string, object> BuildPageObject(ContentItem item, RouteInfo route)
    {
        var type = item.Meta.TryGetValue("type", out var t) ? t?.ToString() : null;
        type = string.IsNullOrWhiteSpace(type) ? null : type.Trim();

        var summary = item.Meta.TryGetValue("summary", out var s) ? s?.ToString() : null;
        summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = item.Id,
            ["title"] = item.Title,
            ["url"] = route.Url,
            ["slug"] = item.Slug,
            ["type"] = type ?? string.Empty,
            ["publish_date"] = item.PublishAt.DateTime,
            ["summary"] = summary ?? string.Empty,
            ["fields"] = BuildFieldsObject(item.Fields)
        };
    }

    private static Dictionary<string, object> BuildFieldsObject(IReadOnlyDictionary<string, ContentField>? fields)
    {
        var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (fields is null || fields.Count == 0)
        {
            return obj;
        }

        foreach (var kv in fields)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            var f = kv.Value;
            obj[kv.Key] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = f.Type,
                ["value"] = f.Value ?? string.Empty
            };
        }

        return obj;
    }

    private void ResolveNotionRelationsIfConfigured(BuildContext context, Dictionary<string, object> index)
    {
        if (!HasNotionContent(context.Config))
        {
            return;
        }

        if (context.Config.Theme.Params is null || context.Config.Theme.Params.Count == 0)
        {
            return;
        }

        if (!TryGetMap(context.Config.Theme.Params, "pages_index", out var pagesIndexCfg))
        {
            return;
        }

        if (!TryGetMap(pagesIndexCfg, "resolve_notion", out var resolveCfg))
        {
            return;
        }

        if (!TryGetBool(resolveCfg, "enabled", false))
        {
            return;
        }

        var fieldKeys = TryGetStringList(resolveCfg, "field_keys");
        if (fieldKeys.Count == 0)
        {
            return;
        }

        var maxItems = TryGetInt(resolveCfg, "max_items", 200);
        if (maxItems <= 0)
        {
            return;
        }

        var ids = CollectRelationIds(context.Routed, fieldKeys, index, maxItems);
        if (ids.Count == 0)
        {
            return;
        }

        var cacheMode = NormalizeCacheMode(TryGetString(resolveCfg, "cache_mode") ?? "readwrite");
        var cachePath = ResolveCachePath(context.RootDir, TryGetString(resolveCfg, "cache_path"));
        if (cacheMode != "off")
        {
            var cached = TryLoadCache(cachePath);
            if (cached is not null && cached.Count > 0)
            {
                var toFetch = new List<string>();
                for (var i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    if (index.ContainsKey(id))
                    {
                        continue;
                    }

                    if (cached.TryGetValue(id, out var cachedPage))
                    {
                        index[id] = cachedPage;
                    }
                    else
                    {
                        toFetch.Add(id);
                    }
                }

                ids = toFetch;
                if (ids.Count == 0)
                {
                    if (cacheMode == "readwrite")
                    {
                        TrySaveCache(cachePath, index);
                    }
                    return;
                }
            }
            else if (cacheMode == "readonly")
            {
                return;
            }
        }

        if (cacheMode == "readonly")
        {
            return;
        }

        var token = Environment.GetEnvironmentVariable("NOTION_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var concurrency = TryGetInt(resolveCfg, "concurrency", 4);
        if (concurrency <= 0)
        {
            concurrency = 4;
        }

        var maxRps = TryGetNullableInt(resolveCfg, "max_rps");
        var maxRetries = TryGetInt(resolveCfg, "max_retries", 5);
        var requestDelayMs = TryGetInt(resolveCfg, "request_delay_ms", 0);

        var opts = new NotionProviderOptions
        {
            DatabaseId = "dummy",
            Token = token.Trim(),
            MaxRetries = maxRetries,
            MaxRps = maxRps,
            RequestDelayMs = requestDelayMs
        };

        using var client = new NotionApiClient(opts);
        using var sem = new SemaphoreSlim(concurrency, concurrency);
        var tasks = new Task<NotionFetchedPage?>[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            tasks[i] = ResolveOneAsync(id);
        }

        Task.WaitAll(tasks);

        for (var i = 0; i < tasks.Length; i++)
        {
            var p = tasks[i].Result;
            if (p is null)
            {
                continue;
            }

            if (index.ContainsKey(p.PageId))
            {
                continue;
            }

            var pageType = GetTypeFromFields(p.Fields) ?? "notion";
            index[p.PageId] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = p.PageId,
                ["title"] = p.Title,
                ["url"] = string.Empty,
                ["external_url"] = p.NotionUrl,
                ["slug"] = p.Slug,
                ["type"] = pageType,
                ["publish_date"] = null!,
                ["summary"] = string.Empty,
                ["fields"] = BuildFieldsObject(p.Fields)
            };
        }

        if (cacheMode == "readwrite")
        {
            TrySaveCache(cachePath, index);
        }

        async Task<NotionFetchedPage?> ResolveOneAsync(string pageId)
        {
            await sem.WaitAsync(CancellationToken.None);
            try
            {
                return await _notionFetcher.FetchAsync(client, pageId, CancellationToken.None);
            }
            finally
            {
                sem.Release();
            }
        }
    }

    private static string? GetTypeFromFields(IReadOnlyDictionary<string, ContentField> fields)
    {
        if (fields.TryGetValue("type", out var f) && f.Value is not null)
        {
            var s = f.Value.ToString();
            s = string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            return s;
        }

        return null;
    }

    private static string NormalizeCacheMode(string mode)
    {
        return (mode ?? "off").Trim().ToLowerInvariant() switch
        {
            "readonly" => "readonly",
            "readwrite" => "readwrite",
            _ => "off"
        };
    }

    private static string ResolveCachePath(string rootDir, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var raw = configured.Trim();
            return Path.IsPathRooted(raw) ? raw : Path.Combine(rootDir, raw);
        }

        return Path.Combine(rootDir, ".cache", "notion", "pages-index.json");
    }

    private static Dictionary<string, object>? TryLoadCache(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var obj = ToObject(doc.RootElement);
            return obj as Dictionary<string, object>;
        }
        catch
        {
            return null;
        }
    }

    private static void TrySaveCache(string path, Dictionary<string, object> index)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var cache = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in index)
            {
                if (kv.Value is not Dictionary<string, object> page)
                {
                    continue;
                }

                if (!page.TryGetValue("url", out var urlObj) || urlObj is not string url || !string.IsNullOrEmpty(url))
                {
                    continue;
                }

                if (!page.ContainsKey("external_url"))
                {
                    continue;
                }

                cache[kv.Key] = page;
            }

            using var fs = File.Create(path);
            using var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = false });
            writer.WriteStartObject();
            foreach (var kv in cache)
            {
                writer.WritePropertyName(kv.Key);
                WriteJsonValue(writer, kv.Value);
            }
            writer.WriteEndObject();
            writer.Flush();
        }
        catch
        {
        }
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, object? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value is string s)
        {
            writer.WriteStringValue(s);
            return;
        }

        if (value is bool b)
        {
            writer.WriteBooleanValue(b);
            return;
        }

        if (value is int i)
        {
            writer.WriteNumberValue(i);
            return;
        }

        if (value is long l)
        {
            writer.WriteNumberValue(l);
            return;
        }

        if (value is double d)
        {
            writer.WriteNumberValue(d);
            return;
        }

        if (value is float f)
        {
            writer.WriteNumberValue(f);
            return;
        }

        if (value is decimal dec)
        {
            writer.WriteNumberValue(dec);
            return;
        }

        if (value is DateTimeOffset dto)
        {
            writer.WriteStringValue(dto.ToString("O"));
            return;
        }

        if (value is DateTime dt)
        {
            writer.WriteStringValue(dt.ToString("O"));
            return;
        }

        if (value is IReadOnlyDictionary<string, object> roDict)
        {
            writer.WriteStartObject();
            foreach (var kv in roDict)
            {
                writer.WritePropertyName(kv.Key);
                WriteJsonValue(writer, kv.Value);
            }
            writer.WriteEndObject();
            return;
        }

        if (value is IDictionary<string, object> dict)
        {
            writer.WriteStartObject();
            foreach (var kv in dict)
            {
                writer.WritePropertyName(kv.Key);
                WriteJsonValue(writer, kv.Value);
            }
            writer.WriteEndObject();
            return;
        }

        if (value is System.Collections.IEnumerable seq)
        {
            writer.WriteStartArray();
            foreach (var x in seq)
            {
                WriteJsonValue(writer, x);
            }
            writer.WriteEndArray();
            return;
        }

        writer.WriteStringValue(value.ToString());
    }

    private static object? ToObject(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Object => ToDictionary(el),
            JsonValueKind.Array => el.EnumerateArray().Select(ToObject).ToList(),
            JsonValueKind.String => el.GetString() ?? string.Empty,
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static Dictionary<string, object> ToDictionary(JsonElement el)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in el.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(p.Name))
            {
                continue;
            }

            dict[p.Name] = ToObject(p.Value) ?? string.Empty;
        }

        return dict;
    }

    private static bool HasNotionContent(SiteGen.Config.AppConfig config)
    {
        if (string.Equals(config.Content.Provider, "notion", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (config.Content.Sources is null || config.Content.Sources.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < config.Content.Sources.Count; i++)
        {
            if (config.Content.Sources[i].Notion is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> CollectRelationIds(
        IReadOnlyList<(ContentItem Item, RouteInfo Route)> routed,
        IReadOnlyList<string> fieldKeys,
        Dictionary<string, object> index,
        int maxItems)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        for (var i = 0; i < routed.Count; i++)
        {
            var fields = routed[i].Item.Fields;
            if (fields is null || fields.Count == 0)
            {
                continue;
            }

            for (var k = 0; k < fieldKeys.Count; k++)
            {
                var key = fieldKeys[k];
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!fields.TryGetValue(key, out var f) || f.Value is null)
                {
                    continue;
                }

                if (f.Value is not IEnumerable<string> ids)
                {
                    continue;
                }

                foreach (var raw in ids)
                {
                    var id = (raw ?? string.Empty).Trim();
                    if (id.Length == 0 || index.ContainsKey(id))
                    {
                        continue;
                    }

                    if (set.Add(id))
                    {
                        list.Add(id);
                        if (list.Count >= maxItems)
                        {
                            return list;
                        }
                    }
                }
            }
        }

        return list;
    }

    private static bool TryGetMap(IReadOnlyDictionary<string, object> map, string key, out IReadOnlyDictionary<string, object> value)
    {
        value = null!;
        if (!map.TryGetValue(key, out var obj) || obj is null)
        {
            return false;
        }

        if (obj is IReadOnlyDictionary<string, object> ro)
        {
            value = ro;
            return true;
        }

        if (obj is IDictionary<string, object> dict)
        {
            value = new Dictionary<string, object>(dict, StringComparer.OrdinalIgnoreCase);
            return true;
        }

        return false;
    }

    private static string? TryGetString(IReadOnlyDictionary<string, object> map, string key)
    {
        if (!map.TryGetValue(key, out var obj) || obj is null)
        {
            return null;
        }

        if (obj is string s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        var text = obj.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool TryGetBool(IReadOnlyDictionary<string, object> map, string key, bool defaultValue)
    {
        if (!map.TryGetValue(key, out var obj) || obj is null)
        {
            return defaultValue;
        }

        if (obj is bool b)
        {
            return b;
        }

        if (bool.TryParse(obj.ToString(), out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static int TryGetInt(IReadOnlyDictionary<string, object> map, string key, int defaultValue)
    {
        if (!map.TryGetValue(key, out var obj) || obj is null)
        {
            return defaultValue;
        }

        if (obj is int i)
        {
            return i;
        }

        if (obj is long l)
        {
            return (int)l;
        }

        if (int.TryParse(obj.ToString(), out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static int? TryGetNullableInt(IReadOnlyDictionary<string, object> map, string key)
    {
        if (!map.TryGetValue(key, out var obj) || obj is null)
        {
            return null;
        }

        if (obj is int i)
        {
            return i;
        }

        if (obj is long l)
        {
            return (int)l;
        }

        return int.TryParse(obj.ToString(), out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<string> TryGetStringList(IReadOnlyDictionary<string, object> map, string key)
    {
        if (!map.TryGetValue(key, out var obj) || obj is null)
        {
            return Array.Empty<string>();
        }

        if (obj is IEnumerable<object> seq)
        {
            var items = seq.Select(x => x?.ToString() ?? string.Empty)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            return items;
        }

        if (obj is string s)
        {
            var items = s.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            return items;
        }

        return Array.Empty<string>();
    }

    private sealed class DefaultNotionPageFetcher : INotionPageFetcher
    {
        public async Task<NotionFetchedPage?> FetchAsync(NotionApiClient client, string pageId, CancellationToken cancellationToken)
        {
            try
            {
                using var doc = await client.GetAsync($"https://api.notion.com/v1/pages/{pageId}", cancellationToken);
                var page = doc.RootElement;

                var notionUrl = page.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                notionUrl = string.IsNullOrWhiteSpace(notionUrl) ? string.Empty : notionUrl.Trim();

                var props = page.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
                var title = ExtractTitle(props);
                title = string.IsNullOrWhiteSpace(title) ? pageId : title.Trim();

                var slug = Slugify(title);
                if (string.IsNullOrWhiteSpace(slug))
                {
                    slug = pageId.Replace("-", string.Empty, StringComparison.Ordinal);
                }

                var fields = NotionPropertyParser.ExtractAllFields(props);
                return new NotionFetchedPage(pageId, title, slug, notionUrl, fields);
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractTitle(JsonElement props)
        {
            if (props.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            foreach (var prop in props.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!prop.Value.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!string.Equals(typeEl.GetString(), "title", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!prop.Value.TryGetProperty("title", out var arr) || arr.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                return ExtractPlainTextArray(arr);
            }

            return string.Empty;
        }

        private static string ExtractPlainTextArray(JsonElement arr)
        {
            if (arr.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("plain_text", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    var s = t.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        if (sb.Length > 0)
                        {
                            sb.Append(' ');
                        }
                        sb.Append(s.Trim());
                    }
                }
            }

            return sb.ToString();
        }

        private static string Slugify(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            var lastDash = false;
            foreach (var ch in text.Trim())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                    lastDash = false;
                    continue;
                }

                if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_')
                {
                    if (!lastDash && sb.Length > 0)
                    {
                        sb.Append('-');
                        lastDash = true;
                    }
                }
            }

            return sb.ToString().Trim('-');
        }
    }
}
