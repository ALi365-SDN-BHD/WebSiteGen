using System.Text;
using SiteGen.Content;
using SiteGen.Routing;

namespace SiteGen.Engine.Plugins.BuiltIn;

public sealed class ArchivePlugin : ISiteGenPlugin, IDerivePagesPlugin
{
    public string Name => "archive";
    public string Version => "2.0.0";

    public IReadOnlyList<(ContentItem Item, RouteInfo Route, DateTimeOffset LastModified)> DerivePages(BuildContext context)
    {
        var posts = context.Routed
            .Where(x => x.Route.Url.StartsWith("/blog/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Item.PublishAt)
            .ToList();

        if (posts.Count == 0)
        {
            return Array.Empty<(ContentItem, RouteInfo, DateTimeOffset)>();
        }

        var prefix = context.BaseUrl == "/" ? string.Empty : context.BaseUrl;

        var byYear = posts
            .GroupBy(x => x.Item.PublishAt.Year)
            .OrderByDescending(g => g.Key)
            .ToList();

        var derived = new List<(ContentItem Item, RouteInfo Route, DateTimeOffset LastModified)>();

        derived.Add(CreateArchiveIndex(prefix, byYear));

        foreach (var yearGroup in byYear)
        {
            var yearPosts = yearGroup.ToList();
            derived.Add(CreateYearPage(prefix, yearGroup.Key, yearPosts));

            var byMonth = yearPosts
                .GroupBy(x => x.Item.PublishAt.Month)
                .OrderByDescending(g => g.Key)
                .ToList();

            foreach (var monthGroup in byMonth)
            {
                derived.Add(CreateMonthPage(prefix, yearGroup.Key, monthGroup.Key, monthGroup.ToList()));
            }
        }

        return derived;
    }

    private static (ContentItem Item, RouteInfo Route, DateTimeOffset LastModified) CreateArchiveIndex(
        string baseUrlPrefix,
        IReadOnlyList<IGrouping<int, (ContentItem Item, RouteInfo Route)>> byYear)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<ul>");
        foreach (var g in byYear)
        {
            var href = $"{baseUrlPrefix}/blog/archive/{g.Key}/";
            sb.AppendLine($"  <li><a href=\"{EscapeAttr(href)}\">{g.Key}</a></li>");
        }
        sb.AppendLine("</ul>");

        var now = DateTimeOffset.UtcNow;
        var route = new RouteInfo("/blog/archive/", Path.Combine("blog", "archive", "index.html"), "pages/page.html");
        var meta = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["type"] = "page" };
        var item = new ContentItem("blog-archive-index", "Archive", "archive", now, sb.ToString(), meta);
        return (item, route, now);
    }

    private static (ContentItem Item, RouteInfo Route, DateTimeOffset LastModified) CreateYearPage(
        string baseUrlPrefix,
        int year,
        IReadOnlyList<(ContentItem Item, RouteInfo Route)> yearPosts)
    {
        var byMonth = yearPosts
            .GroupBy(x => x.Item.PublishAt.Month)
            .OrderByDescending(g => g.Key)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<ul>");
        foreach (var g in byMonth)
        {
            var href = $"{baseUrlPrefix}/blog/archive/{year}/{g.Key:D2}/";
            sb.AppendLine($"  <li><a href=\"{EscapeAttr(href)}\">{year}-{g.Key:D2}</a> <small>({g.Count()})</small></li>");
        }
        sb.AppendLine("</ul>");

        var publishAt = yearPosts.OrderByDescending(x => x.Item.PublishAt).First().Item.PublishAt;
        var url = $"/blog/archive/{year}/";
        var outputPath = Path.Combine("blog", "archive", year.ToString(), "index.html");
        var route = new RouteInfo(url, outputPath, "pages/page.html");
        var meta = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["type"] = "page" };
        var item = new ContentItem($"blog-archive-{year}", $"Archive: {year}", $"archive-{year}", publishAt, sb.ToString(), meta);
        return (item, route, publishAt);
    }

    private static (ContentItem Item, RouteInfo Route, DateTimeOffset LastModified) CreateMonthPage(
        string baseUrlPrefix,
        int year,
        int month,
        IReadOnlyList<(ContentItem Item, RouteInfo Route)> monthPosts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<ul>");
        foreach (var (item, route) in monthPosts.OrderByDescending(x => x.Item.PublishAt))
        {
            var href = $"{baseUrlPrefix}{route.Url}";
            sb.AppendLine($"  <li><a href=\"{EscapeAttr(href)}\">{EscapeHtml(item.Title)}</a></li>");
        }
        sb.AppendLine("</ul>");

        var publishAt = monthPosts.OrderByDescending(x => x.Item.PublishAt).First().Item.PublishAt;
        var url = $"/blog/archive/{year}/{month:D2}/";
        var outputPath = Path.Combine("blog", "archive", year.ToString(), month.ToString("D2"), "index.html");
        var routeInfo = new RouteInfo(url, outputPath, "pages/page.html");
        var meta = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["type"] = "page" };
        var itemInfo = new ContentItem($"blog-archive-{year}-{month:D2}", $"Archive: {year}-{month:D2}", $"archive-{year}-{month:D2}", publishAt, sb.ToString(), meta);
        return (itemInfo, routeInfo, publishAt);
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
