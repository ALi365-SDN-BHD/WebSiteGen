using System.Net.Http.Headers;
using System.Text.Json;
using Scriban;
using SiteGen.Config;
using SiteGen.Engine.Plugins;
using SiteGen.Shared;

namespace SiteGen.Cli.Commands;

public static class DoctorCommand
{
    public static async Task<int> RunAsync(ArgReader reader)
    {
        var resolved = ConfigPathResolver.Resolve(reader);
        var rootDir = resolved.RootDir;
        var config = ConfigLoader.Load(resolved.FullConfigPath);

        var siteUrl = reader.GetOption("--site-url");
        if (!string.IsNullOrWhiteSpace(siteUrl))
        {
            config = config with { Site = config.Site with { Url = siteUrl } };
        }

        try
        {
            ConfigValidator.Validate(config);
            Console.WriteLine("✔ Config loaded");
        }
        catch (ConfigException ex)
        {
            Console.WriteLine("✖ Config error");
            Console.WriteLine(ex.Message);
            return 1;
        }

        var (layoutsDir, assetsDir, staticDir) = ResolveThemeDirectories(rootDir, config.Theme);
        if (!Directory.Exists(layoutsDir))
        {
            Console.WriteLine($"✖ Layouts dir not found: {layoutsDir}");
            return 1;
        }

        var requiredTemplates = new[]
        {
            Path.Combine(layoutsDir, "layouts", "base.html"),
            Path.Combine(layoutsDir, "pages", "page.html"),
            Path.Combine(layoutsDir, "pages", "post.html"),
            Path.Combine(layoutsDir, "pages", "index.html"),
            Path.Combine(layoutsDir, "pages", "list.html")
        };

        var missing = requiredTemplates.Where(p => !File.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            Console.WriteLine("✖ Missing templates:");
            foreach (var p in missing)
            {
                Console.WriteLine($"  - {p}");
            }

            return 1;
        }

        Console.WriteLine("✔ Templates present");

        foreach (var p in requiredTemplates)
        {
            var text = await File.ReadAllTextAsync(p);
            var template = Template.Parse(text, p);
            if (template.HasErrors)
            {
                Console.WriteLine($"✖ Template parse error: {p}");
                foreach (var m in template.Messages)
                {
                    Console.WriteLine($"  - {m}");
                }
                return 1;
            }
        }

        Console.WriteLine("✔ Templates parse");

        if (!Directory.Exists(assetsDir))
        {
            Console.WriteLine($"⚠ Assets dir not found: {assetsDir}");
        }
        else
        {
            Console.WriteLine("✔ Assets dir present");
        }

        if (!Directory.Exists(staticDir))
        {
            Console.WriteLine($"⚠ Static dir not found: {staticDir}");
        }
        else
        {
            Console.WriteLine("✔ Static dir present");
        }

        var cacheDir = Path.Combine(rootDir, ".cache");
        if (Directory.Exists(cacheDir))
        {
            var manifests = Directory.GetFiles(cacheDir, "build-manifest*.json");
            foreach (var m in manifests)
            {
                try
                {
                    using var stream = File.OpenRead(m);
                    _ = await JsonDocument.ParseAsync(stream);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠ Invalid manifest json: {m} ({ex.Message})");
                }
            }
        }

        var pluginContext = new BuildContext
        {
            Config = config,
            RootDir = rootDir,
            OutputDir = Path.Combine(rootDir, config.Build.Output),
            BaseUrl = config.Site.BaseUrl,
            LayoutsDir = layoutsDir,
            Routed = Array.Empty<(SiteGen.Content.ContentItem Item, SiteGen.Routing.RouteInfo Route)>(),
            Logger = new ConsoleLogger(LogLevel.Info)
        };

        var plugins = PluginRegistry.GetAllPlugins(pluginContext).Select(x => x.Plugin).ToList();
        Console.WriteLine($"✔ Plugins discovered: {plugins.Count}");

        if (config.Content.Provider.Equals("notion", StringComparison.OrdinalIgnoreCase) && config.Content.Notion is not null)
        {
            var token = Environment.GetEnvironmentVariable("NOTION_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("✖ NOTION_TOKEN not set");
                return 1;
            }

            var ok = await CheckNotionAsync(token, config.Content.Notion.DatabaseId);
            if (!ok)
            {
                return 1;
            }
        }

        Console.WriteLine("✔ Doctor passed");
        return 0;
    }

    private static (string LayoutsDir, string AssetsDir, string StaticDir) ResolveThemeDirectories(string rootDir, ThemeConfig theme)
    {
        var themeRoot = rootDir;
        if (!string.IsNullOrWhiteSpace(theme.Name))
        {
            themeRoot = Path.Combine(rootDir, "themes", theme.Name);
        }

        var layoutsDir = Path.GetFullPath(Path.Combine(themeRoot, theme.Layouts));
        var assetsDir = Path.GetFullPath(Path.Combine(themeRoot, theme.Assets));
        var staticDir = Path.GetFullPath(Path.Combine(themeRoot, theme.Static));
        return (layoutsDir, assetsDir, staticDir);
    }

    private static async Task<bool> CheckNotionAsync(string token, string databaseId)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");

        var url = $"https://api.notion.com/v1/databases/{databaseId}";
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✖ Notion request failed: {ex.Message}");
            return false;
        }

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine("✔ Notion database reachable");
            return true;
        }

        Console.WriteLine($"✖ Notion database check failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        return false;
    }
}
