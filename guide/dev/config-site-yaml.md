# 配置（site.yaml）字段参考

本文档是 `site.yaml` 的权威字段参考，来源于：
- 配置模型：`src/SiteGen.Config/AppConfig.cs`
- 加载逻辑：`src/SiteGen.Config/ConfigLoader.cs`
- 校验规则：`src/SiteGen.Config/ConfigValidator.cs`

示例配置：
- `examples/starter/site.yaml`

## 顶层结构

```yaml
site: {}
content: {}
build: {}
theme: {}
taxonomy: {}
logging: {}
```

## site

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|---|---:|---:|---|---|
| `site.name` | string | 是 | - | 站点内部标识 |
| `site.title` | string | 是 | - | 站点标题（模板变量 `site.title`） |
| `site.url` | string | 否 | null | 站点绝对 URL（用于 sitemap/rss）；必须以 `http://` 或 `https://` 开头 |
| `site.description` | string | 否 | null | 站点描述（模板变量 `site.description`） |
| `site.autoSummary` | bool | 否 | false | 未提供 `meta.summary` 时是否从正文提取摘要并回填 |
| `site.autoSummaryMaxLength` | int | 否 | 200 | 自动摘要最大长度（字符数） |
| `site.baseUrl` | string | 是 | `/` | GitHub Pages 子路径（例如 `/my-repo`）；必须以 `/` 开头 |
| `site.outputPathEncoding` | string | 否 | `none` | `none` \| `slug` \| `urlencode` \| `sanitize`（`sanitize`：空格替换为 `-`，移除 `<>:"|?*` 和控制字符，连续 `-` 压缩，段末 `.`/空格移除） |
| `site.language` | string | 否 | `zh-CN` | 单语言模式下的语言标识 |
| `site.languages` | string[] | 否 | null | 多语言输出（例如 `["zh-CN","en-US"]`）；不可重复 |
| `site.defaultLanguage` | string | 否 | `site.languages[0]` | 必须包含在 `site.languages` 中 |
| `site.sitemapMode` | string | 否 | `split` | `split` \| `merged` \| `index` |
| `site.rssMode` | string | 否 | `split` | `split` \| `merged` |
| `site.searchMode` | string | 否 | `split` | `split` \| `merged` \| `index` |
| `site.searchIncludeDerived` | bool | 否 | false | 是否把插件派生页纳入搜索索引（语义见 SearchIndex 插件） |
| `site.pluginFailMode` | string | 否 | `strict` | `strict`（插件失败中断构建）\| `warn`（记录错误继续） |
| `site.timezone` | string | 否 | `Asia/Shanghai` | 时间相关处理的默认时区（当前主要用于生成与展示策略） |

## content

content 支持两种模式：

1. 单一 provider：`content.provider: markdown|notion`
2. 多源组合：`content.provider: sources` + `content.sources[]`

### content.provider

| 值 | 说明 |
|---|---|
| `markdown` | 从本地 Markdown 加载内容 |
| `notion` | 从 Notion 数据库加载内容 |
| `sources` | 组合多个内容源（Notion/Markdown），并支持 mode=content/data |

### content.sources[]

当存在 `content.sources` 时，`content.provider` 会被视为 `sources`（即使未显式填写）。

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|---|---:|---:|---|---|
| `content.sources[].type` | string | 是 | - | `notion` \| `markdown` |
| `content.sources[].name` | string | 否 | null | 可选名称；若填写必须唯一 |
| `content.sources[].mode` | string | 否 | `content` | `content`（生成路由并渲染）\| `data`（不生成路由，只注入 `site.modules`） |
| `content.sources[].notion` | object | 视 type | - | type=notion 必填 |
| `content.sources[].markdown` | object | 视 type | - | type=markdown 必填 |

