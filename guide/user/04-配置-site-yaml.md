# 04 配置（site.yaml）：字段说明、默认行为与常见写法

`site.yaml` 是你的站点“控制面板”。你可以把它理解成：**内容从哪来、输出到哪、用什么主题、额外生成哪些文件**。

本页面向普通用户，按“最常用场景”解释字段；如果你需要权威字段表与校验细节，请看开发者文档：[guide/dev/config-site-yaml](../dev/config-site-yaml.md)。

## 覆盖优先级（非常重要）

同一个配置项，最终生效的优先级从高到低是：

1. CLI 参数（例如 `--output/--base-url/--site-url/--clean/--draft`）
2. `site.yaml`
3. 引擎默认值

常见误解：你改了 `site.yaml`，但 CLI 里仍然带着 `--output dist2`，所以“看起来没生效”。

## 最小可用配置（Markdown）

```yaml
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
  name: alt
logging:
  level: info
```

对照可运行示例：`examples/starter/site.yaml`。

## 顶层块：site / content / build / theme / logging

### site：站点级信息（SEO、多语言、插件策略都在这里）

常用字段（用户最常改的）：

| 字段 | 作用 | 常见示例 |
|---|---|---|
| `site.name` | 站点内部标识（建议全小写、短） | `starter` |
| `site.title` | 展示标题（用于模板/SEO） | `SiteGen Starter` |
| `site.baseUrl` | 站点部署子路径（GitHub Pages 常用） | `/` 或 `/my-repo` |
| `site.url` | 站点绝对域名（用于 sitemap/rss） | `https://user.github.io/my-repo` |
| `site.language` | 默认语言 | `zh-CN` |
| `site.languages` | 多语言列表（启用 i18n） | `[zh-CN, en-US]` |
| `site.defaultLanguage` | 多语言下的默认语言 | `zh-CN` |
| `site.timezone` | 时区（影响日期展示与一些默认行为） | `Asia/Shanghai` |
| `site.pluginFailMode` | 插件失败策略 | `strict` / `warn` |
| `site.autoSummary` | 未提供 summary 时是否从正文提取摘要 | `true` / `false` |
| `site.autoSummaryMaxLength` | 自动摘要最大长度（字符数） | `200` |

与输出相关的模式（多语言时很关键）：

| 字段 | 作用 | 常见值 |
|---|---|---|
| `site.sitemapMode` | sitemap 输出模式 | `merged` / `split` / `index` |
| `site.rssMode` | rss 输出模式 | `merged` / `split` / `index` |
| `site.searchMode` | search 输出模式 | `merged` / `split` / `index` |

这些模式怎么选见：[11-多语言与SEO](./11-多语言与SEO.md)。

### site：自动摘要（可选）

当文章没有提供 `summary` 时，可以开启“自动摘要”从正文内容提取一段纯文本作为摘要，并写入 `meta.summary`，因此 taxonomy/RSS/search.json/模板里读取 `summary` 都能拿到值。

```yaml
site:
  autoSummary: true
  autoSummaryMaxLength: 200
```

### content：内容来源（Markdown / Notion / 多源）

你只能选一种 provider：

- `markdown`：从本地文件夹读取 Markdown
- `notion`：从 Notion 数据库读取
- `sources`：组合多个来源（推荐用在 pages + posts + modules 分库）

#### provider=markdown

```yaml
content:
  provider: markdown
  markdown:
    dir: content
    defaultType: page
```

| 字段 | 作用 | 说明 |
|---|---|---|
| `content.markdown.dir` | Markdown 根目录 | 递归读取 `*.md` |
| `content.markdown.defaultType` | 未声明 type 时默认类型 | 常用 `page` |
| `content.markdown.maxItems` | 最多读取多少篇 | 正整数；用于大仓库限额 |
| `content.markdown.includePaths` | 只读取指定路径 | 相对 `content.markdown.dir`；可省略 `.md` |
| `content.markdown.includeGlobs` | 只读取匹配的 glob | 匹配相对路径，分隔符使用 `/` |

Markdown 内容写法见：[05-内容-Markdown](./05-内容-Markdown.md)。

#### provider=notion

```yaml
content:
  provider: notion
  notion:
    databaseId: "xxxx"
    pageSize: 50
    filterProperty: Published
    filterType: checkbox_true
    sortProperty: PublishAt
    sortDirection: descending
    fieldPolicy:
      mode: whitelist
      allowed:
        - seo_title
        - seo_desc
        - cover
```

