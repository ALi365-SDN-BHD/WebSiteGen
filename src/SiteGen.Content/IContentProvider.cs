namespace SiteGen.Content;

public interface IContentProvider
{
    Task<IReadOnlyList<ContentItem>> LoadAsync(CancellationToken cancellationToken = default);
}

