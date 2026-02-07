namespace SiteGen.Config;

public sealed record AppConfig
{
    public required SiteConfig Site { get; init; }
    public required ContentConfig Content { get; init; }
    public BuildConfig Build { get; init; } = new();
    public ThemeConfig Theme { get; init; } = new();
    public TaxonomyConfig Taxonomy { get; init; } = new();
    public LoggingConfig Logging { get; init; } = new();
}

public sealed record SiteConfig
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public string? Url { get; init; }
    public string? Description { get; init; }
    public bool AutoSummary { get; init; }
    public int AutoSummaryMaxLength { get; init; } = 200;
    public string BaseUrl { get; init; } = "/";
    public string OutputPathEncoding { get; init; } = "none";
    public string Language { get; init; } = "zh-CN";
    public IReadOnlyList<string>? Languages { get; init; }
    public string? DefaultLanguage { get; init; }
    public string SitemapMode { get; init; } = "split";
    public string RssMode { get; init; } = "split";
    public string SearchMode { get; init; } = "split";
    public bool SearchIncludeDerived { get; init; }
    public string PluginFailMode { get; init; } = "strict";
    public string Timezone { get; init; } = "Asia/Shanghai";
    public IReadOnlyDictionary<string, PluginToggleConfig>? Plugins { get; init; }
}

public sealed record ContentConfig
{
    public required string Provider { get; init; }
    public IReadOnlyList<ContentSourceConfig>? Sources { get; init; }
    public NotionConfig? Notion { get; init; }
    public MarkdownConfig? Markdown { get; init; }
}

public sealed record ContentSourceConfig
{
    public required string Type { get; init; }
    public string? Name { get; init; }
    public string Mode { get; init; } = "content";
    public NotionConfig? Notion { get; init; }
    public MarkdownConfig? Markdown { get; init; }
}

public sealed record NotionConfig
{
    public required string DatabaseId { get; init; }
    public int PageSize { get; init; } = 50;
    public int? MaxItems { get; init; }
    public bool? RenderContent { get; init; }
    public int? RenderConcurrency { get; init; }
    public int? MaxRps { get; init; }
    public int? MaxRetries { get; init; }
    public NotionFieldPolicyConfig FieldPolicy { get; init; } = new();
    public string FilterProperty { get; init; } = "Published";
    public string FilterType { get; init; } = "checkbox_true";
    public string? SortProperty { get; init; }
    public string SortDirection { get; init; } = "ascending";
    public IReadOnlyList<string>? IncludeSlugs { get; init; }
    public string IncludeSlugProperty { get; init; } = "Slug";
    public string CacheMode { get; init; } = "off";
    public string? CacheDir { get; init; }
}

public sealed record NotionFieldPolicyConfig
{
    public string Mode { get; init; } = "whitelist";
    public IReadOnlyList<string>? Allowed { get; init; }
}

public sealed record MarkdownConfig
{
    public string Dir { get; init; } = "content";
    public string DefaultType { get; init; } = "page";
    public int? MaxItems { get; init; }
    public IReadOnlyList<string>? IncludePaths { get; init; }
    public IReadOnlyList<string>? IncludeGlobs { get; init; }
}

public sealed record BuildConfig
{
    public string Output { get; init; } = "dist";
    public bool Clean { get; init; } = true;
    public bool Draft { get; init; }
}

public sealed record ThemeConfig
{
    public string? Name { get; init; }
    public string Layouts { get; init; } = "layouts";
    public string Assets { get; init; } = "assets";
    public string Static { get; init; } = "static";
    public IReadOnlyDictionary<string, object>? Params { get; init; }
}

public sealed record TaxonomyConfig
{
    public string Template { get; init; } = "pages/page.html";
    public string? IndexTemplate { get; init; }
    public string? TermTemplate { get; init; }
    public TaxonomyTemplatesConfig Templates { get; init; } = new();
    public IReadOnlyList<TaxonomyKindConfig>? Kinds { get; init; }
    public string OutputMode { get; init; } = "both";
    public IReadOnlyList<string>? ItemFields { get; init; }
    public int PageSize { get; init; } = 10;
    public bool IndexEnabled { get; init; } = true;
}

public sealed record TaxonomyTemplatesConfig
{
    public TaxonomyKindTemplateConfig Tags { get; init; } = new();
    public TaxonomyKindTemplateConfig Categories { get; init; } = new();
}

public sealed record TaxonomyKindConfig
{
    public required string Key { get; init; }
    public string? Kind { get; init; }
    public string? Title { get; init; }
    public string? SingularTitlePrefix { get; init; }
    public string? Template { get; init; }
    public string? IndexTemplate { get; init; }
    public string? TermTemplate { get; init; }
    public bool? IndexEnabled { get; init; }
}

public sealed record TaxonomyKindTemplateConfig
{
    public string? Template { get; init; }
    public string? IndexTemplate { get; init; }
    public string? TermTemplate { get; init; }
}

public sealed record LoggingConfig
{
    public string Level { get; init; } = "info";
}

public sealed record PluginToggleConfig
{
    public bool Enabled { get; init; } = true;
}
