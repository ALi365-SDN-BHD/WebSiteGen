# sitegen（.NET 10 Native AOT 静态站点引擎）

一个面向“笔记即 CMS”的静态网站引擎：内容可来自 Notion（或本地 Markdown），在 GitHub Actions 中自动构建并部署到 GitHub Pages。

## 文档

- 普通用户使用文档：[`guide/user`](guide/user/README.md)
- 开发者/维护者文档：[`guide/dev`](guide/dev/README.md)

## 快速开始（使用仓库内示例站点）

先验证端到端构建是否正常：

```bash
dotnet build sitegen.slnx -c Release
dotnet run --project src/SiteGen.Cli -c Release -- doctor --config examples/starter/site.yaml
dotnet run --project src/SiteGen.Cli -c Release -- build --config examples/starter/site.yaml --clean --site-url https://example.com
dotnet run --project src/SiteGen.Cli -c Release -- preview --dir examples/starter/dist --port auto
```

浏览器打开：

```
控制台会输出实际的 Preview URL（端口可能不同）
```

## 命令行

### 初始化站点

```bash
dotnet run --project src/SiteGen.Cli -c Release -- create my-site
```

Notion 模式：

```bash
dotnet run --project src/SiteGen.Cli -c Release -- create my-site --provider notion
```

### 构建

默认读取当前目录 `site.yaml`：

```bash
dotnet run --project src/SiteGen.Cli -c Release -- build --clean
```

输出构建指标（JSON）与结构化日志（便于 CI 采集）：

```bash
dotnet run --project src/SiteGen.Cli -c Release -- build --clean --metrics metrics.json --log-format json
```

并行渲染（可加速大站点构建；默认使用 CPU 核心数）：

```bash
dotnet run --project src/SiteGen.Cli -c Release -- build --clean --jobs 8
```

多站点（读取 `sites/<name>.yaml`，但 rootDir 仍为当前目录）：

```bash
dotnet run --project src/SiteGen.Cli -c Release -- build --site blog --clean
```

覆盖输出目录与 baseUrl：

```bash
dotnet run --project src/SiteGen.Cli -c Release -- build --output dist --base-url /my-repo --site-url https://user.github.io/my-repo --clean
```

### 诊断

```bash
dotnet run --project src/SiteGen.Cli -c Release -- doctor --config site.yaml
```

### 清理

```bash
dotnet run --project src/SiteGen.Cli -c Release -- clean --dir dist
```

### 主题

列出工程根目录下的 `themes/<name>`：

```bash
dotnet run --project src/SiteGen.Cli -c Release -- theme list --config site.yaml
```

写回配置（设置 `theme.name`）：

```bash
dotnet run --project src/SiteGen.Cli -c Release -- theme use alt --config site.yaml
```

### Webhook（触发 GitHub Actions dispatch）

用于 Notion webhook → GitHub `repository_dispatch` 的触发器（不影响核心引擎）：

```bash
dotnet run --project src/SiteGen.Cli -c Release -- webhook --repo owner/repo --port 8787 --path /webhook/notion --event sitegen_notion
```

环境变量要求：

- `SITEGEN_WEBHOOK_TOKEN`（入站请求头 `X-Sitegen-Token`）
- `SITEGEN_GITHUB_TOKEN`（或 `GITHUB_TOKEN`）

## 配置（site.yaml）

