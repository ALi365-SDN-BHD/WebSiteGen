using System.Text;
using System.Text.Json;
using SiteGen.Engine.Plugins;
using SiteGen.Engine.Incremental;
using SiteGen.Config;
using SiteGen.Content;
using SiteGen.Content.Markdown;
using SiteGen.Content.Notion;
using SiteGen.Routing;
using SiteGen.Rendering;
using SiteGen.Rendering.Scriban;
using SiteGen.Shared;

namespace SiteGen.Engine;

public sealed class SiteEngine
{
    private readonly ILogger _logger;

    public SiteEngine(ILogger logger)
    {
        _logger = logger;
    }

    public async Task BuildAsync(AppConfig config, string rootDir, ConfigOverrides overrides, CancellationToken cancellationToken = default)
    {
        var effectiveConfig = ConfigApplier.Apply(config, overrides);
        ConfigValidator.Validate(effectiveConfig);

        var outputDir = MakeAbsolute(rootDir, effectiveConfig.Build.Output);
        var (layoutsDir, assetsDir, staticDir) = ResolveThemeDirectories(rootDir, effectiveConfig.Theme);

        if (effectiveConfig.Build.Clean && Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
        }

        Directory.CreateDirectory(outputDir);
        _logger.Info($"event=build.start rootDir={rootDir} outputDir={outputDir}");

        var provider = CreateContentProvider(effectiveConfig, rootDir, overrides.IsCI);
        var items = await provider.LoadAsync(cancellationToken);

        _logger.Info($"event=content.loaded count={items.Count}");

        var languages = GetLanguages(effectiveConfig.Site);
        if (languages.Count == 0)
        {
            var baseUrl = NormalizeBaseUrl(effectiveConfig.Site.BaseUrl);
            _logger.Info($"event=build.variant.start language={effectiveConfig.Site.Language} baseUrl={baseUrl}");
            var result = await BuildVariantAsync(
                effectiveConfig,
                rootDir,
                overrides,
                items,
                outputDir,
                baseUrl,
                layoutsDir,
                assetsDir,
                staticDir,
                manifestSuffix: null,
                defaultLanguage: null,
                cancellationToken);

            _logger.Info($"event=build.variant.done language={effectiveConfig.Site.Language} baseUrl={baseUrl}");
            WriteMetricsIfRequested(rootDir, overrides.MetricsPath, effectiveConfig, outputDir, items.Count, new[] { result });
            return;
        }

        var defaultLanguage = GetDefaultLanguage(effectiveConfig.Site, languages);
        var rootBaseUrl = NormalizeBaseUrl(effectiveConfig.Site.BaseUrl);
        var results = new List<BuildVariantResult>(capacity: languages.Count);
        for (var i = 0; i < languages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lang = languages[i];
            var baseUrl = CombineBaseUrlWithLanguage(rootBaseUrl, lang);
            var variantConfig = effectiveConfig with
            {
                Site = effectiveConfig.Site with
                {
                    Language = lang,
                    BaseUrl = baseUrl
                }
            };

            var variantItems = FilterItemsByLanguage(items, lang, defaultLanguage);
            var variantOutputDir = Path.Combine(outputDir, lang);
            _logger.Info($"event=build.variant.start language={lang} baseUrl={baseUrl} outputDir={variantOutputDir}");
            var result = await BuildVariantAsync(
                variantConfig,
                rootDir,
                overrides,
                variantItems,
                variantOutputDir,
                baseUrl,
                layoutsDir,
                assetsDir,
                staticDir,
                manifestSuffix: lang,
                defaultLanguage: defaultLanguage,
                cancellationToken);
            results.Add(result);
            _logger.Info($"event=build.variant.done language={lang} baseUrl={baseUrl} outputDir={variantOutputDir}");
        }

        GenerateI18nRootOutputs(effectiveConfig, outputDir, rootBaseUrl, results);
        _logger.Info("event=build.done");
        WriteMetricsIfRequested(rootDir, overrides.MetricsPath, effectiveConfig, outputDir, items.Count, results);
    }

    private async Task<BuildVariantResult> BuildVariantAsync(
        AppConfig config,
        string rootDir,
        ConfigOverrides overrides,
        IReadOnlyList<ContentItem> items,
        string outputDir,
        string baseUrl,
        string layoutsDir,
        string assetsDir,
        string staticDir,
        string? manifestSuffix,
        string? defaultLanguage,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDir);

        if (Directory.Exists(staticDir))
        {
            DirectoryCopy.Copy(staticDir, outputDir);
        }

        var dataItems = items.Where(IsDataItem).ToList();
        var contentItems = items.Where(i => !IsDataItem(i)).ToList();
        var modules = BuildModules(dataItems, config.Site.Language);

        var renderer = new ScribanTemplateRenderer(layoutsDir);

        var routed = contentItems
            .Select(i => (Item: i, Route: RouteGenerator.Generate(i, config.Site.OutputPathEncoding)))
            .ToList();

        var pluginContext = new BuildContext
        {
            Config = config,
            RootDir = rootDir,
            OutputDir = outputDir,
            BaseUrl = baseUrl,
            LayoutsDir = layoutsDir,
            Routed = routed,
            Logger = _logger
        };

        var derived = PluginRunner.RunDerivePages(pluginContext);
        foreach (var (item, route, lastModified) in derived)
        {
            pluginContext.DerivedRouted.Add((item, route));
            pluginContext.DerivedRoutes.Add((route, lastModified));
        }

