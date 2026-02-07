using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using System.Collections.Concurrent;

namespace SiteGen.Rendering.Scriban;

public sealed class FileTemplateLoader : ITemplateLoader
{
    private readonly string _rootDir;
    private readonly ConcurrentDictionary<string, CachedText> _cache = new(StringComparer.OrdinalIgnoreCase);

    public FileTemplateLoader(string rootDir)
    {
        _rootDir = rootDir;
    }

    public string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName)
    {
        if (Path.IsPathRooted(templateName))
        {
            return templateName;
        }

        var normalized = templateName.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_rootDir, normalized);
    }

    public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath)
    {
        return LoadCached(templatePath);
    }

    public ValueTask<string> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
    {
        return ValueTask.FromResult(LoadCached(templatePath));
    }

    private string LoadCached(string templatePath)
    {
        var fileInfo = new FileInfo(templatePath);
        if (!fileInfo.Exists)
        {
            return string.Empty;
        }

        var signature = new FileSignature(fileInfo.LastWriteTimeUtc, fileInfo.Length);
        if (_cache.TryGetValue(templatePath, out var existing) && existing.Signature.Equals(signature))
        {
            return existing.Text;
        }

        var text = File.ReadAllText(templatePath);
        _cache[templatePath] = new CachedText(signature, text);
        return text;
    }

    private readonly record struct FileSignature(DateTime LastWriteTimeUtc, long Length);

    private sealed record CachedText(FileSignature Signature, string Text);
}
