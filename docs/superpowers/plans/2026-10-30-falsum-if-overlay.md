# `.falsum` + `if` overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 同一歌曲目录同时存在 `.falsum` 和 `if` 时，以 `.falsum` 为谱面/音频基准，用 `if` 覆盖展示信息；没有曲绘的歌曲直接跳过。

**Architecture:** 新增一个不依赖 Unity 的 `IfOverlay` 类型，只解析展示键并合并到 `FalsumReader` 产出的 `ChartInfo`；`ChartInfoReader.TryRead` 增加“两者都存在”的分支并做曲绘门槛检查。严格 `if` 路径保持原样。测试项目链接读取器源码，用 `TESTS` 的日志 sink 和一个 `ChartDecryptor` 测试桩跑真实目录/zip 场景。

**Tech Stack:** .NET 6, C# unsafe, System.Text.Json, System.IO.Compression, 现有 `PackRulesTests` 控制台测试。

**Spec:** `docs/superpowers/specs/2026-10-30-falsum-if-overlay-design.md`

## Global Constraints

- 不提交、不推送：`AGENTS.md` 要求用户明确要求才提交；每个任务在检查全绿后停下。
- 不升版本号：`InFalsusChartLoader_falsum.csproj` 与 `Source/Mod/ModCore.cs` 的版本保持 `0.3.0`。
- 新失败原因只写在 `#if DEBUG` 里；Release 产物不得新增需要 allowlist 的诊断串。
- `IfOverlay.cs` 不得引用 `Diagnostics`、Unity 或 IL2CPP 类型。
- 测试项目只链接 `ChartDecryptor` 的测试桩，不链接真实 `ChartDecryptor.cs` / `Memory.cs`（它们会拖进 IL2CPP）。
- `if`-only 与 `.falsum`-only 的行为保持不变。
- 四个构建配置与 `Tests/PackRulesTests` 必须通过。

## Review Focus

1. `.falsum` 没有 `cover`、旁边也没有 `if`：整首跳过，不能抛异常（Task 2 测试 + Task 3 集成测试）。
2. `.falsum` 没有 `cover`、`if` 提供 `jacket`：正常导入（Task 1 合并测试 + Task 3 集成测试）。
3. `if` 只写展示键（没有 `id`/`song`/`chart`）：仍能基于 `.falsum` 导入（Task 3 集成测试）。
4. `if` 的展示键写坏（文件不存在、类型不对）：该曲跳过并给出原因，不能只应用一半（Task 1 + Task 3 测试）。
5. `.falsum` 坏掉但 `if` 完整：回退到严格 `if`，行为与今天一致（Task 3 集成测试）。

## File Structure

- 新增 `Source/Charts/IfOverlay.cs`：展示键的宽松解析 + 合并，纯托管、可测试。
- 修改 `Source/Charts/ChartInfo.cs`：加 `HasJackets()` 判定。
- 修改 `Source/Charts/FalsumReader.cs`：加 `HasPackage(folder)`。
- 修改 `Source/Charts/ChartInfoReader.cs`：两者共存的分支、合并、无曲绘跳过。
- 修改 `Source/Diagnostics/Log.cs`：加 `TESTS` 无操作 sink。
- 新增 `Tests/PackRulesTests/ChartDecryptorStub.cs`：只提供 `FalsumChartCompiler` 需要的三个方法。
- 修改 `Tests/PackRulesTests/PackRulesTests.csproj` 与 `Program.cs`：链接源码并加用例。
- 修改 `docs/IF.EXAMPLE.md`、`README.md`、`AGENTS.md`：补行为说明。

---

### Task 1: `IfOverlay` 解析与合并

**Files:**
- Create: `Source/Charts/IfOverlay.cs`
- Modify: `Tests/PackRulesTests/PackRulesTests.csproj`
- Modify: `Tests/PackRulesTests/Program.cs`

**Interfaces:**
- Consumes: `ChartInfo` 的 `Name`、`Composer`、`PreviewStart`、`PreviewEnd`、`Illustrators`、`JacketPaths`、`BackgroundPaths`、`Charters`、`Level`、`NoChart`。
- Produces:

