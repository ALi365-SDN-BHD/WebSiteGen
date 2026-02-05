# 插件体系（derive-pages / after-build）

插件是 SiteGen 的主要扩展点。它允许在不修改引擎主流程的前提下增加：派生页面、构建后生成附加产物（sitemap/rss/search/taxonomy 等）。

实现参考：
- 插件接口：`src/SiteGen.Engine.Abstractions/Plugins/*`
- 执行器：`src/SiteGen.Engine/Plugins/PluginRunner.cs`
- 注册与加载：`src/SiteGen.Engine/Plugins/PluginRegistry.cs`
- 插件源生成：`src/SiteGen.PluginSourceGenerator/PluginSourceGenerator.cs`

## 生命周期与能力边界

### 1) DerivePages（派生页）

接口：`IDerivePagesPlugin.DerivePages(BuildContext)`

作用：
- 基于现有的 routed 内容，派生额外页面（例如 tags/categories 列表页）
- 派生页返回 `(ContentItem, RouteInfo, LastModified)`，会进入渲染队列

注意：
- 派生页的 `RouteInfo` 应避免与已有路由冲突
- 派生页可被纳入 sitemap/rss/search（取决于对应插件与配置策略）

### 2) AfterBuild（构建后）

接口：`IAfterBuildPlugin.AfterBuild(BuildContext)`

作用：
- 在所有页面渲染完成后，生成附加文件（例如 sitemap.xml、rss.xml、search.json 等）

## 失败策略：site.pluginFailMode

插件执行器遵循 `site.pluginFailMode`：
- `strict`：插件抛错会中断构建
- `warn`：记录错误并继续后续插件

实现点：`PluginRunner` 中会根据 failMode 决定是否 rethrow。

## 插件发现与加载方式

插件来源分三类（`PluginRegistry`）：

1. built-in：内置插件（引擎自带）
2. generated：编译期生成的插件源（用于 AOT 与内置插件开发）
3. external：运行时加载 `plugins/*.dll`（非 AOT 模式才启用）

关于 AOT 与非 AOT 的行为差异（尤其是 external 插件加载在 AOT 下不可用），见 [AOT 与非 AOT 构建模式](./aot.md)。

### generated（编译期发现规则）

源生成器会扫描满足以下条件的类型并生成 `GeneratedPluginSource`：

- 实现 `ISiteGenPlugin`
- 命名空间以 `SiteGen.Plugins.` 开头
- 标注 `[SiteGenPlugin]` 特性

这意味着：如果你在仓库内开发插件，推荐放在 `src/plugins/` 对应工程中，并遵循上述命名空间与特性要求。

### external（运行时加载）

非 AOT 模式下，引擎会扫描 `<rootDir>/plugins/*.dll`：

- 加载程序集
- 遍历可加载类型
- 找出实现 `ISiteGenPlugin` 的非抽象类型
- 通过无参构造 `Activator.CreateInstance` 实例化

## 内置插件一览（BuiltIn）

内置插件当前包括（见 `BuiltInPluginSource`）：

- `taxonomy`：根据 meta.tags/meta.categories 派生 `/tags/` 与 `/categories/`（IDerivePagesPlugin）
- `sitemap`：生成 sitemap.xml（IAfterBuildPlugin）
- `rss`：生成 rss.xml（IAfterBuildPlugin）
- `search-index`：生成 search.json 等（IAfterBuildPlugin）
- `pagination`：分页类派生/输出（视实现）
- `archive`：归档类派生/输出（视实现）

说明：
- 具体输出策略与文件名以各插件实现为准：`src/SiteGen.Engine/Plugins/BuiltIn/*`
- 输出契约与多语言边界的汇总见 [内置插件（BuiltIn）产物与边界](./built-in-plugins.md)。

## 插件开发建议（契约优先）

- 把“对外可配置项”放进 `site.yaml` 的稳定字段（或以 `theme.params` 形式注入模板）
- 插件输出的 URL 与 outputPath 建议固定规则，并与 baseUrl/i18n 兼容
- 插件执行耗时会被记录到 metrics（若启用 `--metrics`），便于 CI 性能回归
