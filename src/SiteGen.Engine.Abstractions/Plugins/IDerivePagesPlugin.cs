using SiteGen.Content;
using SiteGen.Routing;

namespace SiteGen.Engine.Plugins;

public interface IDerivePagesPlugin
{
    IReadOnlyList<(ContentItem Item, RouteInfo Route, DateTimeOffset LastModified)> DerivePages(BuildContext context);
}

