# 内置插件（BuiltIn）产物与边界

本页描述内置插件的“输出契约”（会生成哪些文件/页面、依赖哪些配置、在多语言下如何表现）。当你修改插件或依赖它们的主题时，应当优先维护本页以避免行为漂移。

内置插件实现目录：`src/SiteGen.Engine/Plugins/BuiltIn/`

相关文档：
- [插件体系](./plugins.md)
- [多语言与 SEO](./i18n-seo.md)
- [引擎固定产物](./engine-outputs.md)

## sitemap（IAfterBuildPlugin）

文件：`SitemapPlugin.cs`

- 输出：`<outputDir>/sitemap.xml`
- 依赖：必须配置 `site.url`（否则直接跳过不生成）
- 包含路由：
  - 引擎固定页：`/`、`/blog/`、`/pages/`
  - 所有 routed 内容页
  - 所有 derived 路由（来自 derive-pages 插件，例如 taxonomy/pagination/archive）

多语言行为：
- 当 `site.languages` 非空且 `site.sitemapMode == merged`：该插件会在语言子目录跳过生成（由引擎在根目录生成 merged sitemap）
- 其他模式：各语言输出目录各自生成 `sitemap.xml`

## rss（IAfterBuildPlugin）

文件：`RssPlugin.cs`

- 输出：`<outputDir>/rss.xml`
- 依赖：必须配置 `site.url`（否则直接跳过不生成）
- 输入：只使用 routed 内容（不包含 derived）

多语言行为：
- 当 `site.languages` 非空且 `site.rssMode == merged`：该插件会在语言子目录跳过生成（由引擎在根目录生成 merged rss）
- 其他模式：各语言输出目录各自生成 `rss.xml`

## search-index（IAfterBuildPlugin）

文件：`SearchIndexPlugin.cs`

- 输出：`<outputDir>/search.json`
- 依赖：不依赖 `site.url`（可在纯相对链接站点使用）
- 内容字段：
  - `id/title/url/content/type/tags/categories/language/sourceKey/publishAt`
- `url` 生成规则：用 `site.baseUrl` 拼接页面 `route.url`（结果为站内路径）

是否包含派生页：
- 由 `site.searchIncludeDerived` 控制：
  - false：只索引 routed
  - true：索引 routed + derived

多语言行为：
- 每个语言变体目录都会生成各自的 `search.json`
- 如果 `site.searchMode == index`，引擎会在根目录额外生成 `search.index.json`（聚合指向各语言索引）

## taxonomy（IDerivePagesPlugin）

文件：`TaxonomyPlugin.cs`

根据内容的 `meta.tags` / `meta.categories` 派生页：

- `/tags/` → `tags/index.html`
- `/tags/<slug>/` → `tags/<slug>/index.html`
- `/categories/` → `categories/index.html`
- `/categories/<slug>/` → `categories/<slug>/index.html`

说明：
- 派生页使用模板：默认 `pages/page.html`
- 可配置：`taxonomy.template` / `taxonomy.indexTemplate` / `taxonomy.termTemplate`
- 支持按 kind 覆盖：`taxonomy.templates.tags.*` / `taxonomy.templates.categories.*`
- 优先级：kind 级别 index/term > 全局 index/term > kind 级别 template > 全局 template > 默认 `pages/page.html`
- 页面内容为插件生成的简单 HTML（ul/li 列表），仍会写入 `page.content`（兼容旧主题）
- 同时注入结构化字段（便于主题直接渲染列表，而不是解析 HTML）：
  - index 页（`/tags/`、`/categories/`）：`page.fields.terms.type == "list"`，`page.fields.terms.value[]` 为 `{ title, slug, url, count }`
- term 页（`/tags/<slug>/`、`/categories/<slug>/`）：
  - `page.fields.items.type == "list"`，`page.fields.items.value[]` 为 `{ title, url, publish_date, summary? }`
  - `page.fields.taxonomy.value` 为 `{ kind, term, slug, count }`
  - `page.fields.pagination.value` 为 `{ page, page_size, total, total_pages, has_prev, has_next }`
- slug 规则：字母数字保留，其余压缩为 `-`（小写）
- term 页支持分页路由：`/<kind>/<slug>/page/<n>/`（pageSize 由 `taxonomy.pageSize` 控制）
- taxonomy 索引页可禁用：`taxonomy.indexEnabled=false`（或 `taxonomy.kinds[].indexEnabled=false`）

模板示例（taxonomy term 页分页）：
```scriban
{% layout "layouts/base.html" %}

<article>
  <h1>{{ page.title }}</h1>
  <ul>
  {{ for item in page.fields.items.value }}
    <li>
      <a href="{{ site.base_url }}{{ item.url }}">{{ item.title }}</a>
      {{ if item.publish_date }}
        <small>{{ item.publish_date | date.to_string "%Y-%m-%d" }}</small>
      {{ end }}
    </li>
  {{ end }}
  </ul>

  <nav class="pagination">
    {{ if page.fields.pagination.value.has_prev }}
      <a href="{{ site.base_url }}/{{ page.fields.taxonomy.value.kind }}/{{ page.fields.taxonomy.value.slug }}/page/{{ page.fields.pagination.value.page - 1 }}/">Prev</a>
    {{ end }}
    <span>Page {{ page.fields.pagination.value.page }} / {{ page.fields.pagination.value.total_pages }}</span>
    {{ if page.fields.pagination.value.has_next }}
      <a href="{{ site.base_url }}/{{ page.fields.taxonomy.value.kind }}/{{ page.fields.taxonomy.value.slug }}/page/{{ page.fields.pagination.value.page + 1 }}/">Next</a>
    {{ end }}
  </nav>
</article>
```

## pagination（IDerivePagesPlugin）

文件：`PaginationPlugin.cs`

当 blog 文章数 > 10 时，派生分页：

- `/blog/page/2/` → `blog/page/2/index.html`
- …直到最后一页

说明：
- 派生页使用模板：`pages/page.html`
- 页面内容由插件生成（包含 Prev/Next 链接）
- pageSize 当前固定为 10（如需可配置化，应当把参数纳入稳定配置字段）

## archive（IDerivePagesPlugin）

文件：`ArchivePlugin.cs`

按年月派生归档页：

- `/blog/archive/` → `blog/archive/index.html`
- `/blog/archive/<year>/` → `blog/archive/<year>/index.html`
- `/blog/archive/<year>/<month>/` → `blog/archive/<year>/<month>/index.html`

说明：
- 派生页使用模板：`pages/page.html`
- 页面内容由插件生成（ul/li 列表，年页链接到月页，月页链接到文章）
