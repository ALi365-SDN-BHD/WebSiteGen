using SiteGen.Routing;

namespace SiteGen.Engine.Plugins.BuiltIn;

public sealed class SitemapPlugin : ISiteGenPlugin, IAfterBuildPlugin
{
    public string Name => "sitemap";
    public string Version => "2.0.0";

    public void AfterBuild(BuildContext context)
    {
        if (context.Config.Site.Languages is { Count: > 0 } && context.Config.Site.SitemapMode.Equals("merged", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var siteUrl = context.Config.Site.Url;
        if (string.IsNullOrWhiteSpace(siteUrl))
        {
            return;
        }

        var metaRoutes = new List<(RouteInfo Route, DateTimeOffset LastModified)>(capacity: context.Routed.Count + 3)
        {
            (new RouteInfo("/", "index.html", "pages/index.html"), DateTimeOffset.UtcNow),
            (new RouteInfo("/blog/", Path.Combine("blog", "index.html"), "pages/list.html"), DateTimeOffset.UtcNow),
            (new RouteInfo("/pages/", Path.Combine("pages", "index.html"), "pages/list.html"), DateTimeOffset.UtcNow)
        };

        metaRoutes.AddRange(context.Routed.Select(x => (x.Route, x.Item.PublishAt)));
        if (context.DerivedRoutes.Count > 0)
        {
            metaRoutes.AddRange(context.DerivedRoutes);
        }
        SitemapGenerator.Generate(context.OutputDir, siteUrl, context.BaseUrl, metaRoutes);
    }
}
