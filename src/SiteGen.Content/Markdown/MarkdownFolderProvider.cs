using SiteGen.Shared;
using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace SiteGen.Content.Markdown;

public sealed record MarkdownFolderProviderOptions(
    string ContentDir,
    string DefaultType = "page"
);

public sealed class MarkdownFolderProvider : IContentProvider
{
    private readonly MarkdownFolderProviderOptions _options;

    public MarkdownFolderProvider(MarkdownFolderProviderOptions options)
    {
        _options = options;
    }

    public Task<IReadOnlyList<ContentItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ContentDir))
        {
            throw new ContentException("ContentDir is required.");
        }

        if (!Directory.Exists(_options.ContentDir))
        {
            throw new ContentException($"ContentDir not found: {_options.ContentDir}");
        }

        var files = Directory.GetFiles(_options.ContentDir, "*.md", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = new List<ContentItem>(capacity: files.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var markdown = File.ReadAllText(file);
            var slug = Path.GetFileNameWithoutExtension(file);

            var meta = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = _options.DefaultType,
                ["source"] = "markdown",
                ["sourcePath"] = file
            };

            var bodyMarkdown = markdown;
            if (TryExtractFrontMatter(markdown, out var frontMatterYaml, out var body))
            {
                bodyMarkdown = body;
                var fm = ParseFrontMatter(frontMatterYaml);
                foreach (var kv in fm)
                {
                    meta[kv.Key] = kv.Value;
                }
            }

            if (meta.TryGetValue("slug", out var slugObj) && slugObj is string slugText && !string.IsNullOrWhiteSpace(slugText))
            {
                slug = slugText.Trim();
            }

            var title = meta.TryGetValue("title", out var titleObj) && titleObj is string titleText && !string.IsNullOrWhiteSpace(titleText)
                ? titleText.Trim()
                : ExtractTitle(bodyMarkdown) ?? slug;

            var html = BasicMarkdownToHtml.Convert(bodyMarkdown);

            var publishAt = File.GetLastWriteTimeUtc(file);
            if (meta.TryGetValue("publishAt", out var publishObj) && publishObj is string publishText && TryParseDateTimeOffset(publishText, out var dto))
            {
                publishAt = dto.UtcDateTime;
            }

            var fields = BuildFields(meta);

            items.Add(new ContentItem(
                Id: slug,
                Title: title,
                Slug: slug,
                PublishAt: publishAt,
                ContentHtml: html,
                Meta: meta,
                Fields: fields
            ));
        }

        return Task.FromResult<IReadOnlyList<ContentItem>>(items);
    }

    private static string? ExtractTitle(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# "))
            {
                return trimmed[2..].Trim();
            }
        }

        return null;
    }

    private static bool TryExtractFrontMatter(string markdown, out string frontMatterYaml, out string bodyMarkdown)
    {
        frontMatterYaml = string.Empty;
        bodyMarkdown = markdown;

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return false;
        }

        var normalized = markdown.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal) && !string.Equals(normalized.TrimStart(), "---", StringComparison.Ordinal))
        {
            return false;
        }

        var lines = normalized.Split('\n');
        if (lines.Length < 3 || lines[0].Trim() != "---")
        {
            return false;
        }

        var end = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                end = i;
                break;
            }
        }

        if (end <= 0)
        {
            return false;
        }

        frontMatterYaml = string.Join("\n", lines.Skip(1).Take(end - 1));
        bodyMarkdown = string.Join("\n", lines.Skip(end + 1));
        return true;
    }

    private static IReadOnlyDictionary<string, object> ParseFrontMatter(string yaml)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return dict;
        }

        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count == 0)
            {
                return dict;
            }

            if (stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return dict;
            }

            foreach (var kv in root.Children)
            {
                if (kv.Key is not YamlScalarNode k || string.IsNullOrWhiteSpace(k.Value))
                {
                    continue;
                }

                var key = k.Value.Trim();
                dict[key] = ToObject(kv.Value);
            }

            NormalizeTaxonomy(dict, "tags");
            NormalizeTaxonomy(dict, "categories");

            return dict;
        }
        catch
        {
            return dict;
        }
    }

    private static void NormalizeTaxonomy(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null)
        {
            return;
        }

        if (v is string s)
        {
            var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            dict[key] = parts.ToList();
            return;
        }

        if (v is IEnumerable<object> seq)
        {
            dict[key] = seq.Select(x => x?.ToString() ?? string.Empty)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }
    }

    private static IReadOnlyDictionary<string, ContentField> BuildFields(IReadOnlyDictionary<string, object> meta)
    {
        var fields = new Dictionary<string, ContentField>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in meta)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value is null)
            {
                continue;
            }

            var key = kv.Key.Trim();
            if (IsReservedMetaKey(key))
            {
                continue;
            }

            if (TryConvertToField(kv.Value, out var field))
            {
                fields[key] = field;
            }
        }

        if (meta.TryGetValue("tags", out var tagsObj) && tagsObj is not null && TryConvertToList(tagsObj, out var tags))
        {
            fields["tags"] = new ContentField("list", tags);
        }

        if (meta.TryGetValue("categories", out var catsObj) && catsObj is not null && TryConvertToList(catsObj, out var cats))
        {
            fields["categories"] = new ContentField("list", cats);
        }

        if (meta.TryGetValue("summary", out var summaryObj) && summaryObj is not null)
        {
            fields["summary"] = new ContentField("text", summaryObj.ToString() ?? string.Empty);
        }

        return fields;
    }

    private static bool IsReservedMetaKey(string key)
    {
        return key.Equals("title", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("slug", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("type", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("publishAt", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("language", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("tags", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("categories", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("summary", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("route", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("url", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("outputPath", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("template", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryConvertToField(object value, out ContentField field)
    {
        if (TryConvertToList(value, out var list))
        {
            field = new ContentField("list", list);
            return true;
        }

        if (value is bool b)
        {
            field = new ContentField("bool", b);
            return true;
        }

        if (value is int or long or float or double or decimal)
        {
            field = new ContentField("number", value);
            return true;
        }

        if (value is DateTime dt)
        {
            field = new ContentField("date", dt);
            return true;
        }

        if (value is DateTimeOffset dto)
        {
            field = new ContentField("date", dto);
            return true;
        }

        var text = value.ToString() ?? string.Empty;
        if (TryParseDateTimeOffset(text, out var parsed))
        {
            field = new ContentField("date", parsed);
            return true;
        }

        if (bool.TryParse(text, out var parsedBool))
        {
            field = new ContentField("bool", parsedBool);
            return true;
        }

        if (long.TryParse(text, out var parsedLong))
        {
            field = new ContentField("number", parsedLong);
            return true;
        }

        if (double.TryParse(text, out var parsedDouble))
        {
            field = new ContentField("number", parsedDouble);
            return true;
        }

        field = new ContentField("text", text);
        return true;
    }

    private static bool TryConvertToList(object value, out IReadOnlyList<string> list)
    {
        if (value is IEnumerable<object> seq)
        {
            var items = seq.Select(x => x?.ToString() ?? string.Empty)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            list = items;
            return items.Count > 0;
        }

        list = Array.Empty<string>();
        return false;
    }

    private static object ToObject(YamlNode node)
    {
        return node switch
        {
            YamlScalarNode s => s.Value ?? string.Empty,
            YamlSequenceNode seq => seq.Children.Select(ToObject).ToList(),
            YamlMappingNode map => map.Children
                .Where(p => p.Key is YamlScalarNode ks && !string.IsNullOrWhiteSpace(ks.Value))
                .ToDictionary(
                    p => ((YamlScalarNode)p.Key).Value!,
                    p => ToObject(p.Value),
                    StringComparer.OrdinalIgnoreCase),
            _ => node.ToString()
        };
    }

    private static bool TryParseDateTimeOffset(string text, out DateTimeOffset value)
    {
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out value);
    }
}