| 字段 | 作用 | 说明 |
|---|---|---|
| `content.notion.maxItems` | 最多拉取多少条 | 正整数；用于大库限额 |
| `content.notion.includeSlugs` | 只拉取指定 slug | 数据库 query 过滤（便于单篇调试） |
| `content.notion.includeSlugProperty` | includeSlugs 对应字段 | 默认 `Slug`；建议 rich_text |
| `content.notion.cacheMode` | Notion 渲染缓存模式 | `off`/`readwrite`/`readonly` |
| `content.notion.cacheDir` | 缓存目录 | 相对 config 所在目录；不填时默认 `<rootDir>/.cache/notion` |
| `content.notion.renderConcurrency` | 正文渲染并发度 | 正整数；默认本地 4、CI 2 |
| `content.notion.maxRps` | Notion 请求全局限速 | 正整数；默认 3（包含数据库 query + blocks children） |
| `content.notion.maxRetries` | 429 最大重试次数 | 非负整数；遵循 `Retry-After` 退避 |

Notion 模式的前提：

- 必须设置环境变量 `NOTION_TOKEN`（严禁写进仓库文件）

详细见：[06-内容-Notion](./06-内容-Notion.md)。

#### provider=sources（多源组合，支持 mode=data）

```yaml
content:
  provider: sources
  sources:
    - type: markdown
      name: pages
      mode: content
      markdown:
        dir: content
        defaultType: page
    - type: markdown
      name: modules
      mode: data
      markdown:
        dir: data
        defaultType: module
```

关键点：

- `mode: content` 的源会生成路由与页面
- `mode: data` 的源不会生成路由，会注入 `site.modules`（详见：[09-Modules-结构化数据](./09-Modules-结构化数据.md)）

### build：输出目录与构建策略

| 字段 | 作用 | 常见示例 |
|---|---|---|
| `build.output` | 输出目录 | `dist` |
| `build.clean` | 构建前是否清理输出目录 | `true` |
| `build.draft` | 是否渲染草稿内容 | `false`（默认） |

等价的 CLI 参数：

- `--output <dir>` 覆盖 `build.output`
- `--clean/--no-clean` 覆盖 `build.clean`
- `--draft` 覆盖 `build.draft`

### theme：主题位置与参数

最推荐的写法是只指定 `theme.name`，主题目录放在 `themes/<name>/`：

```yaml
theme:
  name: alt
  params:
    brand: my-site
```

如果你不使用 themes 目录，也可以显式指定各目录：

```yaml
theme:
  layouts: layouts
  assets: assets
  static: static
```

主题与模板变量见：[08-主题与模板](./08-主题与模板.md)。

### logging：日志等级（一般不用频繁改）

```yaml
logging:
  level: info
```

CI 场景下建议配合 `--log-format json`，便于收集与排查（见：[12-命令行参考](./12-命令行参考.md)）。

## 常见配置场景（可直接抄）

### 1）GitHub Pages 子路径（baseUrl）

如果站点部署在 `https://user.github.io/my-repo/`，那么：

- `site.baseUrl` 应该是 `/my-repo`
- `site.url` 应该是 `https://user.github.io/my-repo`

构建命令示例：

```bash
dotnet run --project src/SiteGen.Cli -c Release -- build --clean --base-url /my-repo --site-url https://user.github.io/my-repo
```

### 2）多语言最小配置

```yaml
site:
  language: zh-CN
  languages: [zh-CN, en-US]
  defaultLanguage: zh-CN
```

对照示例：`examples/starter/site.i18n.yaml`。

### 3）Modules（data）最小配置

```yaml
content:
  provider: sources
  sources:
    - type: markdown
      name: content
      mode: content
      markdown: { dir: content, defaultType: page }
    - type: markdown
      name: modules
      mode: data
      markdown: { dir: data, defaultType: module }
```

对照示例：`examples/starter/site.modules.yaml` 与 `examples/starter/data/*.md`。

## 常见坑（快速自查）

- `site.url` 没设：sitemap/rss 的链接可能不正确（可以用 `--site-url` 覆盖）
- `site.baseUrl` 配错：GitHub Pages 打开后资源 404（CSS/JS/图片路径错）
- 相对路径基准搞错：`dir: content` 不是相对命令行所在目录，而是相对 `site.yaml` 所在目录
- Notion token 写进 YAML：不允许且不安全，必须用 `NOTION_TOKEN` 环境变量
