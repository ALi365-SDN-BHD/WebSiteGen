# SiteGen 开发者文档（维护与扩展）

本目录面向后续开发者与维护者，目标是把“稳定契约（配置/参数/数据模型）”与“内部实现细节（构建流水线/增量/插件加载）”讲清楚，保证可快速上手、可稳定迭代、可安全扩展。

## 最短上手路径

1. 先跑通示例站点（命令与参数见 [CLI](./cli.md)）
2. 看懂 `site.yaml` 的字段与校验规则（见 [配置](./config-site-yaml.md)）
3. 理解端到端数据流：Config → Content → Routing → Rendering → Plugins → Output（见 [架构](./architecture.md)）

## 目录导航

- [架构与模块边界](./architecture.md)
- [命令行（CLI）参数参考](./cli.md)
- [配置（site.yaml）字段参考](./config-site-yaml.md)
- [init/create（脚手架初始化）](./init-create.md)
- [内容系统（Markdown / Notion / sources）](./content.md)
- [路由系统（默认规则与覆盖）](./routing.md)
- [渲染与模板（Scriban 模型/变量/目录约定）](./rendering-scriban.md)
- [主题开发（Themes）与参数使用](./theme.md)
- [Modules 数据源（mode=data → site.modules）](./modules-data.md)
- [引擎固定产物（不依赖内容的输出）](./engine-outputs.md)
- [插件体系（derive-pages / after-build）](./plugins.md)
- [内置插件（BuiltIn）产物与边界](./built-in-plugins.md)
- [Intent（意图文件）CLI 落地](./intent-cli.md)
- [AOT 与非 AOT 构建模式](./aot.md)
- [发布与部署（Publish / Deploy）](./publish-deploy.md)
- [增量构建（manifest / cache-dir / render-skip 原因）](./incremental-build.md)
- [缓存与清理（cache-dir / .cache / clean）](./cache-clean.md)
- [doctor（环境与配置自检）](./doctor.md)
- [可观测性（日志与 metrics）](./observability.md)
- [多语言与 SEO（sitemap/rss/search 模式）](./i18n-seo.md)
- [Webhook（触发器与安全约束）](./webhook.md)
- [验收与冒烟（smoke/acceptance）](./testing-smoke.md)

## 仓库内的“其他文档”如何使用

仓库已有 `dosc/` 目录，包含更偏“产品/方案/验收”的专题沉淀，例如：Notion schema、v2.x 规划与验收、企业官网 Modules 建模建议等。

本目录（guide/dev）会在相关章节中给出指向 `dosc/` 的链接，避免重复维护两份内容。

常用专题入口：

- AI 建站指南：[ai_guide.md](../../dosc/ai_guide.md)
- Intent 契约与映射规则：[intent.md](../../dosc/intent.md)
- Notion schema 与字段模板：[notion_schema.md](../../dosc/notion_schema.md)、[notion_database_template.csv](../../dosc/notion_database_template.csv)
- 企业官网 Modules 建模建议：[v2_4.md](../../dosc/v2_4.md)
- 验收文档：[v2_1_acceptance.md](../../dosc/v2_1_acceptance.md)、[v2_2_acceptance.md](../../dosc/v2_2_acceptance.md)

## 概念速览（10 秒）

- ContentItem：统一内容数据结构（来自 Markdown 或 Notion）
- Meta vs Fields：Meta 用于引擎决策（type/language/route...），Fields 用于模板消费（page.fields.*.value）
- mode=content / mode=data：content 参与路由与渲染；data 不生成路由，只注入 `site.modules`
- 插件：两类生命周期钩子（派生页 derive-pages、构建后 after-build）
