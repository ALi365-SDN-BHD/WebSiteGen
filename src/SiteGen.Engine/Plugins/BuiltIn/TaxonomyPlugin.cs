using System.Text;
using System.Text.Json;
using SiteGen.Config;
using SiteGen.Content;
using SiteGen.Routing;

namespace SiteGen.Engine.Plugins.BuiltIn;

public sealed class TaxonomyPlugin : ISiteGenPlugin, IDerivePagesPlugin, IAfterBuildPlugin
{
    public string Name => "taxonomy";
    public string Version => "2.3.1";

    public IReadOnlyList<(ContentItem Item, RouteInfo Route, DateTimeOffset LastModified)> DerivePages(BuildContext context)
    {
        var derived = new List<(ContentItem Item, RouteInfo Route, DateTimeOffset LastModified)>();
        var outputMode = NormalizeOutputMode(context.Config.Taxonomy.OutputMode);
        var itemFields = NormalizeItemFields(context.Config.Taxonomy.ItemFields);
        var pageSize = NormalizePageSize(context.Config.Taxonomy.PageSize);
        SetTaxonomyData(context, itemFields);
        if (outputMode == "data")
        {
            return derived;
        }

        var emitContentHtml = outputMode != "fields_only";

        if (context.Config.Taxonomy.Kinds is { Count: > 0 } kinds)
        {
            var baseUrlPrefix = context.BaseUrl == "/" ? string.Empty : context.BaseUrl;
            foreach (var kindConfig in kinds)
            {
                var key = (kindConfig.Key ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var kind = string.IsNullOrWhiteSpace(kindConfig.Kind) ? key : kindConfig.Kind.Trim();
                var terms = BuildIndex(context.Routed, key, itemFields);
                if (terms.Count == 0)
                {
                    continue;
                }

                var templates = ResolveTemplates(context.Config.Taxonomy, kind, kindConfig);
                var title = string.IsNullOrWhiteSpace(kindConfig.Title) ? kind : kindConfig.Title.Trim();
                var singularTitlePrefix = string.IsNullOrWhiteSpace(kindConfig.SingularTitlePrefix)
                    ? title
                    : kindConfig.SingularTitlePrefix.Trim();
                var indexEnabled = kindConfig.IndexEnabled ?? context.Config.Taxonomy.IndexEnabled;

                derived.AddRange(CreateKind(baseUrlPrefix, kind, title, singularTitlePrefix, terms, templates.IndexTemplate, templates.TermTemplate, emitContentHtml, pageSize, indexEnabled));
            }

            return derived;
        }

        var tags = BuildIndex(context.Routed, "tags", itemFields);
        var categories = BuildIndex(context.Routed, "categories", itemFields);

        if (tags.Count == 0 && categories.Count == 0)
        {
            return derived;
        }

        var prefix = context.BaseUrl == "/" ? string.Empty : context.BaseUrl;

        if (tags.Count > 0)
        {
            var templates = ResolveTemplates(context.Config.Taxonomy, kind: "tags");
            derived.AddRange(CreateKind(prefix, kind: "tags", title: "Tags", singularTitlePrefix: "Tag", tags, templates.IndexTemplate, templates.TermTemplate, emitContentHtml, pageSize, context.Config.Taxonomy.IndexEnabled));
        }

        if (categories.Count > 0)
        {
            var templates = ResolveTemplates(context.Config.Taxonomy, kind: "categories");
            derived.AddRange(CreateKind(prefix, kind: "categories", title: "Categories", singularTitlePrefix: "Category", categories, templates.IndexTemplate, templates.TermTemplate, emitContentHtml, pageSize, context.Config.Taxonomy.IndexEnabled));
        }

        return derived;
    }

    public void AfterBuild(BuildContext context)
    {
        var outputMode = NormalizeOutputMode(context.Config.Taxonomy.OutputMode);
        if (outputMode is not ("both" or "data"))
        {
            return;
        }

        var itemFields = NormalizeItemFields(context.Config.Taxonomy.ItemFields);
        var outPath = Path.Combine(context.OutputDir, "taxonomy.json");
        Directory.CreateDirectory(context.OutputDir);

        using var stream = File.Create(outPath);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();
        writer.WriteNumber("schema", 1);

        writer.WriteStartArray("kinds");
        if (context.Config.Taxonomy.Kinds is { Count: > 0 } kinds)
        {
            foreach (var kindConfig in kinds)
            {
                var key = (kindConfig.Key ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var kind = string.IsNullOrWhiteSpace(kindConfig.Kind) ? key : kindConfig.Kind.Trim();
                var terms = BuildIndex(context.Routed, key, itemFields);
                if (terms.Count == 0)
                {
                    continue;
                }

                var title = string.IsNullOrWhiteSpace(kindConfig.Title) ? kind : kindConfig.Title.Trim();
                WriteKind(writer, context.BaseUrl, key, kind, title, terms);
            }
        }
        else
        {
            var tags = BuildIndex(context.Routed, "tags", itemFields);
            if (tags.Count > 0)
            {
                WriteKind(writer, context.BaseUrl, key: "tags", kind: "tags", title: "Tags", tags);
            }

            var categories = BuildIndex(context.Routed, "categories", itemFields);
            if (categories.Count > 0)
            {
                WriteKind(writer, context.BaseUrl, key: "categories", kind: "categories", title: "Categories", categories);
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    private static Dictionary<string, TaxonomyTerm> BuildIndex(
        IReadOnlyList<(ContentItem Item, RouteInfo Route)> routed,
        string key,
        IReadOnlyList<string> itemFields)
    {
        var terms = new Dictionary<string, TaxonomyTerm>(StringComparer.OrdinalIgnoreCase);

        foreach (var (item, route) in routed)
        {
            var values = GetStringList(item.Meta, key);
            if (values is null || values.Count == 0)
            {
                continue;
            }

            var summary = item.Meta.TryGetValue("summary", out var summaryObj) ? summaryObj?.ToString() : null;
            var extra = ExtractExtraFields(item, itemFields);
            foreach (var raw in values)
            {
                var display = (raw ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(display))
                {
                    continue;
                }

                var slug = Slugify(display);
                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                if (!terms.TryGetValue(slug, out var term))
                {
                    term = new TaxonomyTerm(display, slug);
                    terms[slug] = term;
                }

                term.Pages.Add(new TaxonomyPage(item.Title, route.Url, item.PublishAt, summary, extra));
            }
        }

        foreach (var term in terms.Values)
        {
            term.Pages.Sort((a, b) => b.PublishAt.CompareTo(a.PublishAt));
        }

        return terms;
    }

    private static IReadOnlyList<(ContentItem Item, RouteInfo Route, DateTimeOffset LastModified)> CreateKind(
        string baseUrlPrefix,
        string kind,
        string title,
        string singularTitlePrefix,
        Dictionary<string, TaxonomyTerm> terms,
        string indexTemplate,
        string termTemplate,
        bool emitContentHtml,
        int pageSize,
        bool indexEnabled)
    {
        var derived = new List<(ContentItem Item, RouteInfo Route, DateTimeOffset LastModified)>();
        var items = terms.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

        var now = DateTimeOffset.UtcNow;
        if (indexEnabled)
        {
            derived.Add(CreateIndexPage(baseUrlPrefix, kind, title, items, indexTemplate, publishAt: now, emitContentHtml));
        }

        foreach (var term in items)
        {
            if (term.Pages.Count == 0)
            {
                derived.Add(CreateTermPage(
                    baseUrlPrefix,
                    kind,
                    singularTitlePrefix,
                    term,
                    termTemplate,
                    publishAt: now,
                    emitContentHtml,
                    pageSize,
                    page: 1,
                    totalPages: 1,
                    items: Array.Empty<TaxonomyPage>()));
                continue;
            }

            var totalPages = (int)Math.Ceiling(term.Pages.Count / (double)pageSize);
            for (var page = 1; page <= totalPages; page++)
            {
                var skip = (page - 1) * pageSize;
                var chunk = term.Pages.Skip(skip).Take(pageSize).ToList();
                var publishAt = chunk.Count == 0 ? now : chunk[0].PublishAt;
                derived.Add(CreateTermPage(
                    baseUrlPrefix,
                    kind,
                    singularTitlePrefix,
                    term,
                    termTemplate,
                    publishAt,
                    emitContentHtml,
                    pageSize,
                    page,
                    totalPages,
                    chunk));
            }
        }

        return derived;
    }

    private static (ContentItem Item, RouteInfo Route, DateTimeOffset LastModified) CreateIndexPage(
        string baseUrlPrefix,
        string kind,
        string title,
        IReadOnlyList<TaxonomyTerm> terms,
        string template,
        DateTimeOffset publishAt,
        bool emitContentHtml)
    {
        var html = string.Empty;
        if (emitContentHtml)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<ul>");
            foreach (var term in terms)
            {
                var href = $"{baseUrlPrefix}/{kind}/{term.Slug}/";
                sb.AppendLine($"  <li><a href=\"{EscapeAttr(href)}\">{EscapeHtml(term.DisplayName)}</a> <small>({term.Pages.Count})</small></li>");
            }
            sb.AppendLine("</ul>");
            html = sb.ToString();
        }

        var url = "/" + kind + "/";
        var outputPath = Path.Combine(kind, "index.html");
        var route = new RouteInfo(url, outputPath, template);
        var meta = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["type"] = "page"
        };

        var termsValue = new List<object>(terms.Count);
        foreach (var term in terms)
        {
            termsValue.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = term.DisplayName,
                ["slug"] = term.Slug,
                ["url"] = "/" + kind + "/" + term.Slug + "/",
                ["count"] = term.Pages.Count
            });
        }

        var fields = new Dictionary<string, ContentField>(StringComparer.OrdinalIgnoreCase)
        {
            ["terms"] = new ContentField("list", termsValue)
        };

        var item = new ContentItem(
            Id: $"{kind}-index",
            Title: title,
            Slug: kind,
            PublishAt: publishAt,
            ContentHtml: html,
            Meta: meta,
            Fields: fields);

        return (item, route, publishAt);
    }

    private static (ContentItem Item, RouteInfo Route, DateTimeOffset LastModified) CreateTermPage(
        string baseUrlPrefix,
        string kind,
        string singularTitlePrefix,
        TaxonomyTerm term,
        string template,
        DateTimeOffset publishAt,
        bool emitContentHtml,
        int pageSize,
        int page,
        int totalPages,
        IReadOnlyList<TaxonomyPage> items)
    {
        var html = string.Empty;
        if (emitContentHtml)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<ul>");

            foreach (var pageItem in items)
            {
                var href = $"{baseUrlPrefix}{pageItem.Url}";
                sb.AppendLine($"  <li><a href=\"{EscapeAttr(href)}\">{EscapeHtml(pageItem.Title)}</a></li>");
            }

            sb.AppendLine("</ul>");
            html = sb.ToString();
        }

        var isFirstPage = page <= 1;
        var url = isFirstPage
            ? "/" + kind + "/" + term.Slug + "/"
            : "/" + kind + "/" + term.Slug + "/page/" + page + "/";
        var outputPath = isFirstPage
            ? Path.Combine(kind, term.Slug, "index.html")
            : Path.Combine(kind, term.Slug, "page", page.ToString(), "index.html");
        var route = new RouteInfo(url, outputPath, template);
        var meta = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["type"] = "page"
        };

