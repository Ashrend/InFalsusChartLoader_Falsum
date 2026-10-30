# `.falsum` 为底、`if` 补展示信息（overlay）设计

- 日期：2026-10-30
- 状态：待确认
- 版本影响：无（不改 `if` / `.falsum` 格式，不升版本号）

## 意图

制谱器导出的 `.falsum` 不带预览区间这类展示信息。同一个歌曲目录里同时存在 `.falsum` 和 `if` 时，以 `.falsum` 为谱面事实来源，`if` 只用来覆盖或补充展示信息；单独存在的 `if` 或 `.falsum` 行为不变。用户放 `if` 就代表“这次要改/加东西”，所以 `if` 没有写的字段保持 `.falsum` 的值。

## 目标

- 两者同时存在：`.falsum` 提供歌曲身份、音频、四难度谱面与可用性；`if` 覆盖展示字段。
- 现有 `if` 文件格式不变：overlay 读的是同一套键，只是允许只写一部分。
- 没有 `.falsum` 的目录继续走原有严格 `if` 解析，行为与今天一致。
- 合并结果继续通过 `ModCore.Validate` 的谱面校验。
- 合并后没有任何曲绘（`.falsum` 没有 `cover` 且 `if` 也没给 `jacket`）的歌曲跳过，不导入、不注册。

## 非目标

- 不允许 `if` 覆盖 `id`、`song`、`chart`。
- 不新增文件格式或文件名，不改 `.falsum` 编译器。
- 不修改 `JacketCatalog` 的通用空值处理；改为在导入阶段直接拒绝没有曲绘的歌曲。
- 不处理 `pack_cover.png`（当前 `CoversEnabled = false`）。
- 不升版本号；需要时单独做。

## 反目标

- falsum-only / if-only 目录的行为发生变化。
- 展示型 `if` 导致 `.falsum` 的谱面或音频丢失。
- Release 产物新增需要 allowlist 的诊断串；新增的可失败文本放在 `#if DEBUG` 的 `reason` 里。

## 方案

**A. 新增 `Source/Charts/IfOverlay.cs`：只解析展示键的宽松解析器 + 纯 `Apply(ChartInfo)`。**
`ChartInfoReader.TryRead` 在两者都存在时先 `FalsumReader.TryRead` 拿到基准 `ChartInfo`，再读 overlay 并应用；其它路径保持现状。
理由：复用用户已经打算放置的 `if`，严格解析路径零改动，overlay 不依赖 Unity，可以直接进 `PackRulesTests`。

B. 给现有 `ChartInfoReader` 的每个 helper 穿 `strict` 开关。放弃：要改十来个方法和每个校验点，严格路径容易在后续改动中变松。

C. 另起一个只做覆盖的文件名（如 `if.meta`）。放弃：用户明确要求复用现有 `if`。

## 合并规则

| 字段 | 来源 |
|---|---|
| `BaseName`（id） | falsum `songId`；overlay 的 `id` 忽略 |
| `SongPath` | falsum 音频；overlay 的 `song` 忽略 |
| `ChartPaths` / `ChartNames` / `ChartBytes` / `NoChart` | falsum；overlay 的 `chart` 忽略 |
| `Name` | falsum `title`；overlay `name` 覆盖 |
| `Composer` | falsum `artist`；overlay `composer` 覆盖 |
| `PreviewStart` / `PreviewEnd` | falsum 计算值；overlay `preview_seconds` 覆盖 |
| `JacketPaths` | falsum `cover`；overlay `jacket` 覆盖（1 个或 4 个） |
| `BackgroundPaths` | falsum `background`；overlay `background` 覆盖（1 个或 4 个，数组里的 null = 该难度无背景） |
| `Illustrators` | falsum `jacketDesigner`；overlay `illust` 覆盖（数组里的 null = 该难度不显示画师） |
| `Charters[d]` | falsum `chartDesigner`；为空且有谱面时用 overlay `charter[d]` |
| `Level[d]` | falsum `rating`；为 null 且有谱面时用 overlay `lv[d]` |

`charter` / `lv` 写成单值时只作用于 Forbidden（索引 3），与现有 `if` 的 `Slots` 语义一致；`jacket` / `illust` / `background` 写单值时作用于四个难度。

## 解析与失败策略

- overlay 只接受这八个键：`name`、`composer`、`preview_seconds`、`illust`、`jacket`、`background`、`charter`、`lv`；其余键忽略。
- 每个键的取值规则与现有 `if` 完全一致：`jacket`/`background` 的文件必须在本目录且扩展名合法；`preview_seconds` 两个值；`illust`/`background` 允许 null 占位等。
- `if` 不是合法 JSON 对象，或某个展示键取值非法：整首歌失败并给出原因。和今天严格 `if` 一致，避免“悄悄只应用一半”。
- 两者都存在但 falsum 解析失败：回退到原有严格 `if` 路径（保持今天“if 能用”的行为），Debug 日志说明原因。
- overlay 没写任何展示键：照常加载 falsum，Debug 日志说明。

## 影响文件

- 新增 `Source/Charts/IfOverlay.cs`。
- 修改 `Source/Charts/ChartInfo.cs`（`HasJackets()`）。
- 修改 `Source/Charts/ChartInfoReader.cs`（路由、overlay 应用、无曲绘跳过）。
- 修改 `Source/Charts/FalsumReader.cs`（新增 `HasPackage` 探测）。
- 测试：`Tests/PackRulesTests/Program.cs`，并链接 `IfOverlay.cs`、`ChartInfo.cs`。
- 文档：`docs/IF.EXAMPLE.md` 增加“与 `.falsum` 同目录时”一节，README 补一句。

## 验证

- `build.bat` 四种配置全部通过（含 Release 产物检查）。
- `PackRulesTests` 新增 overlay 用例全绿。
- 游戏内手工验证：falsum-only、if-only、两者共存（只补预览 / 补曲绘+画师+背景 / 补 `lv`+`charter`）各一次。
- 无 `cover` 的 `.falsum`：单独放时被跳过；旁边补一个带 `jacket` 的 `if` 后正常导入。

## 无曲绘歌曲的处理

`.falsum` 的 `cover` 是可选的；没有 `cover` 时 `JacketPaths` 是四个 `null`，现有 `JacketCatalog.Add` 会把它当字典键并抛异常。按“跳过不注册”处理：读取/合并完成后检查四个难度是否都有 jacket，缺任意一个就跳过该歌曲（Debug 给出原因），不再进入 `JacketCatalog`。`if`-only 路径本来就要求 `jacket`，行为不变。

判定放在 `ChartInfo` 上的一个小查询（`HasJackets()`），这样测试项目可以直接链接并覆盖。

## 留给实现者

- 测试用例的具体数量与命名。
- overlay 解析与现有 helper 的重复代码是否在后续合并（本次先复制到新文件，保证严格路径不动）。