参考：[examples/starter/site.yaml](file:///c:/Users/AliSer/Documents/trae_projects/ssp/examples/starter/site.yaml)

关键字段：

- `site.baseUrl`：GitHub Pages 子路径（例如 `/my-repo`），根站点用 `/`
- `site.url`：站点绝对域名（用于 sitemap/rss），也可通过 `--site-url` 覆盖
- `site.pluginFailMode`：插件失败策略（`strict` 默认中断构建；`warn` 仅记录错误继续）
- `site.autoSummary`：未提供 summary 时是否从正文提取摘要（用于 taxonomy/rss/search 等）
- `site.autoSummaryMaxLength`：自动摘要最大长度（字符数）
- `content.provider`：`markdown` 或 `notion`
- `content.markdown.maxItems`：最多读取多少篇 Markdown（正整数）
- `content.markdown.includePaths/includeGlobs`：只读取指定的 Markdown（用于大仓库/单篇调试）
- `content.notion.maxItems`：最多拉取多少条 Notion 页面（正整数）
- `content.notion.includeSlugs`：只拉取 slug 在列表中的页面（数据库 query 过滤）
- `content.notion.cacheMode/cacheDir`：Notion 渲染缓存（off/readwrite/readonly）
- `content.notion.renderConcurrency/maxRps/maxRetries`：Notion 并发渲染与限流/重试（提升大库渲染速度与稳定性）
- 构建结束会输出 `event=notion.stats`：Notion 请求数与节流等待统计（便于评估吞吐与瓶颈）
- `build.output`：输出目录（相对 `site.yaml` 所在目录）
- `theme.layouts/assets/static`：模板、资源与静态文件目录（相对 `site.yaml` 所在目录）

## 模板自定义字段（v2）

v2 支持在模板中读取“自定义字段”，统一入口是：

- `page.fields.<key>.value`
- `page.fields.<key>.type`（text/number/date/list/bool/file）

### Markdown（Front Matter）

在 Markdown 文件的 Front Matter 中新增字段即可（示例）：

```yaml
---
type: page
seo_title: 关于 - SiteGen 示例站点
reading_time: 5
tags:
  - sitegen
---
```

模板中使用（注意：本项目模板语法使用 Scriban 的 `{{ if ... }}` 形式）：

```scriban
<title>
  {{ if page.fields.seo_title }}
    {{ page.fields.seo_title.value }}
  {{ else }}
    {{ page.title }}
  {{ end }}
  - {{ site.title }}
</title>
```

### Notion（fieldPolicy）

Notion 内容源会按 `content.notion.fieldPolicy` 决定哪些 properties 会进入 `page.fields`：

```yaml
content:
  provider: notion
  notion:
    databaseId: "<your_database_id>"
    fieldPolicy:
      mode: whitelist   # whitelist | all
      allowed:
        - cover
        - seo_title
        - seo_desc
        - reading_time
        - my_link
```

模板示例：

```scriban
{{ if page.fields.cover }}
  <img src="{{ page.fields.cover.value }}" />
{{ end }}
```

## v2 验收与测试

当前仓库主要采用“可运行验收（smoke/acceptance）”方式覆盖核心链路（build/doctor/i18n/sitemap/rss/taxonomy/multi-site/webhook）。推荐两种方式：

- 一键 smoke（本地）：[smoke.ps1](file:///c:/Users/AliSer/Documents/trae_projects/ssp/scripts/smoke.ps1) / [smoke.sh](file:///c:/Users/AliSer/Documents/trae_projects/ssp/scripts/smoke.sh)
- 分项验收文档：
  - v2.1（P1）：[v2_1_acceptance.md](file:///c:/Users/AliSer/Documents/trae_projects/ssp/dosc/v2_1_acceptance.md)
  - v2.2+（P2）：[v2_2_acceptance.md](file:///c:/Users/AliSer/Documents/trae_projects/ssp/dosc/v2_2_acceptance.md)

## AI 建站（v2）

- 对话式建站指南：[ai_guide.md](file:///c:/Users/AliSer/Documents/trae_projects/ssp/dosc/ai_guide.md)
- Intent 契约与映射规则：[intent.md](file:///c:/Users/AliSer/Documents/trae_projects/ssp/dosc/intent.md)

## Notion 内容源

### 环境变量

Notion Token 只允许通过环境变量注入：

- `NOTION_TOKEN`

### 数据库字段约定（v1）

`content.provider: notion` 模式下，默认按以下字段解析：

- `Published`（checkbox）：是否发布；仅发布内容会被渲染
- `Title`（title）：标题
- `Slug`（rich_text 或 formula/string）：URL slug（缺省会从 Title 生成）
- `Type`（select 或 multi_select）：`post`/`page`（缺省 `post`）
- `PublishAt`（date，可选）：发布时间（缺省当前时间）

v2 的字段模板与 schema 说明见：

- [notion_schema.md](file:///c:/Users/AliSer/Documents/trae_projects/ssp/dosc/notion_schema.md)
- [notion_database_template.csv](file:///c:/Users/AliSer/Documents/trae_projects/ssp/dosc/notion_database_template.csv)

## GitHub Actions + GitHub Pages

仓库已包含 GitHub Pages 工作流：[pages.yml](file:///c:/Users/AliSer/Documents/trae_projects/ssp/.github/workflows/pages.yml)

要启用部署：

1. 在 GitHub 仓库 Settings → Pages → Build and deployment 选择 “GitHub Actions”
2. 如使用 Notion：在仓库 Settings → Secrets and variables → Actions 添加 `NOTION_TOKEN`
3. 推送到 `main` 分支，工作流会用 .NET 10 Native AOT 发布并运行 `sitegen build`，再部署到 Pages

## AOT 发布（本地）

示例（linux-x64）：

```bash
dotnet publish src/SiteGen.Cli -c AOT -r linux-x64 -o out/sitegen
```

Windows 示例（win-x64）：
```bash
dotnet publish src/SiteGen.Cli -c AOT -r win-x64 -o out/sitegen
dotnet publish src/SiteGen.Cli -c Release -r win-x64 -o out/sitegen /p:PublishSingleFile=true /p:SelfContained=true
```