```csharp
internal sealed class IfOverlay
{
    internal string Name;                 // null = 未给出
    internal string Composer;             // null = 未给出
    internal bool HasPreview;
    internal float PreviewStart;
    internal float PreviewEnd;
    internal string[] Illustrators;       // null = 未给出；显式 null 槽位存 string.Empty
    internal string[] Jackets;            // null = 未给出；四个都是绝对路径
    internal string[] Backgrounds;        // null = 未给出；显式 null 槽位保持 null
    internal string[] Charters;           // null = 未给出；单值只写索引 3
    internal int?[] Levels;               // null = 未给出；单值只写索引 3
    internal bool Any { get; }            // 是否至少应用了一个字段

    internal static bool TryRead(string folder, out IfOverlay overlay, out string reason);
    internal void Apply(ChartInfo info);
}
```

解析规则（键与现有 `if` 完全一致，缺省允许）：`name`/`composer` 非空字符串；`preview_seconds` 两个数字或数字字符串；`illust` 一个字符串或四元数组（null 槽位 → `string.Empty`）；`jacket` 一个或四个 `.png`，文件必须在该目录；`background` 一个或四个 `.png`/`.mp4`，允许 null 槽位；`charter` 一个或四个字符串，单值只写索引 3；`lv` 一个或四个正整数（不接受小数/指数），单值只写索引 3。`id`、`song`、`chart` 与未知键忽略。JSON 用 `CommentHandling = Skip`、`AllowTrailingCommas = true`；根必须是对象。

`Apply`：非 null 字段直接覆盖 `info` 的对应字段；`Charters`/`Levels` 只在 `!info.NoChart[d]` 且 `info.Charters[d]` 为空、`info.Level[d]` 为 null 时回填。若 `info.Charters`/`info.Level` 为 null，先分配长度 4 的数组。

- [ ] **Step 1: 把 `IfOverlay.cs` 与 `ChartInfo.cs` 链进测试项目**

在 `Tests/PackRulesTests/PackRulesTests.csproj` 的 `ItemGroup` 里补：

```xml
<Compile Include="..\..\Source\Charts\ChartInfo.cs" Link="ChartInfo.cs" />
<Compile Include="..\..\Source\Charts\IfOverlay.cs" Link="IfOverlay.cs" />
```

- [ ] **Step 2: 写会失败的测试**

在 `Program.cs` 里加 `OverlayTests()`，并在 `Main` 的 `Environment.ExitCode` 之前调用。至少覆盖：

```csharp
string folder = TempFolder();
File.WriteAllBytes(Path.Combine(folder, "j.png"), new byte[] { 1 });
File.WriteAllBytes(Path.Combine(folder, "b.png"), new byte[] { 1 });
string json = "{\"name\":\"New\",\"composer\":\"New Artist\",\"preview_seconds\":[5,30],"
    + "\"illust\":[\"A\",null,\"B\",\"C\"],\"jacket\":\"j.png\","
    + "\"background\":[null,null,\"b.png\",null],\"charter\":[\"a\",\"b\",\"c\",\"d\"],"
    + "\"lv\":[1,2,3,4],\"id\":\"ignored\",\"song\":\"ignored.wav\","
    + "\"chart\":\"ignored.spc\",\"future\":1}";
File.WriteAllText(Path.Combine(folder, "if"), json);

bool parsed = IfOverlay.TryRead(folder, out IfOverlay overlay, out string why);
Check("overlay parses", parsed, why);
Check("overlay any", overlay != null && overlay.Any);
Check("overlay name", overlay?.Name == "New");
Check("overlay preview", overlay != null && overlay.HasPreview && overlay.PreviewStart == 5f && overlay.PreviewEnd == 30f);
Check("overlay illust null slot", overlay?.Illustrators?[1] == string.Empty);
Check("overlay jacket all four", overlay != null && overlay.Jackets[0] == Path.Combine(folder, "j.png") && overlay.Jackets[3] == Path.Combine(folder, "j.png"));
Check("overlay background null slot", overlay != null && overlay.Backgrounds[0] == null && overlay.Backgrounds[2] == Path.Combine(folder, "b.png"));
Check("overlay charter four", overlay?.Charters?[3] == "d");
```

再加三个小用例：单值 `"charter": "solo"` 只写索引 3 且其余为 null；`"jacket": "missing.png"` 让 `TryRead` 返回 false 且 `why` 非空；`"lv": [1.5, 2, 3, 4]` 返回 false。

