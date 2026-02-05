using System.Text;

namespace SiteGen.Cli.Commands;

public static class InitCommand
{
    public static Task<int> RunAsync(ArgReader reader)
    {
        var targetDir = reader.GetArg(1);
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            Console.Error.WriteLine("init requires a target directory.");
            return Task.FromResult(2);
        }

        var provider = (reader.GetOption("--provider") ?? "markdown").Trim().ToLowerInvariant();
        var templateName = (reader.GetOption("--template") ?? "minimal").Trim();

        var root = Path.GetFullPath(targetDir);
        Directory.CreateDirectory(root);

        var themeRoot = Path.Combine(root, "themes", "starter");

        Directory.CreateDirectory(Path.Combine(root, "content"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "assets"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "static"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "layouts", "layouts"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "layouts", "pages"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "layouts", "partials"));

        WriteFile(root, ".gitignore", "dist/\n.sitegen/\n");
        WriteFile(root, "README.md", $"# {Path.GetFileName(root)}\n\nPowered by sitegen\n");
        WriteFile(root, Path.Combine("content", "hello-world.md"), "# Hello World\n\n这是一个示例页面。\n");
        WriteFile(root, Path.Combine("themes", "starter", "assets", "style.css"), DefaultStyleCss);

        WriteFile(root, Path.Combine("themes", "starter", "layouts", "layouts", "base.html"), BaseLayout);
        WriteFile(root, Path.Combine("themes", "starter", "layouts", "partials", "header.html"), HeaderPartial);
        WriteFile(root, Path.Combine("themes", "starter", "layouts", "partials", "footer.html"), FooterPartial);
        WriteFile(root, Path.Combine("themes", "starter", "layouts", "pages", "page.html"), PageTemplate);
        WriteFile(root, Path.Combine("themes", "starter", "layouts", "pages", "post.html"), PostTemplate);
        WriteFile(root, Path.Combine("themes", "starter", "layouts", "pages", "index.html"), IndexTemplate);
        WriteFile(root, Path.Combine("themes", "starter", "layouts", "pages", "list.html"), ListTemplate);

        WriteFile(root, "site.yaml", BuildSiteYaml(provider, templateName));

        Console.WriteLine($"Initialized: {root}");
        return Task.FromResult(0);
    }

    private static void WriteFile(string rootDir, string relativePath, string content)
    {
        var path = Path.Combine(rootDir, relativePath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string BuildSiteYaml(string provider, string templateName)
    {
        if (provider == "notion")
        {
            return """
site:
  name: my-site
  title: My Site
  baseUrl: /
  language: zh-CN
  timezone: Asia/Shanghai

content:
  provider: notion
  notion:
    databaseId: xxxxx

build:
  output: dist
  clean: true

theme:
  name: starter
  layouts: layouts
  assets: assets
  static: static

logging:
  level: info
""";
        }

        return """
site:
  name: my-site
  title: My Site
  baseUrl: /
  language: zh-CN
  timezone: Asia/Shanghai

content:
  provider: markdown
  markdown:
    dir: content
    defaultType: page

build:
  output: dist
  clean: true

theme:
  name: starter
  layouts: layouts
  assets: assets
  static: static

logging:
  level: info
""";
    }

    private const string DefaultStyleCss = """
body {
  margin: 0;
  font-family: system-ui, -apple-system, Segoe UI, Roboto, Helvetica, Arial, "Noto Sans", "PingFang SC", "Microsoft YaHei", sans-serif;
  line-height: 1.6;
}

.container {
  max-width: 860px;
  margin: 0 auto;
  padding: 24px;
}

nav a {
  margin-right: 12px;
}

footer {
  margin-top: 32px;
  opacity: 0.8;
}
""";

    private const string BaseLayout = """
<!DOCTYPE html>
<html lang="{{ site.language }}">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>{{ page.title }} - {{ site.title }}</title>
  <link rel="stylesheet" href="{{ site.base_url }}/assets/style.css" />
</head>
<body>
  {{ include "partials/header.html" }}
  <main class="container">
    {{ content }}
  </main>
  {{ include "partials/footer.html" }}
</body>
</html>
""";

    private const string HeaderPartial = """
<header>
  <nav>
    <a href="{{ site.base_url }}/">首页</a>
    <a href="{{ site.base_url }}/blog/">博客</a>
    <a href="{{ site.base_url }}/pages/">页面</a>
  </nav>
</header>
""";

    private const string FooterPartial = """
<footer>
  <small>Powered by sitegen</small>
</footer>
""";

    private const string PageTemplate = """
{% layout "layouts/base.html" %}

<article>
  <h1>{{ page.title }}</h1>
  <div class="content">
    {{ page.content }}
  </div>
</article>
""";

    private const string PostTemplate = """
{% layout "layouts/base.html" %}

<article>
  <h1>{{ page.title }}</h1>
  {{ if page.publish_date }}
    <small>{{ page.publish_date | date.to_string "%Y-%m-%d" }}</small>
  {{ end }}
  <div class="content">
    {{ page.content }}
  </div>
</article>
""";

    private const string IndexTemplate = """
{% layout "layouts/base.html" %}

<h1>{{ site.title }}</h1>

<ul>
{{ for p in pages }}
  <li>
    <a href="{{ site.base_url }}{{ p.url }}">{{ p.title }}</a>
    {{ if p.publish_date }}
      <small>{{ p.publish_date | date.to_string "%Y-%m-%d" }}</small>
    {{ end }}
  </li>
{{ end }}
</ul>
""";

    private const string ListTemplate = """
{% layout "layouts/base.html" %}

<ul>
{{ for p in pages }}
  <li>
    <a href="{{ site.base_url }}{{ p.url }}">{{ p.title }}</a>
    {{ if p.publish_date }}
      <small>{{ p.publish_date | date.to_string "%Y-%m-%d" }}</small>
    {{ end }}
  </li>
{{ end }}
</ul>
""";
}