        var siteModel = new SiteModel
        {
            Name = config.Site.Name,
            Title = config.Site.Title,
            Url = config.Site.Url,
            Description = config.Site.Description,
            BaseUrl = baseUrl,
            Language = config.Site.Language,
            Params = config.Theme.Params,
            Modules = modules,
            Data = pluginContext.Data.Count == 0 ? null : pluginContext.Data
        };

        var renderQueue = routed.Concat(pluginContext.DerivedRouted).ToList();

        var incrementalEnabled = overrides.Incremental ?? true;
        var cacheDir = string.IsNullOrWhiteSpace(overrides.CacheDir)
            ? Path.Combine(rootDir, ".cache")
            : Path.GetFullPath(overrides.CacheDir!);

        var suffix = string.IsNullOrWhiteSpace(manifestSuffix) ? null : SanitizeFileSegment(manifestSuffix);
        var manifestPath = suffix is null
            ? Path.Combine(cacheDir, "build-manifest.json")
            : Path.Combine(cacheDir, $"build-manifest.{suffix}.json");

        var templateHash = incrementalEnabled ? HashUtil.Sha256HexForDirectory(layoutsDir) : string.Empty;
        var manifest = incrementalEnabled ? BuildManifest.Load(manifestPath) : new BuildManifest();
        manifest.TemplateHash = templateHash;

