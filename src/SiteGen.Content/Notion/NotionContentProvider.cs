using System.Text;
using System.Text.Json;
using SiteGen.Shared;

namespace SiteGen.Content.Notion;

public sealed class NotionContentProvider : IContentProvider
{
    private readonly NotionProviderOptions _options;

    public NotionContentProvider(NotionProviderOptions options)
    {
        _options = options;
    }

    public async Task<IReadOnlyList<ContentItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.DatabaseId))
        {
            throw new ContentException("Notion DatabaseId is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new ContentException("Notion Token is required.");
        }

        using var client = new NotionApiClient(_options);
        var renderer = new NotionBlocksRenderer(client);

        var items = new List<ContentItem>();
        var policyMode = NormalizePolicyMode(_options.FieldPolicyMode);
        var allowed = policyMode == "whitelist" ? BuildAllowedSet(_options.AllowedFields) : null;
        var (resolvedFilterProperty, resolvedSortProperty) = await ResolveDatabasePropertyNamesAsync(client, _options, cancellationToken);
        string? startCursor = null;

        while (true)
        {
            var query = BuildDatabaseQueryJson(_options, startCursor, resolvedFilterProperty, resolvedSortProperty);
            using var doc = await client.PostAsync($"https://api.notion.com/v1/databases/{_options.DatabaseId}/query", query, cancellationToken);
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                throw new ContentException("Notion query response missing results.");
            }

            foreach (var page in results.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pageId = GetString(page, "id");
                if (string.IsNullOrWhiteSpace(pageId))
                {
                    continue;
                }

                var props = page.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
                EnsureNoCaseInsensitiveConflicts(props, pageId);
                var title = ExtractTitle(props) ?? pageId;
                var slug = ExtractSlug(props) ?? Slugify(title) ?? pageId.Replace("-", string.Empty, StringComparison.Ordinal);
                var type = ExtractType(props) ?? "post";
                var publishAt = ExtractPublishAt(props) ?? DateTimeOffset.UtcNow;

                var contentHtml = _options.RenderContent
                    ? await renderer.RenderPageAsync(pageId, cancellationToken)
                    : string.Empty;

                var meta = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["type"] = type,
                    ["source"] = "notion",
                    ["notionPageId"] = pageId
                };

                var fields = ExtractFields(props, policyMode, allowed);
                PromoteFieldToMeta(fields, meta, "language", "language");
                PromoteFieldToMeta(fields, meta, "i18n_key", "i18nKey");
                PromoteFieldToMeta(fields, meta, "i18nkey", "i18nKey");
                PromoteFieldToMeta(fields, meta, "url", "url");
                PromoteFieldToMeta(fields, meta, "outputpath", "outputPath");
                PromoteFieldToMeta(fields, meta, "template", "template");
                PromoteTaxonomyFieldToMeta(fields, meta, "tags");
                PromoteTaxonomyFieldToMeta(fields, meta, "categories");

                items.Add(new ContentItem(
                    Id: pageId,
                    Title: title,
                    Slug: slug,
                    PublishAt: publishAt,
                    ContentHtml: contentHtml,
                    Meta: meta,
                    Fields: fields
                ));
            }

            if (root.TryGetProperty("has_more", out var hasMoreEl) && hasMoreEl.ValueKind == JsonValueKind.True)
            {
                startCursor = GetString(root, "next_cursor");
                if (string.IsNullOrWhiteSpace(startCursor))
                {
                    break;
                }

                continue;
            }

            break;
        }

        return items;
    }

    private static void PromoteFieldToMeta(IReadOnlyDictionary<string, ContentField> fields, Dictionary<string, object> meta, string fieldKey, string metaKey)
    {
        if (!fields.TryGetValue(fieldKey, out var field))
        {
            return;
        }

        if (field.Value is null)
        {
            return;
        }

        var text = field.Value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        meta[metaKey] = text.Trim();
    }

    private static void PromoteTaxonomyFieldToMeta(IReadOnlyDictionary<string, ContentField> fields, Dictionary<string, object> meta, string fieldKey)
    {
        if (!fields.TryGetValue(fieldKey, out var field) || field.Value is null)
        {
            return;
        }

        if (field.Value is string s)
        {
            var text = s.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                meta[fieldKey] = text;
            }
            return;
        }

        if (field.Value is IEnumerable<string> stringSeq)
        {
            var list = stringSeq
                .Select(x => x?.Trim() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<object>()
                .ToList();

            if (list.Count > 0)
            {
                meta[fieldKey] = list;
            }
            return;
        }

        if (field.Value is IEnumerable<object> objSeq)
        {
            var list = objSeq
                .Select(x => x?.ToString() ?? string.Empty)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<object>()
                .ToList();

            if (list.Count > 0)
            {
                meta[fieldKey] = list;
            }
        }
    }

    private static string NormalizePolicyMode(string? mode)
    {
        var m = (mode ?? "whitelist").Trim().ToLowerInvariant();
        return m is "all" ? "all" : "whitelist";
    }

    private static HashSet<string>? BuildAllowedSet(IReadOnlyList<string>? allowed)
    {
        if (allowed is null || allowed.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in allowed)
        {
            var k = NormalizeFieldKey(a);
            if (!string.IsNullOrWhiteSpace(k))
            {
                set.Add(k);
            }
        }

        return set;
    }

    private static IReadOnlyDictionary<string, ContentField> ExtractFields(JsonElement properties, string policyMode, HashSet<string>? allowed)
    {
        var fields = new Dictionary<string, ContentField>(StringComparer.OrdinalIgnoreCase);
        if (properties.ValueKind != JsonValueKind.Object)
        {
            return fields;
        }

        foreach (var prop in properties.EnumerateObject())
        {
            var rawName = prop.Name;
            if (string.IsNullOrWhiteSpace(rawName))
            {
                continue;
            }

            var key = NormalizeFieldKey(rawName);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (IsReservedNotionField(key))
            {
                continue;
            }

            if (policyMode == "whitelist" && allowed is not null && !allowed.Contains(key))
            {
                continue;
            }

            if (TryParseNotionPropertyToField(prop.Value, out var field))
            {
                fields[key] = field;
            }
        }

        return fields;
    }

    private static bool IsReservedNotionField(string normalizedKey)
    {
        return normalizedKey is "published" or "title" or "slug" or "type" or "publishat" or "publish_at";
    }

    private static string NormalizeFieldKey(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(trimmed.Length);
        var underscore = false;

        foreach (var ch in trimmed)
        {
            var lower = char.ToLowerInvariant(ch);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(lower);
                underscore = false;
                continue;
            }

            if (!underscore)
            {
                sb.Append('_');
                underscore = true;
            }
        }

        return sb.ToString().Trim('_');
    }

    private static bool TryParseNotionPropertyToField(JsonElement property, out ContentField field)
    {
        field = default!;
        if (!property.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var type = typeEl.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        switch (type)
        {
            case "title":
            {
                var text = ExtractPlainTextArray(property, "title");
                field = new ContentField("text", text);
                return !string.IsNullOrWhiteSpace(text);
            }
            case "rich_text":
            {
                var text = ExtractPlainTextArray(property, "rich_text");
                field = new ContentField("text", text);
                return !string.IsNullOrWhiteSpace(text);
            }
            case "number":
            {
                if (property.TryGetProperty("number", out var n) && n.ValueKind is JsonValueKind.Number)
                {
                    field = new ContentField("number", n.GetDouble());
                    return true;
                }
                return false;
            }
            case "checkbox":
            {
                if (property.TryGetProperty("checkbox", out var b) && b.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    field = new ContentField("bool", b.GetBoolean());
                    return true;
                }
                return false;
            }
            case "date":
            {
                if (property.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.Object &&
                    d.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.String)
                {
                    var text = start.GetString();
                    if (!string.IsNullOrWhiteSpace(text) && DateTimeOffset.TryParse(text, out var dto))
                    {
                        field = new ContentField("date", dto);
                        return true;
                    }
                }
                return false;
            }
            case "multi_select":
            {
                if (property.TryGetProperty("multi_select", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    var list = arr.EnumerateArray()
                        .Select(x => x.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!.Trim())
                        .ToList();

                    field = new ContentField("list", list);
                    return list.Count > 0;
                }
                return false;
            }
            case "select":
            {
                if (property.TryGetProperty("select", out var sel) && sel.ValueKind == JsonValueKind.Object &&
                    sel.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                {
                    var text = n.GetString() ?? string.Empty;
                    field = new ContentField("text", text);
                    return !string.IsNullOrWhiteSpace(text);
                }
                return false;
            }
            case "formula":
            {
                if (property.TryGetProperty("formula", out var f) && f.ValueKind == JsonValueKind.Object)
                {
                    return TryParseFormulaToField(f, out field);
                }
                return false;
            }
            case "files":
            {
                if (property.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in files.EnumerateArray())
                    {
                        if (f.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                        {
                            var ft = t.GetString();
                            if (string.Equals(ft, "external", StringComparison.OrdinalIgnoreCase) &&
                                f.TryGetProperty("external", out var ex) && ex.ValueKind == JsonValueKind.Object &&
                                ex.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                            {
                                field = new ContentField("file", url.GetString() ?? string.Empty);
                                return true;
                            }

                            if (string.Equals(ft, "file", StringComparison.OrdinalIgnoreCase) &&
                                f.TryGetProperty("file", out var ff) && ff.ValueKind == JsonValueKind.Object &&
                                ff.TryGetProperty("url", out var furl) && furl.ValueKind == JsonValueKind.String)
                            {
                                field = new ContentField("file", furl.GetString() ?? string.Empty);
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
            default:
                return false;
        }
    }

    private static string ExtractPlainTextArray(JsonElement property, string key)
    {
        if (!property.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
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

    private static string BuildDatabaseQueryJson(NotionProviderOptions options, string? startCursor, string? resolvedFilterProperty, string? resolvedSortProperty)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"page_size\":{options.PageSize},");

        var filterType = (options.FilterType ?? "checkbox_true").Trim().ToLowerInvariant();
        if (filterType == "checkbox_true")
        {
            var prop = (resolvedFilterProperty ?? options.FilterProperty ?? "Published").Trim();
            sb.Append("\"filter\":{");
            sb.Append($"\"property\":\"{EscapeJson(prop)}\",");
            sb.Append("\"checkbox\":{\"equals\":true}");
            sb.Append("},");
        }

        if (!string.IsNullOrWhiteSpace(resolvedSortProperty ?? options.SortProperty))
        {
            var prop = (resolvedSortProperty ?? options.SortProperty)!.Trim();
            var dir = (options.SortDirection ?? "ascending").Trim().ToLowerInvariant();
            if (dir is not ("ascending" or "descending"))
            {
                dir = "ascending";
            }

            sb.Append("\"sorts\":[{");
            sb.Append($"\"property\":\"{EscapeJson(prop)}\",");
            sb.Append($"\"direction\":\"{EscapeJson(dir)}\"");
            sb.Append("}],");
        }

        if (!string.IsNullOrWhiteSpace(startCursor))
        {
            sb.Append($"\"start_cursor\":\"{EscapeJson(startCursor)}\"");
        }
        else
        {
            if (sb[^1] == ',')
            {
                sb.Length--;
            }
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string? ExtractTitle(JsonElement properties)
    {
        if (properties.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryGetPropertyIgnoreCase(properties, "Title", out var titleProp))
        {
            var text = ExtractTitleProperty(titleProp);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        foreach (var prop in properties.EnumerateObject())
        {
            var v = prop.Value;
            if (GetString(v, "type") == "title")
            {
                var text = ExtractTitleProperty(v);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string? ExtractTitleProperty(JsonElement prop)
    {
        if (prop.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!prop.TryGetProperty("title", out var titleArray) || titleArray.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var item in titleArray.EnumerateArray())
        {
            if (item.TryGetProperty("plain_text", out var plain) && plain.ValueKind == JsonValueKind.String)
            {
                sb.Append(plain.GetString());
            }
        }

        return sb.ToString().Trim();
    }

    private static string? ExtractSlug(JsonElement properties)
    {
        if (properties.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryGetPropertyIgnoreCase(properties, "Slug", out var slugProp))
        {
            return null;
        }

        var type = GetString(slugProp, "type");
        if (type == "rich_text" && slugProp.TryGetProperty("rich_text", out var rt))
        {
            var text = ExtractPlainText(rt);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        if (type == "formula" && slugProp.TryGetProperty("formula", out var f) && f.ValueKind == JsonValueKind.Object)
        {
            var value = GetString(f, "string");
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return null;
    }

    private static string? ExtractType(JsonElement properties)
    {
        if (properties.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryGetPropertyIgnoreCase(properties, "Type", out var typeProp))
        {
            return null;
        }

        var t = GetString(typeProp, "type");
        if (t == "select" && typeProp.TryGetProperty("select", out var sel) && sel.ValueKind == JsonValueKind.Object)
        {
            return GetString(sel, "name");
        }

        if (t == "multi_select" && typeProp.TryGetProperty("multi_select", out var ms) && ms.ValueKind == JsonValueKind.Array)
        {
            var first = ms.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object)
            {
                return GetString(first, "name");
            }
        }

        return null;
    }

    private static DateTimeOffset? ExtractPublishAt(JsonElement properties)
    {
        if (properties.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryGetPropertyIgnoreCase(properties, "PublishAt", out var dateProp))
        {
            var value = ReadDateProperty(dateProp);
            if (value is not null)
            {
                return value;
            }
        }

        if (TryGetPropertyIgnoreCase(properties, "Date", out var dateProp2))
        {
            var value = ReadDateProperty(dateProp2);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadDateProperty(JsonElement prop)
    {
        var type = GetString(prop, "type");
        if (type != "date")
        {
            return null;
        }

        if (!prop.TryGetProperty("date", out var dateObj) || dateObj.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var start = GetString(dateObj, "start");
        if (string.IsNullOrWhiteSpace(start))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(start, out var dto))
        {
            return dto;
        }

        return null;
    }

    private static string? Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
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

                continue;
            }
        }

        var slug = sb.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? null : slug;
    }

    private static string ExtractPlainText(JsonElement richTextArray)
    {
        if (richTextArray.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var item in richTextArray.EnumerateArray())
        {
            if (item.TryGetProperty("plain_text", out var plainTextEl) && plainTextEl.ValueKind == JsonValueKind.String)
            {
                sb.Append(plainTextEl.GetString());
            }
        }

        return sb.ToString();
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v))
        {
            return null;
        }

        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryParseFormulaToField(JsonElement formula, out ContentField field)
    {
        field = default!;
        var type = GetString(formula, "type");
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        switch (type)
        {
            case "string":
            {
                var text = GetString(formula, "string") ?? string.Empty;
                field = new ContentField("text", text);
                return !string.IsNullOrWhiteSpace(text);
            }
            case "number":
            {
                if (formula.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Number)
                {
                    field = new ContentField("number", n.GetDouble());
                    return true;
                }
                return false;
            }
            case "boolean":
            {
                if (formula.TryGetProperty("boolean", out var b) && b.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    field = new ContentField("bool", b.GetBoolean());
                    return true;
                }
                return false;
            }
            case "date":
            {
                if (formula.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.Object &&
                    d.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.String)
                {
                    var text = start.GetString();
                    if (!string.IsNullOrWhiteSpace(text) && DateTimeOffset.TryParse(text, out var dto))
                    {
                        field = new ContentField("date", dto);
                        return true;
                    }
                }
                return false;
            }
        }

        return false;
    }

    private static void EnsureNoCaseInsensitiveConflicts(JsonElement properties, string pageId)
    {
        if (properties.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in properties.EnumerateObject())
        {
            var name = prop.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (seen.TryGetValue(name, out var existing))
            {
                throw new ContentException(
                    $"Notion properties have conflicting names ignoring case: '{existing}' and '{name}' (page: {pageId}). " +
                    "Rename one of them to a unique name (case-insensitive).");
            }

            seen[name] = name;
        }
    }

    private static async Task<(string? FilterProperty, string? SortProperty)> ResolveDatabasePropertyNamesAsync(
        NotionApiClient client,
        NotionProviderOptions options,
        CancellationToken cancellationToken)
    {
        var filterType = (options.FilterType ?? "checkbox_true").Trim().ToLowerInvariant();
        var filterProp = filterType == "checkbox_true" ? (options.FilterProperty ?? "Published").Trim() : null;
        var sortProp = options.SortProperty?.Trim();

        if (string.IsNullOrWhiteSpace(filterProp) && string.IsNullOrWhiteSpace(sortProp))
        {
            return (null, null);
        }

        using var doc = await client.GetAsync($"https://api.notion.com/v1/databases/{options.DatabaseId}", cancellationToken);
        var root = doc.RootElement;

        if (!root.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
        {
            throw new ContentException("Notion database schema missing properties.");
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in props.EnumerateObject())
        {
            var name = prop.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (map.TryGetValue(name, out var existing))
            {
                throw new ContentException(
                    $"Notion database has conflicting property names ignoring case: '{existing}' and '{name}'. " +
                    "Rename one of them to a unique name (case-insensitive).");
            }

            map[name] = name;
        }

        string? resolvedFilter = null;
        if (!string.IsNullOrWhiteSpace(filterProp))
        {
            if (!map.TryGetValue(filterProp, out resolvedFilter))
            {
                var available = string.Join(", ", map.Values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                throw new ContentException(
                    $"Notion database property '{filterProp}' not found (case-insensitive match). " +
                    $"Available properties: {available}.");
            }
        }

        string? resolvedSort = null;
        if (!string.IsNullOrWhiteSpace(sortProp))
        {
            if (!map.TryGetValue(sortProp, out resolvedSort))
            {
                var available = string.Join(", ", map.Values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                throw new ContentException(
                    $"Notion database property '{sortProp}' not found (case-insensitive match). " +
                    $"Available properties: {available}.");
            }
        }

        return (resolvedFilter, resolvedSort);
    }
}