合并用例：先 `new ChartInfo()` 填 falsum 形状（`NoChart` 全 false、`Charters = {"", "", "", "D"}`、`Level = {null, null, null, 15}`、其余字段给旧值），再 `overlay.Apply(info)`，断言 `Name`/`Composer`/`Preview*`/`JacketPaths`/`BackgroundPaths`/`Illustrators` 被覆盖，`Charters[0] == "a"`、`Charters[3] == "D"`、`Level[0] == 1`、`Level[3] == 15`。再另起一个 `ChartInfo`，先设 `NoChart[2] = true` 再 `Apply`，断言 `Charters[2]` 保持为空。

`TempFolder()` 用 `Path.Combine(Path.GetTempPath(), "ifcl-tests", Guid.NewGuid().ToString("N"))` 创建目录，用例结束 `Directory.Delete(folder, true)`。

- [ ] **Step 3: 运行测试确认失败**

Run: `dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj`
Expected: 编译失败，`IfOverlay` 不存在（CS0246）。

- [ ] **Step 4: 实现 `IfOverlay`**

按 Interfaces 与解析规则实现；文件内自带私有 helper（`ReadText`、`ReadOneOrFour`、`ReadIllustrators`、`ReadPreview`、`ReadCharters`、`ReadLevels`、`ReadLocalFile`），不要改 `ChartInfoReader`。所有 `reason` 赋值放在 `#if DEBUG` 里。

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj`
Expected: 新增用例全部 PASS，原 21 条仍 PASS。

---

### Task 2: `ChartInfo.HasJackets()`

**Files:**
- Modify: `Source/Charts/ChartInfo.cs`
- Modify: `Tests/PackRulesTests/Program.cs`

**Interfaces:**
- Produces: `internal bool HasJackets()` — `JacketPaths` 非 null 且四个槽位都非空才为 true。

- [ ] **Step 1: 写会失败的测试**

```csharp
Check("null jackets rejected", !new ChartInfo().HasJackets());
Check("one missing jacket rejected", !new ChartInfo { JacketPaths = new[] { "a.png", null, "c.png", "d.png" } }.HasJackets());
Check("four jackets accepted", new ChartInfo { JacketPaths = new[] { "a.png", "b.png", "c.png", "d.png" } }.HasJackets());
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj`
Expected: 编译失败，`HasJackets` 不存在。

- [ ] **Step 3: 实现**

在 `ChartInfo` 里加 `HasJackets()`，用 `string.IsNullOrEmpty` 判断每个槽位。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj`
Expected: 三条新用例 PASS。

---

### Task 3: 读取路由与无曲绘跳过

**Files:**
- Modify: `Source/Charts/FalsumReader.cs`
- Modify: `Source/Charts/ChartInfoReader.cs`
- Modify: `Source/Diagnostics/Log.cs`
- Create: `Tests/PackRulesTests/ChartDecryptorStub.cs`
- Modify: `Tests/PackRulesTests/PackRulesTests.csproj`
- Modify: `Tests/PackRulesTests/Program.cs`

**Interfaces:**
- Consumes: `IfOverlay.TryRead`/`Apply`（Task 1）、`ChartInfo.HasJackets()`（Task 2）。
- Produces: `internal static bool FalsumReader.HasPackage(string folder)`；`ChartInfoReader.TryRead` 签名不变。

- [ ] **Step 1: 给 `Diagnostics` 加测试 sink**

在 `Log.cs` 的 `Sink` 里，`#elif BEPINEX` 之后、`#else` 之前加：

```csharp
#elif TESTS
    internal static void Msg(string message) { }
    internal static void Error(string message) { }
    internal static void Warning(string message) { }
```

- [ ] **Step 2: 加 `ChartDecryptor` 测试桩**

新建 `Tests/PackRulesTests/ChartDecryptorStub.cs`（测试项目专用；不要链接真实的 `ChartDecryptor.cs`）：

```csharp
namespace InFalsusChartLoader_falsum
{
    /// <summary>Test stub: readers only need Compile to return bytes; the real decryptor pulls IL2CPP.</summary>
    internal static class ChartDecryptor
    {
        internal static void DeriveKey(string seed, int count, ushort u16,
                                       out ulong s0, out ulong s1, out ulong s2, out ulong s3,
                                       out uint counter)
        {
            s0 = s1 = s2 = s3 = 0;
            counter = 0;
        }

        internal static ulong Gen(ulong s0, ulong s1, ulong s2, ulong s3, int idx, int k) => 0;

        internal static void Finalize(ref ulong s0, ref ulong s1, ref ulong s2, ref ulong s3,
                                      ref uint counter, ulong rdx, ulong r8, ulong r9) { }
    }
}
```