### content.notion / content.sources[].notion

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|---|---:|---:|---|---|
| `databaseId` | string | 是 | - | Notion Database ID |
| `pageSize` | int | 否 | 50 | Notion query page_size |
| `maxItems` | int | 否 | null | 最多拉取条数（正整数）；达到即停止 |
| `renderContent` | bool | 否 | null | 是否渲染正文；未设置时由内部策略决定（通常为 true） |
| `fieldPolicy.mode` | string | 否 | `whitelist` | `whitelist` \| `all` |
| `fieldPolicy.allowed` | string[] | 否 | null | whitelist 模式下允许进入 `page.fields` 的字段列表 |
| `filterProperty` | string | 否 | `Published` | 过滤字段名（配合 filterType） |
| `filterType` | string | 否 | `checkbox_true` | `checkbox_true` \| `none` |
| `sortProperty` | string | 否 | null | 排序字段名 |
| `sortDirection` | string | 否 | `ascending` | `ascending` \| `descending`（只有设置 sortProperty 才生效） |
| `includeSlugs` | string[] | 否 | null | 指定 slug 列表，仅拉取这些页面（数据库 query 过滤） |
| `includeSlugProperty` | string | 否 | `Slug` | includeSlugs 对应字段名（当前过滤使用 rich_text.equals） |
| `cacheMode` | string | 否 | `off` | `off` \| `readwrite` \| `readonly`（Notion 正文渲染缓存） |
| `cacheDir` | string | 否 | null | 缓存目录（相对 config 所在目录；不填时默认 `<rootDir>/.cache/notion`） |
| `renderConcurrency` | int | 否 | null | 正文渲染并发度（正整数；默认本地 4、CI 2） |
| `maxRps` | int | 否 | null | Notion 请求全局限速（正整数；默认 3，包含 query + blocks） |
| `maxRetries` | int | 否 | null | 429 最大重试次数（非负整数；遵循 Retry-After 退避） |

校验与运行时约束：
- Notion token 必须来自环境变量：`NOTION_TOKEN`（缺失会直接报错）
- filterType!=none 时，filterProperty 必须非空

运行时观测：
- Notion 内容源加载结束会输出 `event=notion.stats`，用于查看请求总数与限流等待情况：
  - `requests`：Notion HTTP 请求总数（包含 query、blocks、429 重试）
  - `throttle_wait_count` / `throttle_wait_ms`：因 `maxRps` 节流带来的等待次数/累计毫秒

### content.markdown / content.sources[].markdown

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|---|---:|---:|---|---|
| `dir` | string | 否 | `content` | Markdown 内容目录（相对 config 所在目录） |
| `defaultType` | string | 否 | `page` | 未指定 type 时的默认类型（page/post 等） |
| `maxItems` | int | 否 | null | 最多读取多少篇（正整数；按路径排序后截断） |
| `includePaths` | string[] | 否 | null | 只读取指定路径（相对 dir；可省略 `.md`） |
| `includeGlobs` | string[] | 否 | null | 只读取匹配的 glob（匹配相对路径，分隔符使用 `/`） |

## build

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|---|---:|---:|---|---|
| `build.output` | string | 是 | `dist` | 输出目录（相对 config 所在目录） |
| `build.clean` | bool | 否 | true | 构建前清理输出目录 |
| `build.draft` | bool | 否 | false | 是否渲染草稿（草稿规则见内容系统） |

## theme

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|---|---:|---:|---|---|
| `theme.name` | string | 否 | null | 主题名（与 `themes/<name>` 配合使用） |
| `theme.layouts` | string | 否 | `layouts` | 模板目录 |
| `theme.assets` | string | 否 | `assets` | 资源目录（会拷贝到输出的 `assets/`） |
| `theme.static` | string | 否 | `static` | 静态目录（会原样拷贝到输出根） |
| `theme.params` | object | 否 | null | 任意参数字典，注入模板变量 `site.params` |

