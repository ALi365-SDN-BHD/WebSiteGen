# AOT 与非 AOT 构建模式

本项目同时支持 AOT 与非 AOT 两种编译/发布模式。它们不仅影响二进制发布形态，也会影响“插件加载方式”等运行行为，因此建议写入开发者文档。

## 两种模式分别是什么

- 非 AOT：常规 .NET 运行方式（JIT），适合开发调试与插件迭代
- AOT：Native AOT 发布（预编译为原生可执行文件），适合发布部署与启动性能/体积诉求

## 仓库内如何切换（权威来源）

`SiteGen.Cli.csproj` 定义了一个名为 `AOT` 的构建配置：
- 当 `Configuration == AOT` 时：`<PublishAot>true</PublishAot>` 且注入编译常量 `AOT`
- 该编译常量会在代码中触发条件编译（例如插件加载逻辑）

参考：`src/SiteGen.Cli/SiteGen.Cli.csproj`

## 行为差异：插件加载

插件注册的差异点在 `PluginRegistry`：

- AOT 模式下：只启用
  - built-in（内置插件）
  - generated（编译期生成的插件源）
- 非 AOT 模式下：除以上两类外，还会启用
  - external（运行时从 `<rootDir>/plugins/*.dll` 扫描加载）

含义：
- 如果你希望“把插件作为外部 DLL 丢进 plugins/ 目录就生效”，必须使用非 AOT 模式
- 如果你要发布 AOT 二进制并携带插件能力，插件应当以“编译期可见”的方式纳入（见下一节）

参考：`src/SiteGen.Engine/Plugins/PluginRegistry.cs`

## AOT 模式下如何纳入自定义插件

编译期插件发现来自源生成器：

当一个类型同时满足：
- 实现 `ISiteGenPlugin`
- 命名空间以 `SiteGen.Plugins.` 开头
- 标注 `[SiteGenPlugin]`

就会被源生成器加入 generated 插件源，从而在 AOT 中可用。

参考：
- `src/SiteGen.PluginSourceGenerator/PluginSourceGenerator.cs`
- `src/SiteGen.Engine.Abstractions/Plugins/SiteGenPluginAttribute.cs`

## 文档建议（写到哪里、写到什么程度）

建议最小写入：
- “如何选择”：开发调试用非 AOT，发布部署用 AOT
- “行为差异”：尤其是外部插件加载在 AOT 下不可用
- “扩展路径”：AOT 下插件要走编译期纳入（generated），非 AOT 可走 plugins/*.dll

本页作为开发者文档的稳定说明，避免后续维护者在“插件为什么加载不到”“发布后行为不一致”上反复踩坑。
