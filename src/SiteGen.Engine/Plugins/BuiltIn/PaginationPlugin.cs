using System.Text;
using SiteGen.Content;
using SiteGen.Routing;

namespace SiteGen.Engine.Plugins.BuiltIn;

public sealed class PaginationPlugin : ISiteGenPlugin, IDerivePagesPlugin
{
    public string Name => "pagination";
    public string Version => "2.0.0";

    public IReadOnlyList<(ContentItem Item, RouteInfo Route, DateTimeOffset LastModified)> DerivePages(BuildContext context)
    {
        const int pageSize = 10;
        var posts = context.Routed
            .Where(x => x.Route.Url.StartsWith("/blog/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Item.PublishAt)
            .ToList();

        if (posts.Count <= pageSize)
        {
            return Array.Empty<(ContentItem, RouteInfo, DateTimeOffset)>();
        }

        var prefix = context.BaseUrl == "/" ? string.Empty : context.BaseUrl;
        var totalPages = (int)Math.Ceiling(posts.Count / (double)pageSize);

        var derived = new List<(ContentItem Item, RouteInfo Route, DateTimeOffset LastModified)>();
        for (var page = 2; page <= totalPages; page++)
        {
            var start = (page - 1) * pageSize;
            var slice = posts.Skip(start).Take(pageSize).ToList();
            if (slice.Count == 0)
            {
                continue;
            }

            var publishAt = slice[0].Item.PublishAt;
            var html = BuildPageHtml(prefix, slice, page, totalPages);
            var url = $"/blog/page/{page}/";
            var outputPath = Path.Combine("blog", "page", page.ToString(), "index.html");
            var route = new RouteInfo(url, outputPath, "pages/page.html");
            var meta = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = "page"
            };

            var item = new ContentItem(
                Id: $"blog-page-{page}",
                Title: $"Blog - Page {page}",
                Slug: $"page-{page}",
                PublishAt: publishAt,
                ContentHtml: html,
                Meta: meta);

            derived.Add((item, route, publishAt));
        }

        return derived;
    }

    private static string BuildPageHtml(
        string baseUrlPrefix,
        IReadOnlyList<(ContentItem Item, RouteInfo Route)> posts,
        int page,
        int totalPages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<ul>");
        foreach (var (item, route) in posts)
        {
            var href = $"{baseUrlPrefix}{route.Url}";
            sb.AppendLine($"  <li><a href=\"{EscapeAttr(href)}\">{EscapeHtml(item.Title)}</a></li>");
        }
        sb.AppendLine("</ul>");

        sb.AppendLine("<nav>");
        if (page > 1)
        {
            var prevHref = page == 2 ? $"{baseUrlPrefix}/blog/" : $"{baseUrlPrefix}/blog/page/{page - 1}/";
            sb.AppendLine($"  <a href=\"{EscapeAttr(prevHref)}\">Prev</a>");
        }

        if (page < totalPages)
        {
            var nextHref = $"{baseUrlPrefix}/blog/page/{page + 1}/";
            sb.AppendLine($"  <a href=\"{EscapeAttr(nextHref)}\">Next</a>");
        }
        sb.AppendLine("</nav>");

        return sb.ToString();
    }

    private static string EscapeHtml(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string EscapeAttr(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}

