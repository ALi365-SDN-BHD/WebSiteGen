using System.Net;
using System.Text;
using System.Text.Json;

namespace SiteGen.Content.Notion;

public static class NotionRichTextRenderer
{
    public static string Render(JsonElement richTextArray)
    {
        if (richTextArray.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var item in richTextArray.EnumerateArray())
        {
            if (!item.TryGetProperty("plain_text", out var plainTextEl) || plainTextEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = WebUtility.HtmlEncode(plainTextEl.GetString() ?? string.Empty);
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var href = GetHref(item);
            var annotations = item.TryGetProperty("annotations", out var ann) ? ann : default;

            if (!string.IsNullOrWhiteSpace(href))
            {
                text = $"<a href=\"{WebUtility.HtmlEncode(href)}\">{text}</a>";
            }

            if (annotations.ValueKind == JsonValueKind.Object)
            {
                if (GetBool(annotations, "code"))
                {
                    text = $"<code>{text}</code>";
                }

                if (GetBool(annotations, "bold"))
                {
                    text = $"<strong>{text}</strong>";
                }

                if (GetBool(annotations, "italic"))
                {
                    text = $"<em>{text}</em>";
                }

                if (GetBool(annotations, "underline"))
                {
                    text = $"<u>{text}</u>";
                }

                if (GetBool(annotations, "strikethrough"))
                {
                    text = $"<s>{text}</s>";
                }
            }

            sb.Append(text);
        }

        return sb.ToString();
    }

    private static bool GetBool(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v))
        {
            return false;
        }

        return v.ValueKind == JsonValueKind.True;
    }

    private static string? GetHref(JsonElement richTextItem)
    {
        if (richTextItem.TryGetProperty("href", out var hrefEl) && hrefEl.ValueKind == JsonValueKind.String)
        {
            return hrefEl.GetString();
        }

        if (richTextItem.TryGetProperty("text", out var textEl) &&
            textEl.ValueKind == JsonValueKind.Object &&
            textEl.TryGetProperty("link", out var linkEl) &&
            linkEl.ValueKind == JsonValueKind.Object &&
            linkEl.TryGetProperty("url", out var urlEl) &&
            urlEl.ValueKind == JsonValueKind.String)
        {
            return urlEl.GetString();
        }

        return null;
    }
}