        var itemsValue = new List<object>(items.Count);
        foreach (var pageItem in items)
        {
            var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = pageItem.Title,
                ["url"] = pageItem.Url,
                ["publish_date"] = pageItem.PublishAt.DateTime
            };
            if (!string.IsNullOrWhiteSpace(pageItem.Summary))
            {
                obj["summary"] = pageItem.Summary!;
            }

            if (pageItem.Extra is not null)
            {
                foreach (var kv in pageItem.Extra)
                {
                    if (!obj.ContainsKey(kv.Key))
                    {
                        obj[kv.Key] = kv.Value;
                    }
                }
            }

            itemsValue.Add(obj);
        }

        var taxonomyValue = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = kind,
            ["term"] = term.DisplayName,
            ["slug"] = term.Slug,
            ["count"] = term.Pages.Count
        };

        var paginationValue = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["page"] = page,
            ["page_size"] = pageSize,
            ["total"] = term.Pages.Count,
            ["total_pages"] = totalPages,
            ["has_prev"] = page > 1,
            ["has_next"] = page < totalPages
        };

        var fields = new Dictionary<string, ContentField>(StringComparer.OrdinalIgnoreCase)
        {
            ["items"] = new ContentField("list", itemsValue),
            ["taxonomy"] = new ContentField("object", taxonomyValue),
            ["pagination"] = new ContentField("object", paginationValue)
        };

        var item = new ContentItem(
            Id: page <= 1 ? $"{kind}-{term.Slug}" : $"{kind}-{term.Slug}-page-{page}",
            Title: page <= 1 ? $"{singularTitlePrefix}: {term.DisplayName}" : $"{singularTitlePrefix}: {term.DisplayName} (Page {page})",
            Slug: term.Slug,
            PublishAt: publishAt,
            ContentHtml: html,
            Meta: meta,
            Fields: fields);

        return (item, route, publishAt);
    }

    private static (string IndexTemplate, string TermTemplate) ResolveTemplates(TaxonomyConfig config, string kind, TaxonomyKindConfig? kindConfig = null)
    {
        var legacyKindConfig = kind.Equals("tags", StringComparison.OrdinalIgnoreCase)
            ? config.Templates.Tags
            : (kind.Equals("categories", StringComparison.OrdinalIgnoreCase) ? config.Templates.Categories : new TaxonomyKindTemplateConfig());

        var baseTemplate = string.IsNullOrWhiteSpace(config.Template) ? "pages/page.html" : config.Template;
        var kindBaseTemplate = FirstNonEmpty(kindConfig?.Template, legacyKindConfig.Template, baseTemplate) ?? "pages/page.html";

        var indexTemplate = FirstNonEmpty(kindConfig?.IndexTemplate, legacyKindConfig.IndexTemplate, config.IndexTemplate, kindBaseTemplate)
            ?? kindBaseTemplate;

        var termTemplate = FirstNonEmpty(kindConfig?.TermTemplate, legacyKindConfig.TermTemplate, config.TermTemplate, kindBaseTemplate)
            ?? kindBaseTemplate;

        return (indexTemplate, termTemplate);
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c))
            {
                return c!.Trim();
            }
        }

        return null;
    }

    private static string NormalizeOutputMode(string? mode)
    {
        var m = (mode ?? "both").Trim().ToLowerInvariant();
        return m switch
        {
            "both" or "pages" or "data" or "fields_only" => m,
            "fields-only" => "fields_only",
            _ => "both"
        };
    }

    private static int NormalizePageSize(int pageSize)
    {
        return pageSize <= 0 ? 10 : pageSize;
    }

    private static IReadOnlyList<string> NormalizeItemFields(IReadOnlyList<string>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var f in fields)
        {
            var key = (f ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (seen.Add(key))
            {
                list.Add(key);
            }
        }

        return list;
    }

    private static IReadOnlyDictionary<string, object>? ExtractExtraFields(ContentItem item, IReadOnlyList<string> itemFields)
    {
        if (itemFields.Count == 0)
        {
            return null;
        }

        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in itemFields)
        {
            if (TryGetItemValue(item, key, out var value))
            {
                dict[key] = value!;
                continue;
            }

            if (key.Equals("date", StringComparison.OrdinalIgnoreCase))
            {
                dict["date"] = item.PublishAt.UtcDateTime.ToString("yyyy-MM-dd");
            }
        }

        return dict.Count == 0 ? null : dict;
    }

    private static bool TryGetItemValue(ContentItem item, string key, out object? value)
    {
        value = null;

        if (item.Meta.TryGetValue(key, out var metaValue) && metaValue is not null)
        {
            if (metaValue is string s)
            {
                var trimmed = s.Trim();
                if (trimmed.Length == 0)
                {
                    return false;
                }

                value = trimmed;
                return true;
            }

            value = metaValue;
            return true;
        }

        if (item.Fields is not null && item.Fields.TryGetValue(key, out var field) && field.Value is not null)
        {
            value = field.Value;
            return true;
        }

        return false;
    }

    private static void WriteExtraJson(Utf8JsonWriter writer, IReadOnlyDictionary<string, object>? extra)
    {
        if (extra is null || extra.Count == 0)
        {
            return;
        }

        foreach (var kv in extra)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value is null)
            {
                continue;
            }

            if (kv.Key.Equals("title", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Equals("url", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Equals("publishAt", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Equals("publish_date", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Equals("summary", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            WriteJsonValue(writer, kv.Key, kv.Value);
        }
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, string name, object value)
    {
        switch (value)
        {
            case string s:
                writer.WriteString(name, s);
                return;
            case bool b:
                writer.WriteBoolean(name, b);
                return;
            case int i:
                writer.WriteNumber(name, i);
                return;
            case long l:
                writer.WriteNumber(name, l);
                return;
            case double d:
                writer.WriteNumber(name, d);
                return;
            case decimal m:
                writer.WriteNumber(name, m);
                return;
            case DateTime dt:
                writer.WriteString(name, dt.ToString("O"));
                return;
            case DateTimeOffset dto:
                writer.WriteString(name, dto.ToString("O"));
                return;
            case IEnumerable<object> seq:
                writer.WriteStartArray(name);
                foreach (var x in seq)
                {
                    if (x is null)
                    {
                        continue;
                    }

                    writer.WriteStringValue(x.ToString());
                }
                writer.WriteEndArray();
                return;
            default:
                writer.WriteString(name, value.ToString());
                return;
        }
    }

    private static void SetTaxonomyData(BuildContext context, IReadOnlyList<string> itemFields)
    {
        var taxonomy = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (context.Config.Taxonomy.Kinds is { Count: > 0 } kinds)
        {
            foreach (var kindConfig in kinds)
            {
                var key = (kindConfig.Key ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var kind = string.IsNullOrWhiteSpace(kindConfig.Kind) ? key : kindConfig.Kind.Trim();
                var terms = BuildIndex(context.Routed, key, itemFields);
                if (terms.Count == 0)
                {
                    continue;
                }

                var title = string.IsNullOrWhiteSpace(kindConfig.Title) ? kind : kindConfig.Title.Trim();
                taxonomy[kind] = BuildKindData(key, kind, title, terms);
            }
        }
        else
        {
            var tags = BuildIndex(context.Routed, "tags", itemFields);
            if (tags.Count > 0)
            {
                taxonomy["tags"] = BuildKindData(key: "tags", kind: "tags", title: "Tags", tags);
            }

            var categories = BuildIndex(context.Routed, "categories", itemFields);
            if (categories.Count > 0)
            {
                taxonomy["categories"] = BuildKindData(key: "categories", kind: "categories", title: "Categories", categories);
            }
        }

        if (taxonomy.Count > 0)
        {
            context.Data["taxonomy"] = taxonomy;
        }
    }

    private static IReadOnlyDictionary<string, object> BuildKindData(
        string key,
        string kind,
        string title,
        Dictionary<string, TaxonomyTerm> terms)
    {
        var termsValue = new List<object>();
        var itemsByTerm = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            termsValue.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = term.DisplayName,
                ["slug"] = term.Slug,
                ["url"] = "/" + kind + "/" + term.Slug + "/",
                ["count"] = term.Pages.Count
            });

            var itemsValue = new List<object>(term.Pages.Count);
            foreach (var page in term.Pages)
            {
                var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = page.Title,
                    ["url"] = page.Url,
                    ["publish_date"] = page.PublishAt.DateTime
                };
                if (!string.IsNullOrWhiteSpace(page.Summary))
                {
                    obj["summary"] = page.Summary!;
                }

            if (page.Extra is not null)
            {
                foreach (var kv in page.Extra)
                {
                    if (!obj.ContainsKey(kv.Key))
                    {
                        obj[kv.Key] = kv.Value;
                    }
                }
            }

                itemsValue.Add(obj);
            }

            itemsByTerm[term.Slug] = itemsValue;
        }

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["key"] = key,
            ["kind"] = kind,
            ["title"] = title,
            ["terms"] = termsValue,
            ["items_by_term"] = itemsByTerm
        };
    }

    private static void WriteKind(
        Utf8JsonWriter writer,
        string baseUrl,
        string key,
        string kind,
        string title,
        Dictionary<string, TaxonomyTerm> terms)
    {
        writer.WriteStartObject();
        writer.WriteString("key", key);
        writer.WriteString("kind", kind);
        writer.WriteString("title", title);

        writer.WriteStartArray("terms");
        foreach (var term in terms.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteStartObject();
            writer.WriteString("title", term.DisplayName);
            writer.WriteString("slug", term.Slug);
            writer.WriteString("url", NormalizeUrl(baseUrl, "/" + kind + "/" + term.Slug + "/"));
            writer.WriteNumber("count", term.Pages.Count);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartObject("itemsByTerm");
        foreach (var term in terms.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteStartArray(term.Slug);
            foreach (var page in term.Pages)
            {
                writer.WriteStartObject();
                writer.WriteString("title", page.Title);
                writer.WriteString("url", NormalizeUrl(baseUrl, page.Url));
                writer.WriteString("publishAt", page.PublishAt.ToString("O"));
                if (!string.IsNullOrWhiteSpace(page.Summary))
                {
                    writer.WriteString("summary", page.Summary);
                }
                WriteExtraJson(writer, page.Extra);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static string NormalizeUrl(string baseUrl, string url)
    {
        var u = url.StartsWith('/') ? url : "/" + url;
        if (string.IsNullOrWhiteSpace(baseUrl) || baseUrl == "/")
        {
            return u;
        }

        var b = baseUrl.StartsWith('/') ? baseUrl : "/" + baseUrl;
        if (b.Length > 1 && b.EndsWith('/'))
        {
            b = b.TrimEnd('/');
        }

        return b + u;
    }

    private static IReadOnlyList<string>? GetStringList(IReadOnlyDictionary<string, object> meta, string key)
    {
        if (!meta.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }

        if (v is string s)
        {
            var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? null : parts;
        }

        if (v is IEnumerable<object> seq)
        {
            var list = seq.Select(x => x?.ToString() ?? string.Empty)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            return list.Count == 0 ? null : list;
        }

        return null;
    }

    private static string Slugify(string text)
    {
        var trimmed = text.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(trimmed.Length);
        var dash = false;

        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                dash = false;
                continue;
            }

            if (ch is ' ' or '-' or '_' or '.')
            {
                if (!dash && sb.Length > 0)
                {
                    sb.Append('-');
                    dash = true;
                }
            }
        }

        var s = sb.ToString().Trim('-');
        return s;
    }

    private static string EscapeHtml(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
    }

    private static string EscapeAttr(string value)
    {
        return EscapeHtml(value);
    }

    private sealed class TaxonomyTerm
    {
        public TaxonomyTerm(string displayName, string slug)
        {
            DisplayName = displayName;
            Slug = slug;
        }

        public string DisplayName { get; }
        public string Slug { get; }
        public List<TaxonomyPage> Pages { get; } = new();
    }

    private sealed record TaxonomyPage(string Title, string Url, DateTimeOffset PublishAt, string? Summary, IReadOnlyDictionary<string, object>? Extra);
}
