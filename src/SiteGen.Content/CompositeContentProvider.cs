using SiteGen.Shared;

namespace SiteGen.Content;

public sealed class CompositeContentProvider : IContentProvider
{
    private readonly IReadOnlyList<(string SourceKey, string SourceMode, IContentProvider Provider)> _providers;

    public CompositeContentProvider(IReadOnlyList<(string SourceKey, string SourceMode, IContentProvider Provider)> providers)
    {
        _providers = providers;
    }

    public async Task<IReadOnlyList<ContentItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var all = new List<ContentItem>();

        foreach (var (sourceKey, sourceMode, provider) in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var items = await provider.LoadAsync(cancellationToken);
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var meta = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in item.Meta)
                {
                    meta[kv.Key] = kv.Value;
                }

                meta["sourceKey"] = sourceKey;
                meta["sourceMode"] = sourceMode;
                meta["sourceId"] = item.Id;

                all.Add(item with
                {
                    Id = $"{sourceKey}:{item.Id}",
                    Meta = meta
                });
            }
        }

        return all;
    }
}