## taxonomy

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|---|---:|---:|---|---|
| `taxonomy.template` | string | 否 | `pages/page.html` | taxonomy 派生页模板默认值（用于 index/term） |
| `taxonomy.indexTemplate` | string | 否 | null | taxonomy 索引页模板（例如 `/tags/`、`/categories/`）；为空时回退到 `taxonomy.template` |
| `taxonomy.termTemplate` | string | 否 | null | taxonomy 具体项页模板（例如 `/tags/<slug>/`、`/categories/<slug>/`）；为空时回退到 `taxonomy.template` |
| `taxonomy.kinds` | list | 否 | null | 通用化 taxonomy 定义列表；配置后将按列表循环生成任意 kind（不再仅限 tags/categories）。每项至少包含 `key`，可选 `kind/title/singularTitlePrefix/template/indexTemplate/termTemplate/indexEnabled` |
| `taxonomy.templates.tags.template` | string | 否 | null | tags 派生页默认模板（为空时回退到 `taxonomy.template`） |
| `taxonomy.templates.tags.indexTemplate` | string | 否 | null | tags 索引页模板（为空时回退到 `taxonomy.indexTemplate` 或 `taxonomy.templates.tags.template`） |
| `taxonomy.templates.tags.termTemplate` | string | 否 | null | tags 具体项页模板（为空时回退到 `taxonomy.termTemplate` 或 `taxonomy.templates.tags.template`） |
| `taxonomy.templates.categories.template` | string | 否 | null | categories 派生页默认模板（为空时回退到 `taxonomy.template`） |
| `taxonomy.templates.categories.indexTemplate` | string | 否 | null | categories 索引页模板（为空时回退到 `taxonomy.indexTemplate` 或 `taxonomy.templates.categories.template`） |
| `taxonomy.templates.categories.termTemplate` | string | 否 | null | categories 具体项页模板（为空时回退到 `taxonomy.termTemplate` 或 `taxonomy.templates.categories.template`） |
| `taxonomy.pageSize` | int | 否 | 10 | taxonomy term 页分页大小（分类/标签详情页） |
| `taxonomy.indexEnabled` | bool | 否 | true | 是否生成 taxonomy 索引页（例如 `/tags/`、`/categories/`） |

说明：
- 未配置 `taxonomy.kinds`：保持旧行为，仅生成 tags/categories 两套派生页。
- 配置了 `taxonomy.kinds`：按 kinds 列表生成任意 taxonomy；`taxonomy.kinds[]` 上的模板字段优先级最高。
  若 `kind` 为 `tags/categories`，`taxonomy.templates.<kind>.*` 仍作为 fallback（未在 kinds 内指定时才会生效）。

完整示例：

```yaml
taxonomy:
  template: pages/page.html
  indexTemplate: pages/taxonomy-index.html
  termTemplate: pages/taxonomy-term.html
  kinds:
    - key: tags
      kind: tags
      title: Tags
      singularTitlePrefix: Tag
      termTemplate: pages/tag.html
    - key: categories
      kind: categories
      title: Categories
      singularTitlePrefix: Category
      termTemplate: pages/category.html
    - key: series
      kind: series
      title: Series
      singularTitlePrefix: Series
      template: pages/series.html
  templates:
    tags:
      template: pages/tag.html
      indexTemplate: pages/tag-index.html
      termTemplate: pages/tag-term.html
    categories:
      template: pages/category.html
      indexTemplate: pages/category-index.html
      termTemplate: pages/category-term.html
```

优先级规则（从高到低）：
1. `taxonomy.templates.<kind>.indexTemplate` / `taxonomy.templates.<kind>.termTemplate`
2. `taxonomy.indexTemplate` / `taxonomy.termTemplate`
3. `taxonomy.templates.<kind>.template`
4. `taxonomy.template`
5. 默认 `pages/page.html`

## logging

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|---|---:|---:|---|---|
| `logging.level` | string | 否 | `info` | `debug` \| `info` \| `warn` \| `error`（CI 模式可能被提升为 warn） |
