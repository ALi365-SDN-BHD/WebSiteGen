using SiteGen.Shared;

namespace SiteGen.Config;

public static class ConfigValidator
{
    public static void Validate(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Site.Name))
        {
            throw new ConfigException("site.name is required.");
        }

        if (string.IsNullOrWhiteSpace(config.Site.Title))
        {
            throw new ConfigException("site.title is required.");
        }

        if (!string.IsNullOrWhiteSpace(config.Site.Url) &&
            !(config.Site.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
              config.Site.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConfigException("site.url must start with http:// or https:// when set.");
        }

        if (config.Site.AutoSummaryMaxLength <= 0 || config.Site.AutoSummaryMaxLength > 5000)
        {
            throw new ConfigException("site.autoSummaryMaxLength must be between 1 and 5000.");
        }

        if (string.IsNullOrWhiteSpace(config.Site.BaseUrl))
        {
            throw new ConfigException("site.baseUrl is required.");
        }

        if (!config.Site.BaseUrl.StartsWith('/'))
        {
            throw new ConfigException("site.baseUrl must start with '/'.");
        }

        var outputPathEncoding = (config.Site.OutputPathEncoding ?? "none").Trim().ToLowerInvariant();
        if (outputPathEncoding is not ("none" or "slug" or "urlencode" or "sanitize"))
        {
            throw new ConfigException("site.outputPathEncoding must be none|slug|urlencode|sanitize.");
        }

        if (config.Site.Languages is { Count: > 0 } languages)
        {
            var cleaned = languages.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            if (cleaned.Count == 0)
            {
                throw new ConfigException("site.languages must contain at least one language.");
            }

            var dup = cleaned.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (dup is not null)
            {
                throw new ConfigException($"site.languages has duplicate language: {dup.Key}");
            }

            var defaultLang = string.IsNullOrWhiteSpace(config.Site.DefaultLanguage) ? cleaned[0] : config.Site.DefaultLanguage.Trim();
            if (!cleaned.Contains(defaultLang, StringComparer.OrdinalIgnoreCase))
            {
                throw new ConfigException("site.defaultLanguage must be included in site.languages.");
            }
        }

        var sitemapMode = (config.Site.SitemapMode ?? "split").Trim().ToLowerInvariant();
        if (sitemapMode is not ("split" or "merged" or "index"))
        {
            throw new ConfigException("site.sitemapMode must be split|merged|index.");
        }

        var rssMode = (config.Site.RssMode ?? "split").Trim().ToLowerInvariant();
        if (rssMode is not ("split" or "merged"))
        {
            throw new ConfigException("site.rssMode must be split|merged.");
        }

        var searchMode = (config.Site.SearchMode ?? "split").Trim().ToLowerInvariant();
        if (searchMode is not ("split" or "merged" or "index"))
        {
            throw new ConfigException("site.searchMode must be split|merged|index.");
        }

        var pluginFailMode = (config.Site.PluginFailMode ?? "strict").Trim().ToLowerInvariant();
        if (pluginFailMode is not ("strict" or "warn"))
        {
            throw new ConfigException("site.pluginFailMode must be strict|warn.");
        }

        if (config.Site.Plugins is not null)
        {
            foreach (var kv in config.Site.Plugins)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                {
                    throw new ConfigException("site.plugins keys must be non-empty strings.");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(config.Content.Provider))
        {
            throw new ConfigException("content.provider is required.");
        }

        if (config.Content.Sources is { Count: > 0 })
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in config.Content.Sources)
            {
                if (string.IsNullOrWhiteSpace(source.Type))
                {
                    throw new ConfigException("content.sources[].type is required.");
                }

                var mode = (source.Mode ?? "content").Trim().ToLowerInvariant();
                if (mode is not ("content" or "data"))
                {
                    throw new ConfigException("content.sources[].mode must be content|data.");
                }

                if (!string.IsNullOrWhiteSpace(source.Name))
                {
                    if (!names.Add(source.Name.Trim()))
                    {
                        throw new ConfigException("content.sources[].name must be unique when set.");
                    }
                }

                if (source.Type.Equals("notion", StringComparison.OrdinalIgnoreCase))
                {
                    if (source.Notion is null)
                    {
                        throw new ConfigException("content.sources[].notion is required when type is notion.");
                    }

                    ValidateNotion(source.Notion);
                    continue;
                }

                if (source.Type.Equals("markdown", StringComparison.OrdinalIgnoreCase))
                {
                    if (source.Markdown is null)
                    {
                        throw new ConfigException("content.sources[].markdown is required when type is markdown.");
                    }

                    ValidateMarkdown(source.Markdown);
                    continue;
                }

                throw new ConfigException($"Unsupported content source type: {source.Type}");
            }
        }
        else if (config.Content.Provider.Equals("notion", StringComparison.OrdinalIgnoreCase))
        {
            if (config.Content.Notion is null)
            {
                throw new ConfigException("content.notion is required when provider is notion.");
            }

            ValidateNotion(config.Content.Notion);
        }

        else if (config.Content.Provider.Equals("markdown", StringComparison.OrdinalIgnoreCase))
        {
            if (config.Content.Markdown is null)
            {
                throw new ConfigException("content.markdown is required when provider is markdown.");
            }

            ValidateMarkdown(config.Content.Markdown);
        }

        if (string.IsNullOrWhiteSpace(config.Build.Output))
        {
            throw new ConfigException("build.output is required.");
        }

        if (string.IsNullOrWhiteSpace(config.Taxonomy.Template))
        {
            throw new ConfigException("taxonomy.template must be a non-empty string when set.");
        }

        var taxonomyOutputMode = (config.Taxonomy.OutputMode ?? "both").Trim().ToLowerInvariant();
        if (taxonomyOutputMode is not ("both" or "pages" or "data" or "fields_only"))
        {
            throw new ConfigException("taxonomy.outputMode must be both|pages|data|fields_only.");
        }

        if (config.Taxonomy.PageSize <= 0)
        {
            throw new ConfigException("taxonomy.pageSize must be a positive integer.");
        }

        if (config.Taxonomy.IndexTemplate is not null && string.IsNullOrWhiteSpace(config.Taxonomy.IndexTemplate))
        {
            throw new ConfigException("taxonomy.indexTemplate must be a non-empty string when set.");
        }

        if (config.Taxonomy.TermTemplate is not null && string.IsNullOrWhiteSpace(config.Taxonomy.TermTemplate))
        {
            throw new ConfigException("taxonomy.termTemplate must be a non-empty string when set.");
        }

        if (config.Taxonomy.ItemFields is { Count: > 0 } itemFields)
        {
            for (var i = 0; i < itemFields.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(itemFields[i]))
                {
                    throw new ConfigException($"taxonomy.itemFields[{i}] must be a non-empty string.");
                }
            }
        }

        ValidateTaxonomyKind("taxonomy.templates.tags", config.Taxonomy.Templates.Tags);
        ValidateTaxonomyKind("taxonomy.templates.categories", config.Taxonomy.Templates.Categories);

        if (config.Taxonomy.Kinds is { Count: > 0 } kinds)
        {
            for (var i = 0; i < kinds.Count; i++)
            {
                ValidateTaxonomyKindConfig($"taxonomy.kinds[{i}]", kinds[i]);
            }
        }
    }

    private static void ValidateTaxonomyKind(string prefix, TaxonomyKindTemplateConfig kind)
    {
        if (kind.Template is not null && string.IsNullOrWhiteSpace(kind.Template))
        {
            throw new ConfigException($"{prefix}.template must be a non-empty string when set.");
        }

        if (kind.IndexTemplate is not null && string.IsNullOrWhiteSpace(kind.IndexTemplate))
        {
            throw new ConfigException($"{prefix}.indexTemplate must be a non-empty string when set.");
        }

        if (kind.TermTemplate is not null && string.IsNullOrWhiteSpace(kind.TermTemplate))
        {
            throw new ConfigException($"{prefix}.termTemplate must be a non-empty string when set.");
        }
    }

    private static void ValidateTaxonomyKindConfig(string prefix, TaxonomyKindConfig kind)
    {
        if (string.IsNullOrWhiteSpace(kind.Key))
        {
            throw new ConfigException($"{prefix}.key is required.");
        }

        if (kind.Kind is not null && string.IsNullOrWhiteSpace(kind.Kind))
        {
            throw new ConfigException($"{prefix}.kind must be a non-empty string when set.");
        }

        if (kind.Title is not null && string.IsNullOrWhiteSpace(kind.Title))
        {
            throw new ConfigException($"{prefix}.title must be a non-empty string when set.");
        }

        if (kind.SingularTitlePrefix is not null && string.IsNullOrWhiteSpace(kind.SingularTitlePrefix))
        {
            throw new ConfigException($"{prefix}.singularTitlePrefix must be a non-empty string when set.");
        }

        if (kind.Template is not null && string.IsNullOrWhiteSpace(kind.Template))
        {
            throw new ConfigException($"{prefix}.template must be a non-empty string when set.");
        }

        if (kind.IndexTemplate is not null && string.IsNullOrWhiteSpace(kind.IndexTemplate))
        {
            throw new ConfigException($"{prefix}.indexTemplate must be a non-empty string when set.");
        }

        if (kind.TermTemplate is not null && string.IsNullOrWhiteSpace(kind.TermTemplate))
        {
            throw new ConfigException($"{prefix}.termTemplate must be a non-empty string when set.");
        }
    }

    private static void ValidateNotion(NotionConfig notion)
    {
        if (string.IsNullOrWhiteSpace(notion.DatabaseId))
        {
            throw new ConfigException("content.notion.databaseId is required.");
        }

        if (notion.MaxItems is not null && notion.MaxItems.Value <= 0)
        {
            throw new ConfigException("content.notion.maxItems must be a positive integer when set.");
        }

        if (notion.RenderConcurrency is not null && notion.RenderConcurrency.Value <= 0)
        {
            throw new ConfigException("content.notion.renderConcurrency must be a positive integer when set.");
        }

        if (notion.MaxRps is not null && notion.MaxRps.Value <= 0)
        {
            throw new ConfigException("content.notion.maxRps must be a positive integer when set.");
        }

        if (notion.MaxRetries is not null && notion.MaxRetries.Value < 0)
        {
            throw new ConfigException("content.notion.maxRetries must be a non-negative integer when set.");
        }

        var mode = (notion.FieldPolicy.Mode ?? "whitelist").Trim().ToLowerInvariant();
        if (mode is not ("whitelist" or "all"))
        {
            throw new ConfigException("content.notion.fieldPolicy.mode must be whitelist|all.");
        }

        var filterType = (notion.FilterType ?? "checkbox_true").Trim().ToLowerInvariant();
        if (filterType is not ("checkbox_true" or "none"))
        {
            throw new ConfigException("content.notion.filterType must be checkbox_true|none.");
        }

        if (filterType != "none" && string.IsNullOrWhiteSpace(notion.FilterProperty))
        {
            throw new ConfigException("content.notion.filterProperty is required when filterType is not none.");
        }

        if (!string.IsNullOrWhiteSpace(notion.SortProperty))
        {
            var dir = (notion.SortDirection ?? "ascending").Trim().ToLowerInvariant();
            if (dir is not ("ascending" or "descending"))
            {
                throw new ConfigException("content.notion.sortDirection must be ascending|descending.");
            }
        }

        if (notion.IncludeSlugs is { Count: > 0 })
        {
            if (string.IsNullOrWhiteSpace(notion.IncludeSlugProperty))
            {
                throw new ConfigException("content.notion.includeSlugProperty is required when includeSlugs is set.");
            }
        }

        var cacheMode = (notion.CacheMode ?? "off").Trim().ToLowerInvariant();
        if (cacheMode is not ("off" or "readwrite" or "readonly"))
        {
            throw new ConfigException("content.notion.cacheMode must be off|readwrite|readonly.");
        }

        if (notion.CacheDir is not null && string.IsNullOrWhiteSpace(notion.CacheDir))
        {
            throw new ConfigException("content.notion.cacheDir must be a non-empty string when set.");
        }

        var token = Environment.GetEnvironmentVariable("NOTION_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ConfigException("NOTION_TOKEN is required for notion provider and must come from environment variables.");
        }
    }

    private static void ValidateMarkdown(MarkdownConfig markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown.Dir))
        {
            throw new ConfigException("content.markdown.dir is required.");
        }

        if (markdown.MaxItems is not null && markdown.MaxItems.Value <= 0)
        {
            throw new ConfigException("content.markdown.maxItems must be a positive integer when set.");
        }

        if (markdown.IncludePaths is { Count: > 0 } includePaths)
        {
            for (var i = 0; i < includePaths.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(includePaths[i]))
                {
                    throw new ConfigException($"content.markdown.includePaths[{i}] must be a non-empty string.");
                }
            }
        }

        if (markdown.IncludeGlobs is { Count: > 0 } includeGlobs)
        {
            for (var i = 0; i < includeGlobs.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(includeGlobs[i]))
                {
                    throw new ConfigException($"content.markdown.includeGlobs[{i}] must be a non-empty string.");
                }
            }
        }
    }
}
