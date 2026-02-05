using Scriban;
using Scriban.Runtime;
using SiteGen.Shared;

namespace SiteGen.Rendering.Scriban;

public sealed class ScribanTemplateRenderer
{
    private readonly string _layoutsDir;

    public ScribanTemplateRenderer(string layoutsDir)
    {
        _layoutsDir = layoutsDir;
    }

    public string RenderPage(string templateRelativePath, PageModel model)
    {
        var globals = ScribanModelBinder.ToScriptObject(model);
        return Render(templateRelativePath, globals);
    }

    public string RenderList(string templateRelativePath, ListPageModel model)
    {
        var globals = ScribanModelBinder.ToScriptObject(model);
        return Render(templateRelativePath, globals);
    }

    private string Render(string templateRelativePath, ScriptObject globals)
    {
        var templatePath = Path.Combine(_layoutsDir, templateRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(templatePath))
        {
            throw new RenderException($"Template not found: {templateRelativePath}");
        }

        var templateText = File.ReadAllText(templatePath);
        if (TryExtractLayoutDirective(templateText, out var layoutTemplateRelativePath, out var bodyTemplateText))
        {
            var body = RenderTemplateText(templateRelativePath, templatePath, bodyTemplateText, globals);
            globals.SetValue("content", body, readOnly: true);
            return Render(layoutTemplateRelativePath, globals);
        }

        return RenderTemplateText(templateRelativePath, templatePath, templateText, globals);
    }

    private string RenderTemplateText(string templateRelativePath, string templatePath, string templateText, ScriptObject globals)
    {
        var template = Template.Parse(templateText, templatePath);
        if (template.HasErrors)
        {
            throw new RenderException(template.Messages.ToString());
        }

        var context = new TemplateContext
        {
            TemplateLoader = new FileTemplateLoader(_layoutsDir),
            EnableRelaxedMemberAccess = true,
            EnableRelaxedTargetAccess = true,
            EnableNullIndexer = true
        };

        context.PushGlobal(globals);

        try
        {
            return template.Render(context);
        }
        catch (Exception ex)
        {
            throw new RenderException($"Render failed: {templateRelativePath}", ex);
        }
    }

    private static bool TryExtractLayoutDirective(string templateText, out string layoutTemplateRelativePath, out string bodyTemplateText)
    {
        layoutTemplateRelativePath = string.Empty;
        bodyTemplateText = templateText;

        var lines = templateText.ReplaceLineEndings("\n").Split('\n').ToList();

        var firstContentLineIndex = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                firstContentLineIndex = i;
                break;
            }
        }

        if (firstContentLineIndex < 0)
        {
            return false;
        }

        var firstLine = lines[firstContentLineIndex].Trim();
        if (!TryParseLayoutLine(firstLine, out layoutTemplateRelativePath))
        {
            return false;
        }

        lines.RemoveAt(firstContentLineIndex);
        bodyTemplateText = string.Join('\n', lines);
        return true;
    }

    private static bool TryParseLayoutLine(string line, out string layoutTemplateRelativePath)
    {
        layoutTemplateRelativePath = string.Empty;

        if (TryParseDirective("{%", "%}", line, out var inner) || TryParseDirective("{{", "}}", line, out inner))
        {
            if (!inner.StartsWith("layout", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rest = inner["layout".Length..].Trim();
            if (TryExtractQuotedString(rest, out var path))
            {
                layoutTemplateRelativePath = path;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseDirective(string open, string close, string line, out string inner)
    {
        inner = string.Empty;
        if (!line.StartsWith(open, StringComparison.Ordinal) || !line.EndsWith(close, StringComparison.Ordinal))
        {
            return false;
        }

        inner = line.Substring(open.Length, line.Length - open.Length - close.Length).Trim();
        return true;
    }

    private static bool TryExtractQuotedString(string text, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var q1 = text.IndexOf('"');
        var q2 = q1 < 0 ? text.IndexOf('\'') : -1;
        var quote = q1 >= 0 ? '"' : q2 >= 0 ? '\'' : '\0';
        var start = q1 >= 0 ? q1 : q2;
        if (start < 0)
        {
            return false;
        }

        var end = text.IndexOf(quote, start + 1);
        if (end <= start)
        {
            return false;
        }

        value = text.Substring(start + 1, end - start - 1);
        return !string.IsNullOrWhiteSpace(value);
    }
}