        var currentKeys = new HashSet<string>(StringComparer.Ordinal);
        var renderedCount = 0;
        var skippedCount = 0;
        var renderReasons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var warnedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (item, route) in renderQueue)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = NormalizeRelPath(route.OutputPath);
            WarnIfWindowsIncompatible(route.OutputPath, warnedOutputPaths);
            currentKeys.Add(key);

            var contentHash = ComputeContentHash(item);
            var routeHash = ComputeRouteHash(route);
            var outputPath = Path.Combine(outputDir, route.OutputPath);
            var outputExists = File.Exists(outputPath);
            BuildManifestEntry? existing = null;
            var hasExisting = incrementalEnabled && manifest.Entries.TryGetValue(key, out existing) && existing is not null;

            var canSkip = incrementalEnabled &&
                hasExisting &&
                outputExists &&
                existing!.TemplateHash == templateHash &&
                existing.ContentHash == contentHash &&
                existing.RouteHash == routeHash;

            if (canSkip)
            {
                skippedCount++;
                renderReasons["unchanged"] = renderReasons.GetValueOrDefault("unchanged") + 1;
                continue;
            }

            if (incrementalEnabled)
            {
                var reason = !hasExisting ? "new_page"
                    : !outputExists ? "output_missing"
                    : existing!.TemplateHash != templateHash ? "template_changed"
                    : existing.ContentHash != contentHash ? "content_changed"
                    : existing.RouteHash != routeHash ? "route_changed"
                    : "render";
                renderReasons[reason] = renderReasons.GetValueOrDefault(reason) + 1;
            }
            else
            {
                renderReasons["full_render"] = renderReasons.GetValueOrDefault("full_render") + 1;
            }

            var pageModel = new PageModel
            {
                Site = siteModel,
                Page = new PageInfo
                {
                    Title = item.Title,
                    Url = route.Url,
                    Content = item.ContentHtml,
                    Summary = item.Meta.TryGetValue("summary", out var summary) ? summary?.ToString() : null,
                    PublishDate = item.PublishAt,
                    Fields = item.Fields
                }
            };

            var html = renderer.RenderPage(route.Template, pageModel);
            FileWriter.WriteUtf8(outputDir, route.OutputPath, html);
            renderedCount++;

            if (incrementalEnabled)
            {
                manifest.Entries[key] = new BuildManifestEntry
                {
                    OutputPath = key,
                    Url = route.Url,
                    Template = route.Template,
                    ContentHash = contentHash,
                    RouteHash = routeHash,
                    TemplateHash = templateHash
                };
            }
        }

        if (incrementalEnabled)
        {
            var removed = manifest.Entries.Keys.Where(k => !currentKeys.Contains(k)).ToList();
            foreach (var k in removed)
            {
                manifest.Entries.Remove(k);
            }
        }

        var allPages = routed
            .Select(x => new PageInfo
            {
                Title = x.Item.Title,
                Url = x.Route.Url,
                Content = x.Item.ContentHtml,
                Summary = x.Item.Meta.TryGetValue("summary", out var summary) ? summary?.ToString() : null,
                PublishDate = x.Item.PublishAt,
                Fields = x.Item.Fields
            })
            .OrderByDescending(p => p.PublishDate)
            .ToList();

        var homeHtml = renderer.RenderList("pages/index.html", new ListPageModel
        {
            Site = siteModel,
            Pages = allPages
        });

        FileWriter.WriteUtf8(outputDir, "index.html", homeHtml);

        var blogPages = routed
            .Where(x => x.Route.Url.StartsWith("/blog/", StringComparison.OrdinalIgnoreCase))
            .Select(x => new PageInfo
            {
                Title = x.Item.Title,
                Url = x.Route.Url,
                Content = x.Item.ContentHtml,
                Summary = x.Item.Meta.TryGetValue("summary", out var summary) ? summary?.ToString() : null,
                PublishDate = x.Item.PublishAt,
                Fields = x.Item.Fields
            })
            .OrderByDescending(p => p.PublishDate)
            .ToList();

        var blogListHtml = renderer.RenderList("pages/list.html", new ListPageModel
        {
            Site = siteModel,
            Pages = blogPages
        });

        FileWriter.WriteUtf8(outputDir, Path.Combine("blog", "index.html"), blogListHtml);

        var pagePages = routed
            .Where(x => x.Route.Url.StartsWith("/pages/", StringComparison.OrdinalIgnoreCase))
            .Select(x => new PageInfo
            {
                Title = x.Item.Title,
                Url = x.Route.Url,
                Content = x.Item.ContentHtml,
                Summary = x.Item.Meta.TryGetValue("summary", out var summary) ? summary?.ToString() : null,
                PublishDate = x.Item.PublishAt,
                Fields = x.Item.Fields
            })
            .OrderByDescending(p => p.PublishDate)
            .ToList();

        var pagesListHtml = renderer.RenderList("pages/list.html", new ListPageModel
        {
            Site = siteModel,
            Pages = pagePages
        });

        FileWriter.WriteUtf8(outputDir, Path.Combine("pages", "index.html"), pagesListHtml);

        if (Directory.Exists(assetsDir))
        {
            DirectoryCopy.Copy(assetsDir, Path.Combine(outputDir, "assets"));
        }

        PluginRunner.RunAfterBuild(pluginContext);

        if (incrementalEnabled)
        {
            manifest.Save(manifestPath);
            _logger.Info($"Incremental build: rendered={renderedCount}, skipped={skippedCount}, cache={cacheDir}");
        }

        if (defaultLanguage is null)
        {
            _logger.Info($"Build completed: {Path.GetFullPath(outputDir)}");
        }
        else
        {
            _logger.Info($"Build completed: {Path.GetFullPath(outputDir)} (lang={config.Site.Language})");
        }

        return new BuildVariantResult(
            config.Site.Language,
            outputDir,
            baseUrl,
            routed,
            pluginContext.DerivedRouted,
            pluginContext.DerivedRoutes,
            pluginContext.PluginExecutions.ToList(),
            renderedCount,
            skippedCount,
            new Dictionary<string, int>(renderReasons, StringComparer.OrdinalIgnoreCase));
    }

    private sealed record BuildVariantResult(
        string Language,
        string OutputDir,
        string BaseUrl,
        IReadOnlyList<(ContentItem Item, RouteInfo Route)> Routed,
        IReadOnlyList<(ContentItem Item, RouteInfo Route)> DerivedRouted,
        IReadOnlyList<(RouteInfo Route, DateTimeOffset LastModified)> DerivedRoutes,
        IReadOnlyList<PluginExecutionInfo> PluginExecutions,
        int RenderedCount,
        int SkippedCount,
        IReadOnlyDictionary<string, int> RenderReasons);

    private static void WriteMetricsIfRequested(
        string rootDir,
        string? metricsPath,
        AppConfig config,
        string outputDir,
        int contentItemCount,
        IReadOnlyList<BuildVariantResult> variants)
    {
        if (string.IsNullOrWhiteSpace(metricsPath))
        {
            return;
        }

        var fullPath = Path.IsPathRooted(metricsPath) ? metricsPath : Path.Combine(rootDir, metricsPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var payload = new
        {
            version = 1,
            ts = DateTimeOffset.UtcNow.ToString("O"),
            site = new
            {
                name = config.Site.Name,
                title = config.Site.Title,
                url = config.Site.Url,
                baseUrl = config.Site.BaseUrl,
                language = config.Site.Language,
                defaultLanguage = config.Site.DefaultLanguage,
                languages = config.Site.Languages
            },
            outputDir = Path.GetFullPath(outputDir),
            contentItems = contentItemCount,
            variants = variants.Select(v => new
            {
                language = v.Language,
                baseUrl = v.BaseUrl,
                outputDir = Path.GetFullPath(v.OutputDir),
                routed = v.Routed.Count,
                derived = v.DerivedRouted.Count,
                rendered = v.RenderedCount,
                skipped = v.SkippedCount,
                reasons = v.RenderReasons,
                plugins = v.PluginExecutions
            })
        };

        File.WriteAllText(fullPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void GenerateI18nRootOutputs(AppConfig config, string outputDir, string rootBaseUrl, IReadOnlyList<BuildVariantResult> results)
    {
        var siteUrl = config.Site.Url;
        if (!string.IsNullOrWhiteSpace(siteUrl))
        {
            var sitemapMode = (config.Site.SitemapMode ?? "split").Trim().ToLowerInvariant();
            if (sitemapMode == "merged")
            {
                var defaultLanguage = string.IsNullOrWhiteSpace(config.Site.DefaultLanguage) ? null : config.Site.DefaultLanguage.Trim();

                var alternatesMap = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                void AddAlternate(string groupKey, string language, string absoluteUrl)
                {
                    if (!alternatesMap.TryGetValue(groupKey, out var langs))
                    {
                        langs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        alternatesMap[groupKey] = langs;
                    }

                    if (!langs.ContainsKey(language))
                    {
                        langs[language] = absoluteUrl;
                    }
                }

                foreach (var r in results)
                {
                    AddAlternate("/", r.Language, SitemapGenerator.BuildAbsoluteUrl(siteUrl, r.BaseUrl, "/"));
                    AddAlternate("/blog/", r.Language, SitemapGenerator.BuildAbsoluteUrl(siteUrl, r.BaseUrl, "/blog/"));
                    AddAlternate("/pages/", r.Language, SitemapGenerator.BuildAbsoluteUrl(siteUrl, r.BaseUrl, "/pages/"));

                    foreach (var (item, route) in r.Routed)
                    {
                        if (TryGetI18nKey(item.Meta, out var key))
                        {
                            AddAlternate(key, r.Language, SitemapGenerator.BuildAbsoluteUrl(siteUrl, r.BaseUrl, route.Url));
                        }
                    }

                    foreach (var (route, _) in r.DerivedRoutes)
                    {
                        AddAlternate(route.Url, r.Language, SitemapGenerator.BuildAbsoluteUrl(siteUrl, r.BaseUrl, route.Url));
                    }
                }

                IReadOnlyList<SitemapGenerator.Alternate>? BuildAlternates(string groupKey)
                {
                    if (!alternatesMap.TryGetValue(groupKey, out var map) || map.Count <= 1)
                    {
                        return null;
                    }

                    var list = new List<SitemapGenerator.Alternate>(capacity: map.Count + 1);
                    if (!string.IsNullOrWhiteSpace(defaultLanguage) && map.TryGetValue(defaultLanguage, out var defHref))
                    {
                        list.Add(new SitemapGenerator.Alternate("x-default", defHref));
                    }

                    foreach (var kv in map.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(new SitemapGenerator.Alternate(kv.Key, kv.Value));
                    }

                    return list;
                }

                var entries = new List<SitemapGenerator.UrlEntry>();
                foreach (var r in results)
                {
                    entries.Add(new SitemapGenerator.UrlEntry(
                        SitemapGenerator.BuildAbsoluteUrl(siteUrl, r.BaseUrl, "/"),
                        DateTimeOffset.UtcNow,
                        BuildAlternates("/")));
                    entries.Add(new SitemapGenerator.UrlEntry(
                        SitemapGenerator.BuildAbsoluteUrl(siteUrl, r.BaseUrl, "/blog/"),
                        DateTimeOffset.UtcNow,
                        BuildAlternates("/blog/")));
                    entries.Add(new SitemapGenerator.UrlEntry(
                        SitemapGenerator.BuildAbsoluteUrl(siteUrl, r.BaseUrl, "/pages/"),
                        DateTimeOffset.UtcNow,
                        BuildAlternates("/pages/")));

                    foreach (var (item, route) in r.Routed)
                    {
                        var alts = TryGetI18nKey(item.Meta, out var key) ? BuildAlternates(key) : null;
                        entries.Add(new SitemapGenerator.UrlEntry(
                            SitemapGenerator.BuildAbsoluteUrl(siteUrl, r.BaseUrl, route.Url),
                            item.PublishAt,
                            alts));
                    }

                    foreach (var (route, lastModified) in r.DerivedRoutes)
                    {
                        entries.Add(new SitemapGenerator.UrlEntry(
                            SitemapGenerator.BuildAbsoluteUrl(siteUrl, r.BaseUrl, route.Url),
                            lastModified,
                            BuildAlternates(route.Url)));
                    }
                }

                SitemapGenerator.GenerateAbsoluteWithAlternates(outputDir, entries);
            }
            else if (sitemapMode == "index")
            {
                var sitemaps = results.Select(r => SitemapGenerator.BuildAbsoluteUrl(siteUrl, r.BaseUrl, "/sitemap.xml")).ToList();
                SitemapGenerator.GenerateIndex(outputDir, sitemaps);
            }

            var rssMode = (config.Site.RssMode ?? "split").Trim().ToLowerInvariant();
            if (rssMode == "merged")
            {
                static IReadOnlyList<string>? MergeCategories(IReadOnlyList<string>? tags, IReadOnlyList<string>? categories)
                {
                    if (tags is null && categories is null)
                    {
                        return null;
                    }

                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var list = new List<string>();

                    void Add(IReadOnlyList<string>? items)
                    {
                        if (items is null)
                        {
                            return;
                        }

                        foreach (var v in items)
                        {
                            var t = (v ?? string.Empty).Trim();
                            if (!string.IsNullOrWhiteSpace(t) && seen.Add(t))
                            {
                                list.Add(t);
                            }
                        }
                    }

                    Add(tags);
                    Add(categories);
                    return list.Count == 0 ? null : list;
                }

                var posts = new List<RssGenerator.Post>();
                foreach (var r in results)
                {
                    foreach (var (item, route) in r.Routed.Where(x => x.Route.Url.StartsWith("/blog/", StringComparison.OrdinalIgnoreCase)))
                    {
                        posts.Add(new RssGenerator.Post(
                            Title: item.Title,
                            AbsoluteUrl: RssGenerator.BuildAbsoluteUrl(siteUrl, r.BaseUrl, route.Url),
                            PublishAt: item.PublishAt,
                            Description: GetString(item.Meta, "summary"),
                            Categories: MergeCategories(GetStringList(item.Meta, "tags"), GetStringList(item.Meta, "categories")),
                            ContentHtml: item.ContentHtml));
                    }
                }

                RssGenerator.GenerateMerged(outputDir, siteUrl, rootBaseUrl, config.Site.Title, posts);
            }
        }

        var searchMode = (config.Site.SearchMode ?? "split").Trim().ToLowerInvariant();
        if (searchMode == "merged")
        {
            GenerateMergedSearchIndex(outputDir, results, config.Site.SearchIncludeDerived);
        }
        else if (searchMode == "index")
        {
            GenerateSearchIndexIndex(outputDir, results);
        }
    }

    private static void GenerateMergedSearchIndex(string outputDir, IReadOnlyList<BuildVariantResult> results, bool includeDerived)
    {
        var outPath = Path.Combine(outputDir, "search.json");
        Directory.CreateDirectory(outputDir);

        using var stream = File.Create(outPath);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });

        writer.WriteStartArray();

        foreach (var r in results)
        {
            var items = includeDerived ? r.Routed.Concat(r.DerivedRouted) : r.Routed;
            foreach (var (item, route) in items)
            {
                writer.WriteStartObject();
                writer.WriteString("id", item.Id);
                writer.WriteString("title", item.Title);
                writer.WriteString("url", NormalizeSearchUrl(r.BaseUrl, route.Url));

                if (item.Meta.TryGetValue("summary", out var summary) && summary is not null)
                {
                    writer.WriteString("summary", summary.ToString());
                }

                var text = StripHtmlToText(item.ContentHtml);
                if (text.Length > 8000)
                {
                    text = text[..8000];
                }

                writer.WriteString("content", text);
                writer.WriteString("type", GetString(item.Meta, "type"));

                var tags = GetStringList(item.Meta, "tags");
                if (tags is not null)
                {
                    writer.WriteStartArray("tags");
                    foreach (var t in tags)
                    {
                        writer.WriteStringValue(t);
                    }

                    writer.WriteEndArray();
                }

                var categories = GetStringList(item.Meta, "categories");
                if (categories is not null)
                {
                    writer.WriteStartArray("categories");
                    foreach (var c in categories)
                    {
                        writer.WriteStringValue(c);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteString("language", GetString(item.Meta, "language"));
                writer.WriteString("sourceKey", GetString(item.Meta, "sourceKey") ?? GetString(item.Meta, "source"));
                writer.WriteString("publishAt", item.PublishAt.ToString("O"));
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
        writer.Flush();
    }

    private static void GenerateSearchIndexIndex(string outputDir, IReadOnlyList<BuildVariantResult> results)
    {
        var outPath = Path.Combine(outputDir, "search.index.json");
        Directory.CreateDirectory(outputDir);

        using var stream = File.Create(outPath);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();
        writer.WriteNumber("version", 1);
        writer.WriteStartArray("indexes");
        foreach (var r in results)
        {
            writer.WriteStartObject();
            writer.WriteString("language", r.Language);
            writer.WriteString("path", NormalizeSearchUrl(r.BaseUrl, "/search.json"));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    private static string NormalizeSearchUrl(string baseUrl, string url)
    {
        var u = url.StartsWith('/') ? url : "/" + url;
        if (string.IsNullOrWhiteSpace(baseUrl) || baseUrl == "/")
        {
            return u;
        }

        var b = baseUrl.StartsWith('/') ? baseUrl : "/" + baseUrl;
        if (b.Length > 1 && b.EndsWith('/'))
        {
            b = b.TrimEnd('/');
        }

        return b + u;
    }

    private static string StripHtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(html.Length);
        var inside = false;
        for (var i = 0; i < html.Length; i++)
        {
            var c = html[i];
            if (c == '<')
            {
                inside = true;
                continue;
            }

            if (c == '>')
            {
                inside = false;
                sb.Append(' ');
                continue;
            }

            if (!inside)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().ReplaceLineEndings(" ").Trim();
    }

    private static string? GetString(IReadOnlyDictionary<string, object> meta, string key)
    {
        return meta.TryGetValue(key, out var v) && v is not null ? v.ToString() : null;
    }

    private static IReadOnlyList<string>? GetStringList(IReadOnlyDictionary<string, object> meta, string key)
    {
        if (!meta.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }

        if (v is string s)
        {
            var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? null : parts;
        }

        if (v is IEnumerable<object> seq)
        {
            var list = seq.Select(x => x?.ToString() ?? string.Empty)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            return list.Count == 0 ? null : list;
        }

        return null;
    }

    private static bool TryGetI18nKey(IReadOnlyDictionary<string, object> meta, out string key)
    {
        key = string.Empty;

        object? v = null;
        if (!meta.TryGetValue("i18nKey", out v) || v is null)
        {
            meta.TryGetValue("i18n_key", out v);
        }

        var s = v?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        key = s;
        return true;
    }

    private static (string LayoutsDir, string AssetsDir, string StaticDir) ResolveThemeDirectories(string rootDir, ThemeConfig theme)
    {
        if (string.IsNullOrWhiteSpace(theme.Name))
        {
            return (
                MakeAbsolute(rootDir, theme.Layouts),
                MakeAbsolute(rootDir, theme.Assets),
                MakeAbsolute(rootDir, theme.Static)
            );
        }

        var themeRoot = Path.Combine(rootDir, "themes", theme.Name);

        var layouts = string.Equals(theme.Layouts, "layouts", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(themeRoot, "layouts")
            : MakeAbsolute(rootDir, theme.Layouts);

        var assets = string.Equals(theme.Assets, "assets", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(themeRoot, "assets")
            : MakeAbsolute(rootDir, theme.Assets);

        var stat = string.Equals(theme.Static, "static", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(themeRoot, "static")
            : MakeAbsolute(rootDir, theme.Static);

        return (layouts, assets, stat);
    }

    private static IReadOnlyList<string> GetLanguages(SiteConfig site)
    {
        if (site.Languages is not { Count: > 0 } langs)
        {
            return Array.Empty<string>();
        }

        var cleaned = langs.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        if (cleaned.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in cleaned)
        {
            if (seen.Add(l))
            {
                result.Add(l);
            }
        }

        return result;
    }

    private static string GetDefaultLanguage(SiteConfig site, IReadOnlyList<string> languages)
    {
        if (languages.Count == 0)
        {
            return site.Language;
        }

        if (string.IsNullOrWhiteSpace(site.DefaultLanguage))
        {
            return languages[0];
        }

        var dl = site.DefaultLanguage.Trim();
        return languages.Contains(dl, StringComparer.OrdinalIgnoreCase) ? dl : languages[0];
    }

    private static string CombineBaseUrlWithLanguage(string baseUrl, string language)
    {
        var b = NormalizeBaseUrl(baseUrl);
        var l = language.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(l))
        {
            return b;
        }

        if (b == "/")
        {
            return "/" + l;
        }

        return b.TrimEnd('/') + "/" + l;
    }

    private static string SanitizeFileSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "default";
        }

        var chars = value.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    private static IReadOnlyList<ContentItem> FilterItemsByLanguage(IReadOnlyList<ContentItem> items, string language, string defaultLanguage)
    {
        return items.Where(item =>
        {
            if (IsDataItem(item))
            {
                var locale = TryGetTextField(item.Fields, "locale");
                return string.IsNullOrWhiteSpace(locale) || string.Equals(locale, language, StringComparison.OrdinalIgnoreCase);
            }

            if (item.Meta.TryGetValue("language", out var v) && v is not null && !string.IsNullOrWhiteSpace(v.ToString()))
            {
                return string.Equals(v.ToString(), language, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(language, defaultLanguage, StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    private static bool IsDataItem(ContentItem item)
    {
        return item.Meta.TryGetValue("sourceMode", out var v) &&
               v is not null &&
               string.Equals(v.ToString(), "data", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ModuleInfo>>? BuildModules(IReadOnlyList<ContentItem> dataItems, string language)
    {
        if (dataItems.Count == 0)
        {
            return null;
        }

        var map = new Dictionary<string, List<ModuleInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in dataItems)
        {
            var enabled = TryGetBoolField(item.Fields, "enabled");
            if (enabled is false)
            {
                continue;
            }

            var type = item.Meta.TryGetValue("type", out var v) && v is not null ? (v.ToString() ?? string.Empty) : string.Empty;
            type = type.Trim();
            if (string.IsNullOrWhiteSpace(type))
            {
                type = "module";
            }

            if (!map.TryGetValue(type, out var list))
            {
                list = new List<ModuleInfo>();
                map[type] = list;
            }

            list.Add(new ModuleInfo
            {
                Id = item.Id,
                Title = item.Title,
                Slug = item.Slug,
                Content = item.ContentHtml,
                Fields = item.Fields
            });
        }

        var result = new Dictionary<string, IReadOnlyList<ModuleInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in map)
        {
            var ordered = kv.Value
                .OrderBy(x => TryGetNumberField(x.Fields, "order") ?? 0d)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result[kv.Key] = ordered;
        }

        return result;
    }

    private static bool? TryGetBoolField(IReadOnlyDictionary<string, ContentField>? fields, string key)
    {
        if (fields is null)
        {
            return null;
        }

        if (!fields.TryGetValue(key, out var field))
        {
            var alt = fields.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (alt is null || !fields.TryGetValue(alt, out field))
            {
                return null;
            }
        }

        return field.Value switch
        {
            null => null,
            bool b => b,
            string s when bool.TryParse(s, out var b) => b,
            int i => i != 0,
            long l => l != 0,
            double d => Math.Abs(d) > double.Epsilon,
            _ => null
        };
    }

    private static string? TryGetTextField(IReadOnlyDictionary<string, ContentField>? fields, string key)
    {
        if (fields is null)
        {
            return null;
        }

        if (!fields.TryGetValue(key, out var field))
        {
            var alt = fields.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (alt is null || !fields.TryGetValue(alt, out field))
            {
                return null;
            }
        }

        return field.Value switch
        {
            null => null,
            string s => s,
            _ => field.Value.ToString()
        };
    }

    private static double? TryGetNumberField(IReadOnlyDictionary<string, ContentField>? fields, string key)
    {
        if (fields is null)
        {
            return null;
        }

        if (!fields.TryGetValue(key, out var field))
        {
            var alt = fields.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (alt is null || !fields.TryGetValue(alt, out field))
            {
                return null;
            }
        }

        return field.Value switch
        {
            null => null,
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal m => (double)m,
            string s when double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }

    private static string NormalizeRelPath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string ComputeContentHash(ContentItem item)
    {
        var type = item.Meta.TryGetValue("type", out var typeObj) && typeObj is not null ? typeObj.ToString() : string.Empty;
        var summary = item.Meta.TryGetValue("summary", out var summaryObj) && summaryObj is not null ? summaryObj.ToString() : string.Empty;
        var fieldsFingerprint = ComputeFieldsFingerprint(item.Fields);
        var fingerprint = string.Join(
            "\n",
            item.Id,
            item.Title,
            item.Slug,
            item.PublishAt.ToString("O"),
            type ?? string.Empty,
            summary ?? string.Empty,
            fieldsFingerprint,
            item.ContentHtml);

        return HashUtil.Sha256Hex(fingerprint);
    }

    private static string ComputeFieldsFingerprint(IReadOnlyDictionary<string, ContentField>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return string.Empty;
        }

        var keys = fields.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var parts = new List<string>(capacity: keys.Count * 3);

        foreach (var k in keys)
        {
            if (!fields.TryGetValue(k, out var f))
            {
                continue;
            }

            parts.Add(k);
            parts.Add(f.Type ?? string.Empty);
            parts.Add(FieldValueToString(f.Value));
        }

        return string.Join("\n", parts);
    }

    private static string FieldValueToString(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is DateTimeOffset dto)
        {
            return dto.ToString("O");
        }

        if (value is DateTime dt)
        {
            return dt.ToUniversalTime().ToString("O");
        }

        if (value is IEnumerable<object> seq)
        {
            var items = seq.Select(FieldValueToString).ToList();
            return string.Join(",", items);
        }

        if (value is IEnumerable<string> sseq)
        {
            return string.Join(",", sseq);
        }

        return value.ToString() ?? string.Empty;
    }

    private static string ComputeRouteHash(RouteInfo route)
    {
        var fingerprint = string.Join("\n", route.Url, NormalizeRelPath(route.OutputPath), route.Template);
        return HashUtil.Sha256Hex(fingerprint);
    }

    public async Task BuildAsync(IContentProvider provider, BuildOptions options, CancellationToken cancellationToken = default)
    {
        if (options.Clean && Directory.Exists(options.OutputDir))
        {
            Directory.Delete(options.OutputDir, recursive: true);
        }

        Directory.CreateDirectory(options.OutputDir);

        var baseUrl = NormalizeBaseUrl(options.BaseUrl);
        var items = await provider.LoadAsync(cancellationToken);

        _logger.Info($"Loaded content: {items.Count}");

        var routed = items
            .Select(i => (Item: i, Route: RouteGenerator.Generate(i, options.OutputPathEncoding)))
            .ToList();

        var warnedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (item, route) in routed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WarnIfWindowsIncompatible(route.OutputPath, warnedOutputPaths);
            var html = RenderSimplePage(baseUrl, item.Title, route.Url, item.ContentHtml);
            FileWriter.WriteUtf8(options.OutputDir, route.OutputPath, html);
        }

        FileWriter.WriteUtf8(options.OutputDir, "index.html", RenderSimpleIndex(baseUrl, routed));
        FileWriter.WriteUtf8(options.OutputDir, Path.Combine("blog", "index.html"), RenderSimpleIndex(baseUrl, routed.Where(x => x.Route.Url.StartsWith("/blog/", StringComparison.OrdinalIgnoreCase)).ToList(), "Blog"));
        FileWriter.WriteUtf8(options.OutputDir, Path.Combine("pages", "index.html"), RenderSimpleIndex(baseUrl, routed.Where(x => x.Route.Url.StartsWith("/pages/", StringComparison.OrdinalIgnoreCase)).ToList(), "Pages"));

        if (!string.IsNullOrWhiteSpace(options.AssetsDir))
        {
            DirectoryCopy.Copy(options.AssetsDir, Path.Combine(options.OutputDir, "assets"));
        }

        if (!string.IsNullOrWhiteSpace(options.SiteUrl) && options.GenerateSitemap)
        {
            var metaRoutes = new List<(RouteInfo Route, DateTimeOffset LastModified)>(capacity: routed.Count + 3)
            {
                (new RouteInfo("/", "index.html", "pages/index.html"), DateTimeOffset.UtcNow),
                (new RouteInfo("/blog/", Path.Combine("blog", "index.html"), "pages/index.html"), DateTimeOffset.UtcNow),
                (new RouteInfo("/pages/", Path.Combine("pages", "index.html"), "pages/index.html"), DateTimeOffset.UtcNow)
            };

            metaRoutes.AddRange(routed.Select(x => (x.Route, x.Item.PublishAt)));
            SitemapGenerator.Generate(options.OutputDir, options.SiteUrl, baseUrl, metaRoutes);
        }

        if (!string.IsNullOrWhiteSpace(options.SiteUrl) && options.GenerateRss)
        {
            RssGenerator.Generate(options.OutputDir, options.SiteUrl, baseUrl, options.SiteTitle, routed);
        }

        _logger.Info($"Build completed: {Path.GetFullPath(options.OutputDir)}");
    }

    private static IContentProvider CreateContentProvider(AppConfig config, string rootDir, bool isCi)
    {
        if (config.Content.Sources is { Count: > 0 } sources)
        {
            var typeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sources)
            {
                var t = (s.Type ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(t))
                {
                    continue;
                }

                typeCounts[t] = typeCounts.TryGetValue(t, out var c) ? c + 1 : 1;
            }

            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var providers = new List<(string SourceKey, string SourceMode, IContentProvider Provider)>();

            for (var i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                var type = (s.Type ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(type))
                {
                    throw new ContentException("content.sources[].type is required.");
                }

                var mode = (s.Mode ?? "content").Trim().ToLowerInvariant();
                var key = string.IsNullOrWhiteSpace(s.Name) ? string.Empty : s.Name.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = type.ToLowerInvariant();
                    if (typeCounts.TryGetValue(type, out var count) && count > 1)
                    {
                        seen[type] = seen.TryGetValue(type, out var n) ? n + 1 : 1;
                        key = $"{key}{seen[type]}";
                    }
                }

                if (type.Equals("markdown", StringComparison.OrdinalIgnoreCase))
                {
                    var md = s.Markdown ?? new MarkdownConfig();
                    var contentDir = MakeAbsolute(rootDir, md.Dir);
                    providers.Add((key, mode, new MarkdownFolderProvider(new MarkdownFolderProviderOptions(contentDir, md.DefaultType))));
                    continue;
                }

                if (type.Equals("notion", StringComparison.OrdinalIgnoreCase))
                {
                    var notion = s.Notion;
                    if (notion is null)
                    {
                        throw new ContentException("content.sources[].notion is required when type is notion.");
                    }

                    var renderContent = notion.RenderContent ?? mode != "data";
                    providers.Add((key, mode, CreateNotionProvider(notion, isCi, renderContent: renderContent)));
                    continue;
                }

                throw new ContentException($"Unsupported content source type: {type}");
            }

            return new CompositeContentProvider(providers);
        }

        if (config.Content.Provider.Equals("markdown", StringComparison.OrdinalIgnoreCase))
        {
            var md = config.Content.Markdown ?? new MarkdownConfig();
            var contentDir = MakeAbsolute(rootDir, md.Dir);
            return new MarkdownFolderProvider(new MarkdownFolderProviderOptions(contentDir, md.DefaultType));
        }

        if (config.Content.Provider.Equals("notion", StringComparison.OrdinalIgnoreCase))
        {
            var notion = config.Content.Notion;
            if (notion is null)
            {
                throw new ContentException("content.notion is required when provider is notion.");
            }

            var renderContent = notion.RenderContent ?? true;
            return CreateNotionProvider(notion, isCi, renderContent: renderContent);
        }

        throw new ContentException($"Unknown content provider: {config.Content.Provider}");
    }

    private static NotionContentProvider CreateNotionProvider(NotionConfig notion, bool isCi, bool renderContent)
    {
        var token = Environment.GetEnvironmentVariable("NOTION_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ContentException("NOTION_TOKEN is required for notion provider and must come from environment variables.");
        }

        return new NotionContentProvider(new NotionProviderOptions
        {
            DatabaseId = notion.DatabaseId,
            Token = token,
            PageSize = notion.PageSize,
            RequestDelayMs = isCi ? 150 : 0,
            FieldPolicyMode = notion.FieldPolicy.Mode,
            AllowedFields = notion.FieldPolicy.Allowed,
            FilterProperty = notion.FilterProperty,
            FilterType = notion.FilterType,
            SortProperty = notion.SortProperty,
            SortDirection = notion.SortDirection,
            RenderContent = renderContent
        });
    }

    private static string MakeAbsolute(string rootDir, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(rootDir, path));
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "/";
        }

        var trimmed = baseUrl.Trim();
        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }

        if (trimmed.Length > 1 && trimmed.EndsWith('/'))
        {
            trimmed = trimmed.TrimEnd('/');
        }

        return trimmed;
    }

    private void WarnIfWindowsIncompatible(string outputPath, HashSet<string> warned)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var normalized = outputPath.Replace('\\', '/');
        if (!warned.Add(normalized))
        {
            return;
        }

        if (!TryGetWindowsPathIssue(normalized, out var issue))
        {
            return;
        }

        _logger.Warn($"windows path warning: outputPath '{normalized}' {issue}");
    }

    private static bool TryGetWindowsPathIssue(string outputPath, out string issue)
    {
        issue = string.Empty;

        var segments = outputPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            issue = "is empty.";
            return true;
        }

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                issue = "contains an empty path segment.";
                return true;
            }

            if (segment.EndsWith(' ') || segment.EndsWith('.'))
            {
                issue = $"has a segment that ends with a space or dot: '{segment}'.";
                return true;
            }

            foreach (var ch in segment)
            {
                if (ch < 32 || ch is '<' or '>' or ':' or '\"' or '|' or '?' or '*')
                {
                    issue = $"contains an invalid Windows character in segment '{segment}'.";
                    return true;
                }
            }

            var baseName = segment.Split('.', 2, StringSplitOptions.RemoveEmptyEntries)[0];
            if (IsWindowsDeviceName(baseName))
            {
                issue = $"uses a reserved Windows device name: '{baseName}'.";
                return true;
            }
        }

        return false;
    }

    private static bool IsWindowsDeviceName(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var name = segment.Trim().ToLowerInvariant();
        if (name is "con" or "prn" or "aux" or "nul")
        {
            return true;
        }

        if (name.Length == 4 && name.StartsWith("com") && char.IsDigit(name[3]))
        {
            return true;
        }

        if (name.Length == 4 && name.StartsWith("lpt") && char.IsDigit(name[3]))
        {
            return true;
        }

        return false;
    }

    private static string RenderSimplePage(string baseUrl, string title, string url, string contentHtml)
    {
        var cssHref = baseUrl == "/" ? "/assets/style.css" : $"{baseUrl}/assets/style.css";
        var canonical = baseUrl == "/" ? url : $"{baseUrl}{url}";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine($"  <title>{EscapeHtml(title)}</title>");
        sb.AppendLine($"  <link rel=\"stylesheet\" href=\"{cssHref}\" />");
        sb.AppendLine($"  <link rel=\"canonical\" href=\"{canonical}\" />");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <main class=\"container\">");
        sb.AppendLine($"    <h1>{EscapeHtml(title)}</h1>");
        sb.AppendLine("    <div class=\"content\">");
        sb.AppendLine(contentHtml);
        sb.AppendLine("    </div>");
        sb.AppendLine("  </main>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string RenderSimpleIndex(string baseUrl, IReadOnlyList<(ContentItem Item, RouteInfo Route)> routed, string title = "SiteGen")
    {
        var cssHref = baseUrl == "/" ? "/assets/style.css" : $"{baseUrl}/assets/style.css";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine($"  <title>{EscapeHtml(title)}</title>");
        sb.AppendLine($"  <link rel=\"stylesheet\" href=\"{cssHref}\" />");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <main class=\"container\">");
        sb.AppendLine($"    <h1>{EscapeHtml(title)}</h1>");
        sb.AppendLine("    <ul>");

        foreach (var (item, route) in routed)
        {
            var href = baseUrl == "/" ? route.Url : $"{baseUrl}{route.Url}";
            sb.AppendLine($"      <li><a href=\"{href}\">{EscapeHtml(item.Title)}</a></li>");
        }

        sb.AppendLine("    </ul>");
        sb.AppendLine("  </main>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
    }
}
