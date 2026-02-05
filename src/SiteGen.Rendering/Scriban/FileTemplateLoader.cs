using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace SiteGen.Rendering.Scriban;

public sealed class FileTemplateLoader : ITemplateLoader
{
    private readonly string _rootDir;

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
        return File.ReadAllText(templatePath);
    }

    public ValueTask<string> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
    {
        return ValueTask.FromResult(File.ReadAllText(templatePath));
    }
}

