# 增量构建（manifest / cache-dir / render-skip 原因）

增量构建用于在模板与内容未变化时跳过渲染，提高本地与 CI 构建速度。

实现参考：
- `src/SiteGen.Engine/SiteEngine.cs`（增量主逻辑）
- `src/SiteGen.Engine/Incremental/BuildManifest.cs`
- `src/SiteGen.Engine/Incremental/HashUtil.cs`

## 开关与目录

- 默认：启用增量构建
- CLI：
  - `--incremental` / `--no-incremental`
  - `--cache-dir <dir>`（默认 `<rootDir>/.cache`）
  - `--jobs <n>`：控制渲染并行度（默认 CPU 核心数；与增量判定独立）

缓存目录与 clean 的关系见：[缓存与清理](./cache-clean.md)。

## manifest 文件

manifest 用于记录“上次渲染时的指纹”，默认路径：
- 单语言：`<cacheDir>/build-manifest.json`
- 多语言：`<cacheDir>/build-manifest.<lang>.json`（例如 `build-manifest.zh-CN.json`）

## 跳过渲染的判定条件

某个页面可跳过渲染需同时满足：

1. 增量开关开启
2. manifest 存在且包含该页面的 entry
3. 输出文件存在
4. 三个 hash 都一致：
   - `TemplateHash`：模板目录内容 hash（layoutsDir）
   - `ContentHash`：内容指纹（ContentItem 的关键字段 + fields + ContentHtml）
   - `RouteHash`：路由指纹（url/outputPath/template）

补充：

- 首页与列表页（例如 `index.html`、`blog/index.html`、`pages/index.html`）也会写入 manifest，并参与增量判定。

## renderReasons（诊断意义）

当需要渲染时，引擎会记录原因统计（写入 metrics 时可见）：
- `new_page`：manifest 中不存在
- `output_missing`：输出文件不存在
- `template_changed`：模板 hash 变化
- `content_changed`：内容指纹变化
- `route_changed`：路由指纹变化
- `full_render`：关闭增量时的全量渲染

当页面被跳过/或是列表页的增量判定时，可能还会看到：

- `unchanged`：内容页命中增量缓存而跳过
- `list_render`：列表页需要重渲染
- `list_unchanged`：列表页命中增量缓存而跳过

## 常见问题与排查

1. “改了模板但没生效”
   - 确认模板目录是否指向了预期 layouts（见 `theme.layouts`）
   - 确认未在多个 layoutsDir 间切换导致误判
2. “本地渲染很慢”
   - 确认未使用 `--no-incremental`
   - 检查 cache-dir 是否可写
3. “多语言增量缓存互相污染”
   - 设计上按语言 suffix 分离 manifest；检查 language 值是否被非预期覆盖
