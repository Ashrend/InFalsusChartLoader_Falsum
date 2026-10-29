# AGENTS.md

## 项目

`InFalsusChartLoader_falsum`：In Falsus 的自制谱加载 Mod，支持 MelonLoader 和 BepInEx。

- 目标框架：`net6.0`，x64，启用 unsafe。
- 当前版本：`0.3.0`。
- 旧谱面格式：`Charts/<歌曲目录>/`，内含 `if` 或 `.falsum`，统一进入 `IFCL`。
- 新曲包格式：`Charts/[Pack]<曲包ID>/<歌曲目录>/`，曲包内支持 `if` 或 `.falsum`。
- 运行时通过游戏自己的 IL2CPP 方法解码谱面，并按字段名/方法名适配游戏更新。
- 本项目只维护 Mod 本身；Python 转换器已停止维护，不属于本项目，不要为其添加代码、测试或依赖。

## 目录

- `Source/Mod/`：MelonLoader/BepInEx 入口、`ModCore`。
- `Source/Charts/`：谱面发现、`if`/`.falsum` 读取、曲包解析、歌曲/曲包/结果注册。
- `Source/Gameplay/`：谱面解码、`_fA` 解密、C# `.falsum` 编译器、音频与流式资源。
- `Source/Interop/`：字段/方法解析、GameAssembly 地址、原生 hooks。
- `Source/Jacket/`、`Source/Background/`、`Source/Diagnostics/`：封面、背景、日志。
- `Source/Probe/`：仅 Debug 构建；Release 会排除。
- `Managed/`：MelonLoader/BepInEx 的编译引用，不是本项目源码。
- `Tests/PackRulesTests/`：曲包目录、manifest 和多语言回退的 C# 测试。
- `PACK.EXAMPLE.md`：自定义曲包目录、`pack` 和封面示例。
- `build.bat`：推荐构建入口，包含警告即错误和 Release 产物检查；额外允许项在 `release-allowlist.txt`。

## 构建

```cmd
build.bat                  :: MelonLoader Release（默认）
build.bat Debug            :: MelonLoader Debug
build.bat --BepInEx        :: BepInEx Release
build.bat --BepInEx Debug  :: BepInEx Debug
```

快速编译也可以：

```cmd
dotnet build InFalsusChartLoader_falsum.csproj -c Release -p:Loader=MelonLoader
```

- Release 产物：`bin\Release\InFalsusChartLoader_falsum.dll`
- Debug 产物：`bin\Debug\InFalsusChartLoader_falsum.dll`
- 构建后运行完整 `build.bat` 检查；不要绕过 Release 产物字符串检查。
- 不要手工修改 `bin/`、`obj/` 或 `Managed/`。

## 版本与安装

修改版本时必须同时更新：

- `InFalsusChartLoader_falsum.csproj` 的 `<AssemblyVersion>`
- `Source/Mod/ModCore.cs` 的 `Version`

安装由人手动完成，不要把构建产物自动复制进游戏。MelonLoader 放入：

`D:\SteamLibrary\steamapps\common\In Falsus\Mods`

## 测试

本项目不包含、不维护 Python 转换器，也没有 Python 测试步骤。

曲包规则测试：

```cmd
dotnet run --project Tests/PackRulesTests/PackRulesTests.csproj
```

游戏验证：

- 游戏路径：`D:\SteamLibrary\steamapps\common\In Falsus`
- 必须从 Steam 启动，或使用 `steam://rungameid/3971950`。
- 不要直接双击 `infalsus.exe`，也不要同时启动多个实例。
- 日志：`MelonLoader\Logs`
- 成功标志：`InFalsusChartLoader_falsum v0.3.0`、`packs: N imported, songs: M imported`、`packs: N registered (C custom, L IFCL), songs: M registered`、字段和 hooks 无 unresolved。
- 回归测试至少检查：旧 IFCL、两个以上自定义曲包、曲包排序、语言回退、有/无封面、track、lane、skyarea、宽 note、暂停/恢复。
- 不要在 `Charts` 留下测试谱面或测试资源。

## 曲包规则

- `[Pack]` 前缀按精确形式识别；后缀即逻辑曲包 ID。
- 曲包内 `pack` 为无扩展名 JSON，`name` 支持 `en`、`ja`、`ko`、`zh-Hant`、`zh-Hans`。
- 缺失语言按 `当前语言 → en → ja → ko → zh-Hant → zh-Hans` 回退。
- `pack_cover.png` 可选；缺失或无效时回退到游戏已有曲包视觉。
- 曲包按文件夹名排序；旧 `IFCL` 位于自定义曲包之后；没有旧歌曲时不创建空的 `IFCL`。
- 歌曲 ID 仍以 `if.id` / `.falsum` 的 `songId` 为准，文件夹名只用于定位。
- `pack` 缺失、JSON 无效或没有有效名称时跳过整个曲包；包内单曲无效只跳过该歌曲。
- 修改曲包注入、名称或视觉逻辑时，必须保留旧 IFCL 路径和游戏更新后的字段解析失败回退。

## 兼容性规则

- 游戏中大量字段/方法是混淆名；不要随意重命名。
- 优先通过 `FieldResolver`/`MethodResolver` 按名字读取，保留已有 raw offset 语义。
- 同时保持 `Entry.Melon.cs` 和 `Entry.BepInEx.cs` 可编译。
- Release 不允许携带 Debug 诊断字符串；调试输出放在 `Source/Probe/` 或 `#if DEBUG` 中。
- `.falsum` 的 C# 编译逻辑必须保持现有事件编码、line 重排和 seed 规则。
- 修改 `_fe/_Fe`、`_Ee/_ge`、lane/track 语义时，先补 C#/游戏回归验证再改实现。
- 原生 `.spc` 单独分发不保证可用；需要本 Mod 的运行时解码路径。
- 游戏更新后，先检查新版字段名、方法名、hook 地址和 note stride，再做适配。

## 工作习惯

- 改动前先读相关源码和当前日志；不要猜测游戏内部字段。
- 完成后至少运行一次构建和 `Tests/PackRulesTests`，并检查游戏日志。
- 不处理 Python 转换器的功能、兼容性或维护请求。
- 不要提交、推送或安装到游戏，除非用户明确要求。
- 不要提交密钥、账号信息或游戏原始资源。
