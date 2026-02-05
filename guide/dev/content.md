# 内容系统（Markdown / Notion / sources）

本页描述内容系统的输入、输出与约定：ContentItem/Meta/Fields、Markdown Front Matter、Notion 字段归一化、以及 sources 组合模式。

实现参考：
- `src/SiteGen.Content/ContentItem.cs`
- `src/SiteGen.Content/ContentField.cs`
- `src/SiteGen.Content/Markdown/MarkdownFolderProvider.cs`
- `src/SiteGen.Content/Notion/NotionContentProvider.cs`
- `src/SiteGen.Engine/SiteEngine.cs`（mode=data 的处理与 modules 注入）

## 核心模型：ContentItem / Meta / Fields

### ContentItem

ContentItem 是引擎的统一输入模型，包含：
- `Id/Title/Slug/PublishAt/ContentHtml`
- `Meta`：影响引擎决策的元信息
- `Fields`：面向模板消费的自定义字段（`<key>.type/value`）

### Meta（引擎决策）

常见 Meta 键：
- `type`：`post` / `page`（影响默认路由与模板）
- `language`：多语言过滤（内容项的语言归属）
- `route` 或 `url/outputPath/template`：路由覆盖（见 [路由](./routing.md)）
- `source` / `sourcePath` / `notionPageId`：来源信息
- `sourceMode`：当启用 sources 时，用于区分 `content` / `data`

### Fields（模板消费）

模板使用统一入口：
- `page.fields.<key>.type`
- `page.fields.<key>.value`

字段 key 通常会被归一化（尤其是 Notion），建议模板与建模都使用“下划线小写”风格，例如 `seo_title`、`reading_time`。

## Markdown 模式

### 文件与目录

`content.provider: markdown` 时，从 `content.markdown.dir`（默认 `content/`）递归读取 `*.md` 文件。

### Front Matter

支持 YAML Front Matter：

```yaml
---
type: page
title: 关于我们
slug: about
publishAt: 2026-01-01T00:00:00Z
tags: [sitegen, starter]
categories: docs
summary: 一句话摘要
seo_title: 自定义 SEO 标题
---
```

规则要点（由 `MarkdownFolderProvider` 实现）：
- `title` 缺失时，会从正文的第一个 `# ` 标题提取；仍缺失则回退为 slug
- `slug` 默认是文件名（不含扩展名），可在 Front Matter 覆盖
- `publishAt` 可选，缺失则使用文件最后修改时间
- `tags/categories` 支持字符串（逗号分隔）或数组，会统一归一化为列表

### Reserved keys 与 Fields 构建

以下键会被视为保留键，不会作为一般字段进入 `page.fields`（但 tags/categories/summary 会以固定方式写入 fields）：
- `title/slug/type/publishAt/language/tags/categories/summary`
- `route/url/outputPath/template`

## Notion 模式

### 环境变量

Notion token 强制来自环境变量：
- `NOTION_TOKEN`

缺失会在配置校验阶段直接报错（`ConfigValidator`）。

### 固定字段（用于引擎决策）

Notion provider 会从数据库 properties 中解析以下字段（字段名大小写敏感，遵循 Notion UI 显示名）：
- `Title`（title）：标题
- `Slug`（rich_text 或 formula.string）：slug
- `Type`（select/multi_select）：类型（默认 `post`）
- `PublishAt` 或 `Date`（date）：发布时间（默认 now）
- 过滤与排序：由 `content.notion.filter*`、`content.notion.sort*` 控制

### 自定义字段进入 page.fields：fieldPolicy

Notion provider 会把 properties 映射为 `fields`，但会做两层筛选：

1. 保留字段剔除：`published/title/slug/type/publishat` 不会进入 fields
2. fieldPolicy 筛选：
   - `whitelist`：仅允许 `fieldPolicy.allowed` 中列出的字段
   - `all`：除保留字段外全部进入 fields

字段名归一化规则：把原字段名转换为“下划线小写 + 非字母数字变下划线”，例如：
- `SEO Title` → `seo_title`
- `PublishAt` → `publishat`

### 特殊字段提升到 Meta：language / i18nKey

Notion provider 会把以下 fields（若存在）提升到 Meta，供引擎做多语言与 alternates 关联：
- `language` → `meta.language`
- `i18n_key` / `i18nkey` → `meta.i18nKey`

## sources 组合模式（多数据库 + data 模块）

当你需要 Pages/Posts/Modules 多库组合时，使用：

```yaml
content:
  provider: sources
  sources:
    - type: notion
      name: pages
      mode: content
      notion:
        databaseId: "..."
    - type: notion
      name: modules
      mode: data
      notion:
        databaseId: "..."
        fieldPolicy: { mode: all }
```

mode 的语义：
- `content`：参与路由生成与页面渲染
- `data`：不生成路由；会被引擎分组、排序后注入 `site.modules`（见 [Modules](./modules-data.md)）

## 相关专题（dosc）

- Notion schema 与字段约定：[notion_schema.md](../../dosc/notion_schema.md)
- 企业官网内容建模（Pages/Posts/Modules）：[v2_4.md](../../dosc/v2_4.md)
- AI 建站（与内容模型结合）：[ai_guide.md](../../dosc/ai_guide.md)、[intent.md](../../dosc/intent.md)
