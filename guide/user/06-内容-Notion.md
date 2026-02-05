# 06 内容（Notion）：把 Notion 当 CMS 的完整配置与示例

如果你希望“写作与编辑”发生在 Notion，而不是仓库里，那么 Notion 模式可以让你把 Notion 数据库当作 CMS，构建时自动拉取并渲染。

本页会讲清楚：需要哪些 Notion 字段、如何过滤/排序、如何把自定义字段传到模板、以及常见的 token/权限问题。

深入的字段归一化规则与开发者契约见：[guide/dev/content](../dev/content.md) 与 `dosc/notion_schema.md`。

## 你将获得什么

- Notion 数据库推荐字段（可直接照着建）
- 一份可复制的 `site.yaml`（Notion 模式）
- 一张“模拟数据库数据表”（方便理解每列的含义）
- 常见报错与修复（token、databaseId、字段类型不匹配）

## 前置条件与安全要求

### 1）必须设置环境变量 NOTION_TOKEN

Notion token **只能通过环境变量注入**，不能写进 `site.yaml`（也不应写进仓库任何文件）。

Windows PowerShell（当前会话）示例：

```powershell
$env:NOTION_TOKEN="secret_xxx"
```

GitHub Actions 里建议用仓库 Secrets（见：[13-部署-GitHub-Pages](./13-部署-GitHub-Pages.md)）。

### 2）Notion 集成（Integration）需要访问你的数据库

你需要在 Notion 创建一个 Integration，并把目标数据库分享给该 Integration，否则会出现“无权限/找不到数据库”等错误。

## 最小配置（Notion provider）

```yaml
content:
  provider: notion
  notion:
    databaseId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
```

建议你从“最小配置”开始跑通，再逐步增加 filter/sort/fieldPolicy。

## 推荐的数据库字段（建议按这个建）

以下字段名以 Notion UI 显示名为准，大小写敏感（建议直接复制粘贴字段名）。

### 引擎决策字段（强建议具备）

| 字段名 | 类型 | 作用 |
|---|---|---|
| `Published` | checkbox | 是否发布（建议只渲染已发布内容） |
| `Title` | title | 内容标题 |
| `Slug` | rich_text 或 formula(string) | URL slug（缺省可由 Title 生成，但建议显式稳定） |
| `Type` | select 或 multi_select | `page`/`post`（缺省常为 post） |
| `PublishAt` | date | 发布时间（缺省可用当前时间，但建议显式） |

### 多语言相关字段（可选，但建议）

| 字段名 | 类型 | 作用 |
|---|---|---|
| `language` | rich_text / select | 内容语言（例如 `zh-CN`/`en-US`） |
| `i18n_key` | rich_text | 跨语言内容关联的稳定 key（例如 `about`, `pricing`） |

### 模板自定义字段（按需）

你可以添加任意字段作为“模板字段”，例如：

| 字段名 | 类型 | 模板用途 |
|---|---|---|
| `SEO Title` | rich_text | `page.fields.seo_title.value` |
| `SEO Desc` | rich_text | `page.fields.seo_desc.value` |
| `cover` | files / url | 封面图 |
| `reading_time` | number | 阅读时长 |

## 模拟数据（示例数据库表）

下面是一份“模拟数据”，帮助你理解一条 Notion 页面会如何变成站点内容（你可以在 Notion 里照着录入几条测试）。

| Published | Title | Slug | Type | PublishAt | language | i18n_key | SEO Title | tags | categories |
|---|---|---|---|---|---|---|---|---|---|
| ✅ | 关于我们 | about | page | 2026-01-01 | zh-CN | about | 关于我们 - My Site | company,intro | docs |
| ✅ | About | about | page | 2026-01-01 | en-US | about | About - My Site | company,intro | docs |
| ✅ | 第一篇博客 | first-post | post | 2026-01-10 | zh-CN | blog_first | 第一篇博客 - My Site | release,roadmap | updates |
| ⬜ | 未发布草稿 | draft-1 | post | 2026-01-20 | zh-CN | draft_1 | 草稿 - My Site | draft | draft |

说明：

- `Published` 用于构建过滤，避免草稿上站
- `language + i18n_key` 用于多语言站点内容关联（可选）
- `SEO Title` 等自定义字段需要 `fieldPolicy` 允许进入模板（见下一节）

## 过滤与排序（filter / sort）

### 只渲染已发布内容

```yaml
content:
  provider: notion
  notion:
    databaseId: "..."
    filterProperty: Published
    filterType: checkbox_true
```

### 按发布时间倒序

```yaml
content:
  provider: notion
  notion:
    databaseId: "..."
    sortProperty: PublishAt
    sortDirection: descending
```

## fieldPolicy：哪些 Notion 字段会进入 page.fields

Notion 的 properties 很多时候并不都需要进入模板。你可以用 `fieldPolicy` 控制：

### 白名单（推荐：可控、安全、模板更稳定）

```yaml
content:
  provider: notion
  notion:
    databaseId: "..."
    fieldPolicy:
      mode: whitelist
      allowed:
        - seo_title
        - seo_desc
        - cover
        - reading_time
```

### 全量（调试方便，但字段变化更容易影响模板）

```yaml
content:
  provider: notion
  notion:
    databaseId: "..."
    fieldPolicy:
      mode: all
```

字段名会做归一化（例如 `SEO Title` → `seo_title`），所以建议：

- Notion 侧字段名尽量稳定
- 模板侧统一用“下划线小写”访问（`page.fields.seo_title`）

## 常见错误与修复

### 1）报错：缺少 NOTION_TOKEN

现象：`doctor` 或 `build` 在配置校验阶段直接失败。

修复：

- 本地：设置环境变量 `NOTION_TOKEN`
- CI：在 GitHub Actions Secrets 中添加 `NOTION_TOKEN`，并注入到工作流环境

### 2）报错：databaseId 无效 / 无权限

现象：构建时拉取失败、提示 Notion API 相关错误。

修复清单：

- databaseId 是否是“数据库”的 ID，不是页面 URL
- Integration 是否被分享到了这个数据库（Notion 数据库右上角 Share）
- token 是否属于同一个 workspace/有权限

### 3）报错：字段类型不匹配

例如你把 `PublishAt` 建成了文本，而配置里用它排序。

修复：

- 按本页推荐字段类型创建（date/checkbox/select 等）
- 或调整 `filterProperty/sortProperty` 指向真实类型一致的字段
