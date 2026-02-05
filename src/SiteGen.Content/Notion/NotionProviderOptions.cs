namespace SiteGen.Content.Notion;

public sealed record NotionProviderOptions
{
    public required string DatabaseId { get; init; }
    public required string Token { get; init; }
    public int PageSize { get; init; } = 50;
    public int RequestDelayMs { get; init; }
    public string FieldPolicyMode { get; init; } = "whitelist";
    public IReadOnlyList<string>? AllowedFields { get; init; }
    public string FilterProperty { get; init; } = "Published";
    public string FilterType { get; init; } = "checkbox_true";
    public string? SortProperty { get; init; }
    public string SortDirection { get; init; } = "ascending";
    public bool RenderContent { get; init; } = true;
}
