# 架构与模块边界

本文档描述 SiteGen 的端到端构建链路、模块边界与关键数据结构，帮助维护者快速定位“改动应该落在哪一层”。

## 端到端数据流

```text
CLI (sitegen build/doctor/...) 
  └─ 解析参数 → 解析配置路径 → 加载 site.yaml
      └─ Config（Load + Validate + ApplyOverrides）
          └─ Engine.BuildAsync
              ├─ ContentProvider.LoadAsync（Markdown / Notion / sources 组合）
              ├─ Routing.Generate（为每个 ContentItem 生成 RouteInfo）
              ├─ Plugins.DerivePages（可派生额外页面路由）
              ├─ Rendering.Render（Scriban 模板渲染 HTML）
              ├─ Plugins.AfterBuild（sitemap/rss/search/taxonomy 等）
              └─ Output（dist + 资产 + 静态文件 + 可选 metrics/manifest）
```

## 代码模块划分（按 src 工程）

### SiteGen.Cli

职责：
- 命令解析与参数归一化
- 配置路径解析（`--config` / `--site` / 默认 `site.yaml`）
- 将 CLI 选项映射为配置覆盖（output/baseUrl/clean/draft/ci/incremental/cache-dir/metrics/log-format）

关键入口：
- `src/SiteGen.Cli/Program.cs`
- `src/SiteGen.Cli/Commands/*`
- `src/SiteGen.Cli/ConfigPathResolver.cs`

### SiteGen.Config

职责：
- `site.yaml` 的解析（类型化到 AppConfig）
- 配置字段默认值
- 配置校验与错误消息（作为“对外契约”）

关键入口：
- `src/SiteGen.Config/AppConfig.cs`
- `src/SiteGen.Config/ConfigLoader.cs`
- `src/SiteGen.Config/ConfigValidator.cs`
- `src/SiteGen.Config/ConfigOverrides.cs`

### SiteGen.Content

职责：
- 内容统一模型（`ContentItem`、`ContentField`）
- 内容加载：Markdown 文件夹、Notion 数据库、以及组合 sources 模式
- 字段/属性归一化：Meta（引擎决策）与 Fields（模板消费）

关键入口：
- `src/SiteGen.Content/ContentItem.cs`
- `src/SiteGen.Content/Markdown/MarkdownFolderProvider.cs`
- `src/SiteGen.Content/Notion/NotionContentProvider.cs`
- `src/SiteGen.Content/CompositeContentProvider.cs`

### SiteGen.Routing

职责：
- 将 ContentItem 转换为 `RouteInfo`（url/outputPath/template）
- 支持从 Meta 读取路由覆盖（route/url/outputPath/template）

关键入口：
- `src/SiteGen.Routing/RouteGenerator.cs`

### SiteGen.Rendering

职责：
- 渲染输入模型（SiteModel/PageModel/ListPageModel 等）
- Scriban 模板渲染（模板加载、模型绑定、输出 HTML）

关键入口：
- `src/SiteGen.Rendering/Models.cs`
- `src/SiteGen.Rendering/Scriban/*`

### SiteGen.Engine

职责：
- 构建主流程编排（清理输出、加载内容、分语言变体构建、渲染、资产拷贝、插件执行、metrics/manifest 输出）
- 增量构建（hash/manifest/skip 原因统计）
- i18n root 输出（sitemap/rss/search 的 merged/index 模式）

关键入口：
- `src/SiteGen.Engine/SiteEngine.cs`
- `src/SiteGen.Engine/Incremental/*`
- `src/SiteGen.Engine/Plugins/*`

### SiteGen.Engine.Abstractions

职责：
- 插件接口与构建上下文的稳定契约（对外扩展点）

关键入口：
- `src/SiteGen.Engine.Abstractions/Plugins/*`

### SiteGen.Shared

职责：
- 通用异常类型、日志接口/实现等基础能力

关键入口：
- `src/SiteGen.Shared/*`

## 最核心的数据结构

- ContentItem：内容加载后的统一结构；引擎只认它
- Meta：影响路由/构建策略的元信息（type/language/route/sourceMode...）
- Fields：面向主题与模板的“自定义字段”（fields.<key>.type/value）
- RouteInfo：路由决策的结果（url/outputPath/template）
- BuildContext：插件运行上下文（config/rootDir/outputDir/baseUrl/routed/derived...）

## 维护原则（避免架构腐化）

- 对外契约优先：配置字段名、校验错误文案、CLI 参数都是用户会依赖的稳定接口，改动需谨慎
- 单向依赖：Cli → Config/Engine；Engine → Content/Routing/Rendering；插件只通过 Abstractions 访问上下文
- 明确职责边界：
  - Content 负责“把内容变成 ContentItem”
  - Routing 负责“ContentItem → RouteInfo”
  - Rendering 负责“模型 → HTML”
  - Engine 负责“编排与 IO”
  - Plugins 负责“可插拔扩展”
