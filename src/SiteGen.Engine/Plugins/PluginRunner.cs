using SiteGen.Content;
using SiteGen.Routing;
using System.Diagnostics;

namespace SiteGen.Engine.Plugins;

public static class PluginRunner
{
    public static IReadOnlyList<(ContentItem Item, RouteInfo Route, DateTimeOffset LastModified)> RunDerivePages(BuildContext context)
    {
        var derived = new List<(ContentItem Item, RouteInfo Route, DateTimeOffset LastModified)>();
        var warnOnPluginFailure = string.Equals(context.Config.Site.PluginFailMode, "warn", StringComparison.OrdinalIgnoreCase);

        foreach (var (plugin, _) in PluginRegistry.GetAllPlugins(context))
        {
            if (!IsPluginEnabled(context, plugin.Name))
            {
                continue;
            }

            if (plugin is IDerivePagesPlugin derive)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var pages = derive.DerivePages(context);
                    if (pages.Count > 0)
                    {
                        derived.AddRange(pages);
                    }

                    sw.Stop();
                    context.PluginExecutions.Add(new PluginExecutionInfo(plugin.Name, "derive-pages", sw.ElapsedMilliseconds, true, null));
                    context.Logger.Info($"plugin {plugin.Name} derive-pages {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    context.PluginExecutions.Add(new PluginExecutionInfo(plugin.Name, "derive-pages", sw.ElapsedMilliseconds, false, ex.Message));
                    context.Logger.Error($"plugin {plugin.Name} derive-pages failed: {ex.Message}");
                    if (!warnOnPluginFailure)
                    {
                        throw;
                    }
                }
            }
        }

        return derived;
    }

    public static void RunAfterBuild(BuildContext context)
    {
        var warnOnPluginFailure = string.Equals(context.Config.Site.PluginFailMode, "warn", StringComparison.OrdinalIgnoreCase);

        foreach (var (plugin, _) in PluginRegistry.GetAllPlugins(context))
        {
            if (!IsPluginEnabled(context, plugin.Name))
            {
                continue;
            }

            if (plugin is IAfterBuildPlugin after)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    after.AfterBuild(context);
                    sw.Stop();
                    context.PluginExecutions.Add(new PluginExecutionInfo(plugin.Name, "after-build", sw.ElapsedMilliseconds, true, null));
                    context.Logger.Info($"plugin {plugin.Name} after-build {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    context.PluginExecutions.Add(new PluginExecutionInfo(plugin.Name, "after-build", sw.ElapsedMilliseconds, false, ex.Message));
                    context.Logger.Error($"plugin {plugin.Name} after-build failed: {ex.Message}");
                    if (!warnOnPluginFailure)
                    {
                        throw;
                    }
                }
            }
        }
    }

    private static bool IsPluginEnabled(BuildContext context, string name)
    {
        if (context.Config.Site.Plugins is null || string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        if (context.Config.Site.Plugins.TryGetValue(name, out var cfg))
        {
            return cfg.Enabled;
        }

        return true;
    }
}