- [ ] **Step 3: 扩充测试项目并写会失败的集成测试**

`PackRulesTests.csproj` 增加 `<DefineConstants>$(DefineConstants);TESTS</DefineConstants>`，并链接：

```xml
<Compile Include="..\..\Source\Charts\ChartInfoReader.cs" Link="ChartInfoReader.cs" />
<Compile Include="..\..\Source\Charts\FalsumReader.cs" Link="FalsumReader.cs" />
<Compile Include="..\..\Source\Gameplay\FalsumChartCompiler.cs" Link="FalsumChartCompiler.cs" />
<Compile Include="..\..\Source\Diagnostics\Log.cs" Link="Log.cs" />
```

在 `Program.cs` 加 `using System.IO.Compression;`，并加 `WriteFalsum(folder, songId, cover, title, artist, int? rating, string designer)`：用 `ZipFile.Open(..., ZipArchiveMode.Create)` 写入 `song.json`（`songId`、`title`、`artist`、`audio.file = "a.wav"`、`durationMs = 60000`、可选 `cover.file = "c.png"`、`difficulties[{index:3, available:true, chart:"chart.txt"}]` 并可选 `rating`/`chartDesigner`）、`a.wav`、可选 `c.png`、`chart.txt`（内容 `chart(120,4)\ntap(0,1,0)\n`）。再加 `RoutingTests()`：

```csharp
string a = TempFolder(), b = TempFolder(), c = TempFolder(), d = TempFolder(), e = TempFolder();

// 1. falsum-only，有 cover
WriteFalsum(a, "songA", cover: true, title: "Title A", artist: "Artist A", rating: 15, designer: "D");
ChartInfoReader.TryRead(a, out ChartInfo ia, out string ra);
Check("falsum only", ia != null, ra);
Check("falsum title", ia?.Name == "Title A");
Check("falsum preview default", ia != null && ia.PreviewStart == 0f && ia.PreviewEnd == 60f);

// 2. falsum-only，无 cover → 跳过
WriteFalsum(b, "songB", cover: false, title: "B", artist: "B", rating: null, designer: null);
Check("no cover skipped", !ChartInfoReader.TryRead(b, out _, out string rb) && !string.IsNullOrEmpty(rb));

// 3. falsum + 只写展示键的 if
WriteFalsum(c, "songC", cover: true, title: "Old", artist: "Old", rating: null, designer: null);
File.WriteAllBytes(Path.Combine(c, "j.png"), new byte[] { 1 });
File.WriteAllText(Path.Combine(c, "if"),
    "{\"name\":\"New\",\"composer\":\"New Artist\",\"preview_seconds\":[5,30],"
    + "\"jacket\":\"j.png\",\"charter\":[\"a\",\"b\",\"c\",\"d\"],\"lv\":[1,2,3,4],"
    + "\"id\":\"other\",\"song\":\"x.wav\",\"chart\":\"y.spc\"}");
Check("overlay routing", ChartInfoReader.TryRead(c, out ChartInfo ic, out string rc), rc);
Check("overlay name wins", ic?.Name == "New");
Check("overlay id ignored", ic?.BaseName == "songC");
Check("falsum charts kept", ic != null && ic.ChartBytes[3] != null && !ic.NoChart[3]);
Check("overlay lv fills", ic?.Level?[3] == 4);
Check("overlay charter fills", ic?.Charters?[3] == "d");

// 4. if 的 jacket 指向不存在的文件 → 跳过
WriteFalsum(d, "songD", cover: true, title: "D", artist: "D", rating: null, designer: null);
File.WriteAllText(Path.Combine(d, "if"), "{\"jacket\":\"missing.png\"}");
Check("bad overlay skipped", !ChartInfoReader.TryRead(d, out _, out string rd) && !string.IsNullOrEmpty(rd));

// 5. falsum 坏掉 + 完整 if → 回退严格 if
File.WriteAllText(Path.Combine(e, "bad.falsum"), "not a zip");
WriteFullIf(e, "ifSong");
Check("broken falsum falls back", ChartInfoReader.TryRead(e, out ChartInfo ie, out string re), re);
Check("fallback uses if id", ie?.BaseName == "ifSong");
Check("fallback uses if charts", ie != null && ie.ChartBytes == null && ie.ChartPaths[3] != null);
```

