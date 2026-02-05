using System.Net;
using System.Text;
using System.Text.Json;
using SiteGen.Shared;

namespace SiteGen.Content.Notion;

public sealed class NotionBlocksRenderer
{
    private readonly NotionApiClient _client;

    public NotionBlocksRenderer(NotionApiClient client)
    {
        _client = client;
    }

    public async Task<string> RenderPageAsync(string pageId, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        await RenderChildrenAsync(pageId, sb, cancellationToken);
        return sb.ToString();
    }

    private async Task RenderChildrenAsync(string blockId, StringBuilder sb, CancellationToken cancellationToken)
    {
        string? startCursor = null;
        string? openList = null;

        while (true)
        {
            var url = $"https://api.notion.com/v1/blocks/{blockId}/children?page_size=100";
            if (!string.IsNullOrWhiteSpace(startCursor))
            {
                url += $"&start_cursor={WebUtility.UrlEncode(startCursor)}";
            }

            using var doc = await _client.GetAsync(url, cancellationToken);
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                throw new ContentException("Notion blocks response missing results.");
            }

            foreach (var block in results.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var type = GetString(block, "type");
                if (type is null)
                {
                    continue;
                }

                if (type is "bulleted_list_item" or "numbered_list_item")
                {
                    var listTag = type == "bulleted_list_item" ? "ul" : "ol";
                    if (openList is null)
                    {
                        sb.AppendLine($"<{listTag}>");
                        openList = listTag;
                    }
                    else if (!string.Equals(openList, listTag, StringComparison.Ordinal))
                    {
                        sb.AppendLine($"</{openList}>");
                        sb.AppendLine($"<{listTag}>");
                        openList = listTag;
                    }

                    sb.AppendLine(await RenderListItemAsync(block, type, cancellationToken));
                    continue;
                }

                if (openList is not null)
                {
                    sb.AppendLine($"</{openList}>");
                    openList = null;
                }

                var rendered = await RenderBlockAsync(block, type, cancellationToken);
                if (!string.IsNullOrWhiteSpace(rendered))
                {
                    sb.AppendLine(rendered);
                }
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

        if (openList is not null)
        {
            sb.AppendLine($"</{openList}>");
        }
    }

    private async Task<string?> RenderBlockAsync(JsonElement block, string type, CancellationToken cancellationToken)
    {
        return type switch
        {
            "paragraph" => RenderRichTextContainer(block, "paragraph", "p"),
            "heading_1" => RenderRichTextContainer(block, "heading_1", "h1"),
            "heading_2" => RenderRichTextContainer(block, "heading_2", "h2"),
            "heading_3" => RenderRichTextContainer(block, "heading_3", "h3"),
            "quote" => RenderRichTextContainer(block, "quote", "blockquote"),
            "code" => RenderCode(block),
            "divider" => "<hr />",
            "image" => RenderImage(block),
            _ => await RenderUnknownAsync(block, cancellationToken)
        };
    }

    private static string? RenderRichTextContainer(JsonElement block, string containerName, string tag)
    {
        if (!block.TryGetProperty(containerName, out var container) || container.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!container.TryGetProperty("rich_text", out var richText))
        {
            return null;
        }

        var inner = NotionRichTextRenderer.Render(richText);
        if (string.IsNullOrWhiteSpace(inner))
        {
            return null;
        }

        return $"<{tag}>{inner}</{tag}>";
    }

    private static string? RenderCode(JsonElement block)
    {
        if (!block.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var lang = GetString(code, "language") ?? string.Empty;
        var richText = code.TryGetProperty("rich_text", out var rt) ? rt : default;
        var raw = ExtractPlainText(richText);
        var encoded = WebUtility.HtmlEncode(raw);

        var classAttr = string.IsNullOrWhiteSpace(lang) ? string.Empty : $" class=\"language-{WebUtility.HtmlEncode(lang)}\"";
        return $"<pre><code{classAttr}>{encoded}</code></pre>";
    }

    private static string? RenderImage(JsonElement block)
    {
        if (!block.TryGetProperty("image", out var image) || image.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var imageType = GetString(image, "type");
        string? url = null;
        if (imageType == "external" &&
            image.TryGetProperty("external", out var ext) &&
            ext.ValueKind == JsonValueKind.Object)
        {
            url = GetString(ext, "url");
        }

        if (imageType == "file" &&
            image.TryGetProperty("file", out var file) &&
            file.ValueKind == JsonValueKind.Object)
        {
            url = GetString(file, "url");
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var captionText = image.TryGetProperty("caption", out var cap) ? NotionRichTextRenderer.Render(cap) : null;
        var img = $"<img src=\"{WebUtility.HtmlEncode(url)}\" alt=\"\" />";
        if (string.IsNullOrWhiteSpace(captionText))
        {
            return img;
        }

        return $"<figure>{img}<figcaption>{captionText}</figcaption></figure>";
    }

    private async Task<string?> RenderListItemAsync(JsonElement block, string type, CancellationToken cancellationToken)
    {
        if (!block.TryGetProperty(type, out var container) || container.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var richText = container.TryGetProperty("rich_text", out var rt) ? rt : default;
        var inner = NotionRichTextRenderer.Render(richText);

        var hasChildren = block.TryGetProperty("has_children", out var hc) && hc.ValueKind == JsonValueKind.True;
        if (!hasChildren)
        {
            return $"<li>{inner}</li>";
        }

        var id = GetString(block, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return $"<li>{inner}</li>";
        }

        var nested = new StringBuilder();
        await RenderChildrenAsync(id, nested, cancellationToken);
        return $"<li>{inner}{nested}</li>";
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

    private static Task<string?> RenderUnknownAsync(JsonElement block, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult<string?>(null);
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v))
        {
            return null;
        }

        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}

