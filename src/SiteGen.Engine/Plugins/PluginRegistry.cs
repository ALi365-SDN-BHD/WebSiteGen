using System.Reflection;
using SiteGen.Engine.Plugins.Generated;
using SiteGen.Shared;

namespace SiteGen.Engine.Plugins;

public interface IPluginSource
{
    IEnumerable<ISiteGenPlugin> GetPlugins();
}

public sealed class BuiltInPluginSource : IPluginSource
{
    public IEnumerable<ISiteGenPlugin> GetPlugins()
    {
        yield return new BuiltIn.TaxonomyPlugin();
        yield return new BuiltIn.SitemapPlugin();
        yield return new BuiltIn.RssPlugin();
        yield return new BuiltIn.SearchIndexPlugin();
        yield return new BuiltIn.PaginationPlugin();
        yield return new BuiltIn.ArchivePlugin();
    }
}

public sealed class ExternalAssemblyPluginSource : IPluginSource
{
    private readonly string _pluginsDir;
    private readonly ILogger _logger;

    public ExternalAssemblyPluginSource(string rootDir, ILogger logger)
    {
        _pluginsDir = Path.Combine(rootDir, "plugins");
        _logger = logger;
    }

    public IEnumerable<ISiteGenPlugin> GetPlugins()
    {
        if (!Directory.Exists(_pluginsDir))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(_pluginsDir, "*.dll"))
        {
            Assembly? assembly;
            try
            {
                assembly = Assembly.LoadFrom(path);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to load plugin assembly '{path}': {ex.Message}");
                continue;
            }

            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type is null ||
                    type.IsAbstract ||
                    type.IsInterface ||
                    !typeof(ISiteGenPlugin).IsAssignableFrom(type))
                {
                    continue;
                }

                ISiteGenPlugin? instance = null;
                try
                {
                    instance = Activator.CreateInstance(type) as ISiteGenPlugin;
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Failed to create plugin '{type.FullName}' from '{path}': {ex.Message}");
                }

                if (instance is not null)
                {
                    yield return instance;
                }
            }
        }
    }

    private static IEnumerable<Type?> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types;
        }
    }
}

public static class PluginRegistry
{
    public static IEnumerable<(ISiteGenPlugin Plugin, string Source)> GetAllPlugins(BuildContext context)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

#if AOT
        var sources = new (IPluginSource Source, string Name)[]
        {
            (new BuiltInPluginSource(), "built-in"),
            (new GeneratedPluginSource(), "generated")
        };
#else
        var sources = new (IPluginSource Source, string Name)[]
        {
            (new BuiltInPluginSource(), "built-in"),
            (new GeneratedPluginSource(), "generated"),
            (new ExternalAssemblyPluginSource(context.RootDir, context.Logger), "external")
        };
#endif

        foreach (var (source, name) in sources)
        {
            foreach (var plugin in source.GetPlugins())
            {
                if (plugin is not null)
                {
                    var key = $"{plugin.Name}@{plugin.Version}";
                    if (seen.Add(key))
                    {
                        yield return (plugin, name);
                    }
                }
            }
        }
    }
}