`WriteFullIf(folder, id)` 在该目录写出 `song.wav`、`jacket.png`、`chart.spc`（内容随意），`if` 内容包含 `id`、`name`、`composer`、`song`、`jacket`、`preview_seconds`、`"chart": [null, null, null, "chart.spc"]`、`"charter": [null, null, null, "X"]`、`"lv": [null, null, null, 10]`。

- [ ] **Step 4: 运行测试确认失败**

Run: `dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj`
Expected: 编译失败（`HasPackage` 不存在、路由未实现）或断言失败。

- [ ] **Step 5: 实现 `HasPackage` 与路由**

`FalsumReader.HasPackage`：`Directory.GetFiles(folder, "*.falsum").Length > 0`，异常返回 false（`Diagnostics.Warn`）。

`ChartInfoReader.TryRead` 改成三分支：
- `!hasIf`：`FalsumReader.TryRead` 成功后，`info.HasJackets()` 不满足则返回 false（Debug 原因 `"the song has no cover and no jacket to fall back to"`）。
- `hasIf && hasFalsum`：先 `FalsumReader.TryRead`；失败则 `Diagnostics.Warn` 后把 `reason` 清空，继续走严格 `if`；成功则 `IfOverlay.TryRead` + `Apply` + `HasJackets()` 门槛，`Diagnostics.Info` 记一行“overlay applied / no display keys”。
- 其余：现有严格 `if` 逻辑原样。

- [ ] **Step 6: 运行测试确认通过**

Run: `dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj`
Expected: 全部 PASS（原 21 条 + Task 1/2/3 新用例）。

- [ ] **Step 7: 四个配置构建**

Run:
```cmd
build.bat
build.bat Debug
build.bat --BepInEx
build.bat --BepInEx Debug
```
Expected: 全部 `Build succeeded`；两个 Release 的产物检查均通过（若出现 `OFF-LIST`，说明新增了 Release 诊断串，按 Global Constraints 把它改回 `#if DEBUG`，不要加 allowlist）。

---

### Task 4: 文档与最终检查

**Files:**
- Modify: `docs/IF.EXAMPLE.md`
- Modify: `README.md`
- Modify: `AGENTS.md`

- [ ] **Step 1: `docs/IF.EXAMPLE.md` 增加一节**

在文件末尾加 `## 与 .falsum 同目录时`，写明：`.falsum` 提供谱面与音频；`if` 只覆盖 `name`、`composer`、`preview_seconds`、`illust`、`jacket`、`background`，并回填缺失的 `charter`/`lv`；`id`、`song`、`chart` 被忽略；没有曲绘的歌曲不会导入。给一个只写 `"preview_seconds"` 和 `"jacket"` 的示例。

- [ ] **Step 2: `README.md` 谱面规则补一条**

在“规则：”列表里加：`同一歌曲目录同时有 .falsum 和 if 时，.falsum 提供谱面与音频，if 只覆盖展示信息；没有曲绘的歌曲会被跳过。`

- [ ] **Step 3: `AGENTS.md` 曲包规则补一条**

加：`- .falsum 同目录的 if 只覆盖展示字段（name/composer/preview_seconds/illust/jacket/background，并回填 charter/lv）；id/song/chart 以 .falsum 为准；没有曲绘的歌曲跳过。`

- [ ] **Step 4: 最终验证**

Run:
```cmd
dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj
build.bat
```
Expected: 测试全绿；Release 构建与产物检查通过；`git status` 只显示本计划相关的源码、测试与文档修改。

- [ ] **Step 5: 交给用户在游戏里验证**

需要用户手动安装 DLL 并检查：falsum-only、if-only、两者共存（只补预览 / 补曲绘+画师+背景 / 补 `lv`+`charter`）、无 `cover` 的 falsum 被跳过、无 `cover` + `if` 给 `jacket` 正常导入。日志应出现 overlay 应用的 Debug 行；无异常。
