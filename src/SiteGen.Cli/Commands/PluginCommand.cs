using SiteGen.Engine.Plugins;
using SiteGen.Config;
using SiteGen.Shared;

namespace SiteGen.Cli.Commands;

public static class PluginCommand
{
    public static Task<int> RunAsync(ArgReader reader)
    {
        var sub = reader.GetArg(1);
        if (string.IsNullOrWhiteSpace(sub) || sub is "help" or "--help" or "-h")
        {
            PrintHelp();
            return Task.FromResult(0);
        }

        return sub switch
        {
            "list" => ListAsync(),
            _ => Task.FromResult(Unknown(sub))
        };
    }

    private static Task<int> ListAsync()
    {
        var context = new BuildContext
        {
            Config = new AppConfig
            {
                Site = new SiteConfig
                {
                    Name = "dummy",
                    Title = "dummy",
                    BaseUrl = "/"
                },
                Content = new ContentConfig
                {
                    Provider = "markdown"
                },
                Build = new BuildConfig(),
                Theme = new ThemeConfig(),
                Logging = new LoggingConfig()
            },
            RootDir = Directory.GetCurrentDirectory(),
            OutputDir = "",
            BaseUrl = "/",
            LayoutsDir = "",
            Routed = new List<(SiteGen.Content.ContentItem Item, SiteGen.Routing.RouteInfo Route)>(),
            Logger = new ConsoleLogger(LogLevel.Info)
        };

        foreach (var (plugin, source) in PluginRegistry.GetAllPlugins(context))
        {
            var hooks = new List<string>(capacity: 2);
            if (plugin is IDerivePagesPlugin)
            {
                hooks.Add("derive-pages");
            }
            if (plugin is IAfterBuildPlugin)
            {
                hooks.Add("after-build");
            }

            var hooksText = hooks.Count == 0 ? "" : $" ({string.Join(", ", hooks)})";
            Console.WriteLine($"{plugin.Name}@{plugin.Version} [{source}]{hooksText}");
        }

        return Task.FromResult(0);
    }

    private static int Unknown(string sub)
    {
        Console.Error.WriteLine($"Unknown plugin subcommand: {sub}");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("sitegen plugin");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  sitegen plugin list");
    }
}

